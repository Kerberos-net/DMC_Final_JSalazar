using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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

        return app;
    }

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
