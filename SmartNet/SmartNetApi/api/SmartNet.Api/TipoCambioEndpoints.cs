using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartNet.Exportacion.Infrastructure;
using SmartNet.TiposCambio.Core;

namespace SmartNet.Api;

/// <summary>
/// tasks.md Phase 4 (PR 4) — <c>tipos-de-cambio</c> (spec.md "POST /api/tipos-cambio exposes
/// carga-manual over HTTP with problem+json errors"): one thin route delegating to the existing
/// <see cref="ITipoCambioRepository.CargarManualAsync"/> adapter from item #4 (never a new Core
/// rule -- this is an HTTP wrapper around a repository that already exists). No <c>If-Match</c>
/// concurrency here: <c>fact.TipoCambio</c> has no <c>Version</c> column, and its PK (Fecha, Origen)
/// is the only concurrency guard the design calls for (design D2 lists 6 mutating surfaces plus
/// every GET -- tipos-cambio load is not one of them).
/// </summary>
public static class TipoCambioEndpoints
{
    public static IEndpointRouteBuilder MapTipoCambioEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/tipos-cambio", (Delegate)CargarManualAsync).RequireAuthorization();
        app.MapGet("/api/tipos-cambio", (Delegate)ListarHistoricoAsync).RequireAuthorization();
        app.MapGet("/api/tipos-cambio/exportacion", (Delegate)ExportarHistoricoAsync).RequireAuthorization();

        return app;
    }

    // BACKLOG #22 PR7 — catalog-queries-api spec req 5: read-only history for the SPA tipo de
    // cambio screen. `desde` and `hasta` are BOTH required ISO dates; the inclusive span is capped
    // at 366 days. Range validation lives HERE, never in the core (ADR 0019 / a span cap would
    // invent a rule REGLAS.md does not have). `origen` is emitted as the string "SBS"/"MANUAL" via
    // an explicit mapper — the enum would serialize as 0/1. No origin filter; both origins per date.
    private const int MaximoDiasRango = 366;

    private static async Task<IResult> ListarHistoricoAsync(
        string? desde, string? hasta, ITipoCambioRepository repositorio, CancellationToken ct)
    {
        if (!TryResolverRango(desde, hasta, out var desdeFecha, out var hastaFecha))
        {
            return Results.BadRequest();
        }

        var historico = await repositorio.ListarHistoricoAsync(desdeFecha, hastaFecha, ct);
        var items = historico
            .Select(t => new TipoCambioHistoricoResultado(
                t.Fecha, OrigenComoTexto(t.Origen), t.Compra, t.Venta, t.FechaConsulta))
            .ToArray();

        return Results.Ok(new TipoCambioHistoricoRespuesta(items));
    }

    // BACKLOG #22 PR7 — catalog-queries-api spec req 6 / ADR 0021: real .xlsx of the full range
    // (same 400s as the list route). ADR 0021 decision 4: no user input reaches Content-Disposition
    // — the filename is a constant plus the server date from the registered TimeProvider.
    private static async Task<IResult> ExportarHistoricoAsync(
        string? desde, string? hasta, ITipoCambioRepository repositorio, TimeProvider reloj, CancellationToken ct)
    {
        if (!TryResolverRango(desde, hasta, out var desdeFecha, out var hastaFecha))
        {
            return Results.BadRequest();
        }

        var historico = await repositorio.ListarHistoricoAsync(desdeFecha, hastaFecha, ct);
        var filas = historico
            .Select(t => (IReadOnlyList<string>)new[]
            {
                t.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                OrigenComoTexto(t.Origen),
                t.Compra.ToString(CultureInfo.InvariantCulture),
                t.Venta.ToString(CultureInfo.InvariantCulture),
                t.FechaConsulta.ToString("s", CultureInfo.InvariantCulture),
            })
            .ToArray();

        using var buffer = new MemoryStream();
        ExportadorXlsx.Escribir(buffer, filas, new[] { "Fecha", "Origen", "Compra", "Venta", "Fecha de consulta" });

        var hoy = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);
        return Results.File(
            buffer.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileDownloadName: $"tipos-cambio-{hoy:yyyy-MM-dd}.xlsx");
    }

    private static bool TryResolverRango(string? desde, string? hasta, out DateOnly desdeFecha, out DateOnly hastaFecha)
    {
        desdeFecha = default;
        hastaFecha = default;

        if (string.IsNullOrWhiteSpace(desde) || string.IsNullOrWhiteSpace(hasta)
            || !DateOnly.TryParseExact(desde, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out desdeFecha)
            || !DateOnly.TryParseExact(hasta, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out hastaFecha))
        {
            return false;
        }

        if (hastaFecha < desdeFecha || hastaFecha.DayNumber - desdeFecha.DayNumber + 1 > MaximoDiasRango)
        {
            return false;
        }

        return true;
    }

    private static string OrigenComoTexto(OrigenTipoCambio origen) => origen switch
    {
        OrigenTipoCambio.Sbs => "SBS",
        OrigenTipoCambio.Manual => "MANUAL",
        _ => throw new ArgumentOutOfRangeException(nameof(origen), origen, "Origen fuera de ('SBS','MANUAL')."),
    };

    private static async Task<IResult> CargarManualAsync(
        TipoCambioManualRequest cuerpo, HttpContext http, ITipoCambioRepository repositorio, TimeProvider tiempo,
        CancellationToken ct)
    {
        if (cuerpo.Tasa is null || cuerpo.Tasa <= 0)
        {
            return Results.BadRequest();
        }

        var usuarioId = ResolverUsuarioId(http);
        var resultado = await repositorio.CargarManualAsync(
            cuerpo.Fecha, cuerpo.Tasa.Value, cuerpo.Tasa.Value, tiempo.GetUtcNow().UtcDateTime, usuarioId, ct);

        return resultado switch
        {
            ResultadoCargaManual.Cargada => Results.Created($"/api/tipos-cambio/{cuerpo.Fecha:yyyy-MM-dd}", null),
            ResultadoCargaManual.YaExistia => ProblemasDeNegocio.TipoCambioManualYaExistente(),
            _ => throw new ArgumentOutOfRangeException(nameof(resultado)),
        };
    }

    private static long? ResolverUsuarioId(HttpContext http)
    {
        var claim = http.User.FindFirst("usuarioId")?.Value;
        return long.TryParse(claim, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }
}

