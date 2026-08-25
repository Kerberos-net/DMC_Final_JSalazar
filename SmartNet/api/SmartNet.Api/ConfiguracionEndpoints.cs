using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartNet.Facturacion.Core;

namespace SmartNet.Api;

/// <summary>
/// tasks.md Phase 5 (PR 5) — <c>configuracion-api-spa</c> (spec.md, design D6): dedicated
/// GET/PUT over <c>fact.Configuracion</c>, a SEPARATE file from <c>IntegracionEndpoints.cs</c>
/// (owner-ratified answer #2 — design D6). Both routes require a session (spec.md "Authenticated
/// access only"); <c>PUT</c> is UPDATE-only — an unknown key is 404, never an INSERT
/// (<see cref="IConfiguracionRepository.ActualizarAsync"/>, design D6).
/// </summary>
public static class ConfiguracionEndpoints
{
    public static IEndpointRouteBuilder MapConfiguracionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/configuracion", (Delegate)ListarAsync).RequireAuthorization();
        app.MapPut("/api/configuracion/{seccion}/{clave}", (Delegate)ActualizarAsync).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> ListarAsync(
        string? seccion, IConfiguracionRepository repositorio, CancellationToken ct)
    {
        var entradas = await repositorio.ListarAsync(seccion, ct);
        return Results.Ok(entradas.Select(ConfiguracionEntradaRespuesta.De).ToArray());
    }

    private static async Task<IResult> ActualizarAsync(
        string seccion, string clave, ActualizarConfiguracionRequest cuerpo, HttpContext http,
        IConfiguracionRepository repositorio, CancellationToken ct)
    {
        var resultado = await repositorio.ActualizarAsync(seccion, clave, cuerpo.Valor, ResolverUsuarioId(http), ct);
        return resultado switch
        {
            ResultadoActualizacionConfiguracion.Actualizado => Results.Ok(),
            ResultadoActualizacionConfiguracion.NoEncontrado => Results.NotFound(),
            ResultadoActualizacionConfiguracion.ValorInvalido => ProblemasDeNegocio.ValorDeConfiguracionInvalido(),
            _ => throw new ArgumentOutOfRangeException(nameof(resultado)),
        };
    }

    // Mismo patrón que TipoCambioEndpoints.ResolverUsuarioId -- el claim "usuarioId" lo emite
    // SesionEndpoints al iniciar sesión; ausente/no numérico -> null (columna nullable, design D6).
    private static long? ResolverUsuarioId(HttpContext http)
    {
        var claim = http.User.FindFirst("usuarioId")?.Value;
        return long.TryParse(claim, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id)
            ? id
            : null;
    }
}

/// <summary>Cuerpo de <c>PUT /api/configuracion/{seccion}/{clave}</c> (spec.md
/// <c>{ "valor": string|null }</c> — <c>null</c> es legítimo: "usar ValorPorDefecto").</summary>
internal sealed record ActualizarConfiguracionRequest(string? Valor);

/// <summary>Forma de respuesta de <c>GET /api/configuracion</c> — un espejo 1:1 de
/// <see cref="ConfiguracionEntrada"/>, sin transformación (a diferencia de la "pill" derivada de
/// <c>IntegracionEstadoRespuesta</c>).</summary>
internal sealed record ConfiguracionEntradaRespuesta(
    string Seccion, string Clave, string Tipo, string? Valor, string? ValorPorDefecto, string Descripcion)
{
    public static ConfiguracionEntradaRespuesta De(ConfiguracionEntrada entrada) => new(
        entrada.Seccion, entrada.Clave, entrada.Tipo, entrada.Valor, entrada.ValorPorDefecto, entrada.Descripcion);
}
