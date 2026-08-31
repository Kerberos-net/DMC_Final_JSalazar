using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartNet.Exportacion.Infrastructure;
using SmartNet.Facturacion.Core.RegistroCompra;

namespace SmartNet.Api;

/// <summary>
/// BACKLOG #23 — <c>registro-compra-api</c> (spec req 1/2/3/5): three thin, authenticated,
/// read-only GET routes over the purchase register, each delegating to
/// <see cref="IRegistroCompraRepository"/> (<c>SqlRegistroCompraRepository</c>). No accounting rule
/// and no SQL live here (ADR 0019).
///
/// The one validation the host owns is the <c>periodo</c> wire form: <see cref="PeriodoContable.TryParse"/>
/// (a pure Core call — no clock) then a 400 RFC 9457 problem-details response on failure. The
/// filename for the export is REBUILT from the parsed <c>Anio</c>/<c>Mes</c> ints, so no user input
/// ever reaches <c>Content-Disposition</c> (design D5).
/// </summary>
public static class RegistroCompraEndpoints
{
    // design / Open Question resolved: adopt #22's rows-per-page allow-list and default verbatim
    // (CatalogoEndpoints.TamaniosValidos = {6,10,20,50}, default 20). Anything else is a 400.
    private static readonly HashSet<int> TamaniosValidos = new() { 6, 10, 20, 50 };
    private const int TamanioPorDefecto = 20;

    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static IEndpointRouteBuilder MapRegistroCompraEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/registro-compra", (Delegate)ListarAsync).RequireAuthorization();
        app.MapGet("/api/registro-compra/{asientoId:long}", (Delegate)ObtenerAsync).RequireAuthorization();
        app.MapGet("/api/registro-compra/export", (Delegate)ExportarAsync).RequireAuthorization();
        return app;
    }

    private static async Task<IResult> ListarAsync(
        string? periodo, int? pagina, int? tamanioPagina,
        IRegistroCompraRepository repositorio, CancellationToken ct)
    {
        if (!PeriodoContable.TryParse(periodo, out var periodoContable))
        {
            return PeriodoInvalido();
        }

        var tamanio = tamanioPagina ?? TamanioPorDefecto;
        if (!TamaniosValidos.Contains(tamanio))
        {
            return Results.Problem(
                title: "El parámetro 'tamanioPagina' debe ser uno de: 6, 10, 20, 50.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var nroPagina = pagina is > 0 ? pagina.Value : 1;

        var page = await repositorio.ListarPeriodoAsync(periodoContable!.Value, nroPagina, tamanio, ct);
        return Results.Ok(page);
    }

    private static async Task<IResult> ObtenerAsync(
        long asientoId, IRegistroCompraRepository repositorio, CancellationToken ct)
    {
        var detalle = await repositorio.ObtenerAsync(asientoId, ct);
        return detalle is null ? Results.NotFound() : Results.Ok(detalle);
    }

    private static async Task<IResult> ExportarAsync(
        string? periodo, IRegistroCompraRepository repositorio, CancellationToken ct)
    {
        if (!PeriodoContable.TryParse(periodo, out var periodoContable))
        {
            return PeriodoInvalido();
        }

        var filas = await repositorio.ListarPeriodoCompletoAsync(periodoContable!.Value, ct);

        var registros = filas
            .Select(c => (IReadOnlyList<string>)new[]
            {
                c.FechaContable.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                c.NumeroAsiento ?? string.Empty,
                c.OrigenLibro,
                c.NumeroComprobante ?? string.Empty,
                c.ProveedorCodigo,
                c.ProveedorNombre ?? string.Empty,
                c.Glosa ?? string.Empty,
                Formato(c.TipoCambioVenta, "F6"),
                Formato(c.BasePEN, "F2"),
                Formato(c.IgvPEN, "F2"),
                Formato(c.NetoPEN, "F2"),
            })
            .ToArray();

        using var buffer = new MemoryStream();
        ExportadorXlsx.Escribir(buffer, registros, new[]
        {
            "Fecha contable", "Numero de asiento", "Origen libro", "Numero de comprobante",
            "Codigo proveedor", "Proveedor", "Glosa", "Tipo de cambio venta",
            "Base PEN", "IGV PEN", "Neto PEN",
        });

        // design D5: filename reconstructed from the PARSED ints, never from the raw query string.
        var p = periodoContable!.Value;
        return Results.File(
            buffer.ToArray(),
            XlsxMime,
            fileDownloadName: $"registro-compra-{p.Anio:D4}-{p.Mes:D2}.xlsx");
    }

    private static string Formato(decimal? valor, string formato) =>
        valor is null ? string.Empty : valor.Value.ToString(formato, CultureInfo.InvariantCulture);

    private static IResult PeriodoInvalido() =>
        Results.Problem(
            title: "El parámetro 'periodo' debe tener el formato YYYY-MM (por ejemplo 2026-08).",
            statusCode: StatusCodes.Status400BadRequest);
}