/// <summary>Cuerpo de <c>POST /api/tipos-cambio</c> — spec.md's literal scenario body,
/// <c>{ "fecha": ..., "tasa": ... }</c>: un solo <c>Tasa</c>, no <c>Compra</c>/<c>Venta</c>
/// separados (deviación documentada en apply-progress: <c>fact.TipoCambio</c> técnicamente
/// distingue ambos, pero ninguna de las 4 superficies de este cambio necesita esa distinción para
/// una carga MANUAL -- se replica el mismo valor en ambas columnas).</summary>
internal sealed record TipoCambioManualRequest(DateOnly Fecha, decimal? Tasa);

/// <summary>Una fila de <c>GET /api/tipos-cambio</c> (BACKLOG #22): <c>origen</c> como texto
/// <c>"SBS"</c>/<c>"MANUAL"</c>, nunca el ordinal del enum.</summary>
internal sealed record TipoCambioHistoricoResultado(
    DateOnly Fecha, string Origen, decimal Compra, decimal Venta, DateTime FechaConsulta);

/// <summary>Cuerpo de <c>GET /api/tipos-cambio?desde=&amp;hasta=</c>: el histórico del rango,
/// ambos orígenes por fecha, ordenado por fecha y luego origen. Sin paginar (rango acotado a
/// 366 días).</summary>
internal sealed record TipoCambioHistoricoRespuesta(IReadOnlyList<TipoCambioHistoricoResultado> Items);
