using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartNet.Inbox.Core;

namespace SmartNet.Api;

/// <summary>
/// <c>GET /api/bandeja</c> (design D6 -- reuses ADR 0008's contract, #7-shaped, widened by #13).
/// Thin -- ADR 0019's demand of the API host: bind, validate the two malformed-request shapes
/// (<c>pagina</c>/<c>desde</c>/<c>hasta</c>) the design.md edge-cases table calls out, then delegate
/// every other decision to <see cref="IBandejaRepository"/> (<c>SqlBandejaRepository</c>, Phase 3);
/// never a second query surface over the same data.
/// </summary>
public static class BandejaEndpoints
{
    public static IEndpointRouteBuilder MapBandejaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/bandeja", (Delegate)GetBandejaAsync).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> GetBandejaAsync(
        string? estado,
        string? estadoDerivado,
        DateOnly? desde,
        DateOnly? hasta,
        string? proveedor,
        string? pagina,
        string? orden,
        IBandejaRepository bandeja,
        CancellationToken ct)
    {
        var numeroPagina = 1;
        if (pagina is not null && (!int.TryParse(pagina, out numeroPagina) || numeroPagina < 1))
        {
            return Results.Problem(
                title: "El parámetro 'pagina' debe ser un entero mayor o igual a 1.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (desde is not null && hasta is not null && desde > hasta)
        {
            return Results.Problem(
                title: "'desde' no puede ser posterior a 'hasta'.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (estado is not null && estadoDerivado is not null)
        {
            return Results.Problem(
                title: "No se pueden combinar 'estado' y 'estadoDerivado' en la misma consulta.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (estadoDerivado is not null && !EstadoDerivadoBandeja.EsValido(estadoDerivado))
        {
            return Results.Problem(
                title: "El parámetro 'estadoDerivado' debe ser uno de: "
                    + string.Join(", ", EstadoDerivadoBandeja.Valores) + ".",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var filtros = new FiltrosBandeja(
            estado, desde, hasta, proveedor, orden ?? "desc", numeroPagina, EstadoDerivado: estadoDerivado);
        var resultado = await bandeja.ListarAsync(filtros, ct);
        return Results.Ok(resultado);
    }
}
