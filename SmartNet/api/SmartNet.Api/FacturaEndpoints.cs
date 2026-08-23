using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SmartNet.Facturacion.Core;

namespace SmartNet.Api;

/// <summary>
/// tasks.md Phase 2 (PR 2) — <c>api-facturas</c> (spec.md), design D2/D3/D8: siete rutas, todas
/// thin -- deserializar, resolver el usuario autenticado, delegar en <see cref="ServicioDeFacturas"/>,
/// traducir con <see cref="IfMatch"/>/<see cref="ProblemasDeNegocio"/>. Ninguna regla contable ni
/// mapeo SQL vive aquí (ADR 0019).
/// </summary>
public static class FacturaEndpoints
{
    public static IEndpointRouteBuilder MapFacturaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/facturas/{id:long}", (Delegate)GetFacturaAsync).RequireAuthorization();
        app.MapPatch("/api/facturas/{id:long}", (Delegate)PatchFacturaAsync).RequireAuthorization();
        app.MapPost("/api/facturas/{id:long}/abrir", (Delegate)AbrirAsync).RequireAuthorization();
        app.MapPost("/api/facturas/{id:long}/validar", (Delegate)ValidarAsync).RequireAuthorization();
        app.MapPost("/api/facturas/{id:long}/descartar", (Delegate)DescartarAsync).RequireAuthorization();
        app.MapPost("/api/facturas/{id:long}/adjuntos", (Delegate)RegistrarAdjuntoAsync).RequireAuthorization();
        app.MapDelete("/api/facturas/{id:long}/adjuntos/{adjuntoId:long}", (Delegate)EliminarAdjuntoAsync)
            .RequireAuthorization();

        return app;
    }

    private static async Task<IResult> GetFacturaAsync(long id, HttpContext http, IFacturacionStore store, CancellationToken ct)
    {
        await using var uow = await store.AbrirAsync(ct);
        var factura = await uow.CargarFacturaAsync(id, ct);
        if (factura is null)
        {
            return Results.NotFound();
        }

        return ConEtag(http, factura);
    }

    private static async Task<IResult> PatchFacturaAsync(
        long id, CorreccionFacturaRequest cuerpo, HttpContext http, ServicioDeFacturas servicio,
        TimeProvider tiempo, CancellationToken ct)
    {
        if (!IfMatch.Requerido(http, out var version, out var error))
        {
            return error!;
        }

        var resultado = await servicio.PatchAsync(
            id, version, cuerpo.ACorreccion(), ResolverUsuarioId(http), tiempo.GetUtcNow(), ct);

        if (resultado is not ResultadoComando.Aplicado)
        {
            return ProblemasDeNegocio.Map(resultado);
        }

        var store = http.RequestServices.GetRequiredService<IFacturacionStore>();
        await using var uow = await store.AbrirAsync(ct);
        var factura = await uow.CargarFacturaAsync(id, ct);
        return factura is null ? Results.NotFound() : ConEtag(http, factura);
    }

    private static async Task<IResult> AbrirAsync(long id, ServicioDeFacturas servicio, CancellationToken ct)
    {
        var resultado = await servicio.AbrirAsync(id, ct);
        return resultado is ResultadoComando.Aplicado ? Results.Ok() : ProblemasDeNegocio.Map(resultado);
    }

    private static async Task<IResult> ValidarAsync(
        long id, DateOnly fechaCorteContable, HttpContext http, ServicioDeFacturas servicio, TimeProvider tiempo,
        CancellationToken ct)
    {
        var resultado = await servicio.ValidarPorFacturaAsync(
            id, fechaCorteContable, tiempo.GetUtcNow(), ResolverUsuarioId(http), ct);

        return resultado is ResultadoComando.Aplicado ? Results.Ok() : ProblemasDeNegocio.Map(resultado);
    }

    private static async Task<IResult> DescartarAsync(
        long id, HttpContext http, ServicioDeFacturas servicio, CancellationToken ct)
    {
        if (!IfMatch.Requerido(http, out var version, out var error))
        {
            return error!;
        }

        var resultado = await servicio.DescartarAsync(id, version, ct);
        return resultado is ResultadoComando.Aplicado ? Results.Ok() : ProblemasDeNegocio.Map(resultado);
    }

    private static async Task<IResult> RegistrarAdjuntoAsync(
        long id, RegistrarAdjuntoRequest cuerpo, HttpContext http, ServicioDeFacturas servicio, TimeProvider tiempo,
        CancellationToken ct)
    {
        var adjunto = new AdjuntoManual(
            AdjuntoManualId: 0, FacturaId: id, NombreArchivo: cuerpo.NombreArchivo, RutaRelativa: cuerpo.RutaRelativa,
            MimeType: cuerpo.MimeType, TamanoBytes: cuerpo.TamanoBytes, SubidoPorUsuarioId: ResolverUsuarioId(http),
            SubidoEn: tiempo.GetUtcNow(), EliminadoEn: null);

        var resultado = await servicio.RegistrarAdjuntoAsync(id, adjunto, ct);
        return resultado is ResultadoComando.Aplicado ? Results.Ok() : ProblemasDeNegocio.Map(resultado);
    }

    private static async Task<IResult> EliminarAdjuntoAsync(
        long id, long adjuntoId, [FromBody] EliminarAdjuntoRequest cuerpo, HttpContext http,
        ServicioDeFacturas servicio, TimeProvider tiempo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cuerpo.Motivo))
        {
            return Results.BadRequest();
        }

        var resultado = await servicio.EliminarAdjuntoAsync(
            id, adjuntoId, ResolverUsuarioId(http), cuerpo.Motivo, tiempo.GetUtcNow(), ct);
        return resultado is ResultadoComando.Aplicado ? Results.Ok() : ProblemasDeNegocio.Map(resultado);
    }

    /// <summary>design D2 -- el ETag va en el encabezado HTTP estándar
    /// (<see cref="TokenDeConcurrencia.Codificar"/>), nunca duplicado en el cuerpo.</summary>
    private static IResult ConEtag(HttpContext http, FacturaPersistida factura)
    {
        http.Response.Headers.ETag = TokenDeConcurrencia.Codificar(factura.Version);
        return Results.Ok(FacturaRespuesta.De(factura));
    }

    private static long ResolverUsuarioId(HttpContext http)
    {
        var claim = http.User.FindFirst("usuarioId")?.Value;
        return long.TryParse(claim, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;
    }
}

internal sealed record CorreccionFacturaRequest(
    string? ProveedorCodigo,
    string? RucProveedor,
    string? Moneda,
    decimal? TotalOrig,
    DateOnly? FechaEmision,
    int? Motivo,
    string? Afectacion)
{
    public CorreccionFactura ACorreccion() =>
        new(ProveedorCodigo, RucProveedor, Moneda, TotalOrig, FechaEmision, Motivo, Afectacion);
}

internal sealed record RegistrarAdjuntoRequest(string NombreArchivo, string RutaRelativa, string MimeType, long TamanoBytes);

internal sealed record EliminarAdjuntoRequest(string? Motivo);

/// <summary>Forma de respuesta de <c>GET</c>/<c>PATCH /api/facturas/{id}</c>.</summary>
internal sealed record FacturaRespuesta(
    long FacturaId, string Estado, string ProveedorCodigo, string? RucProveedor, string TipoComprobante,
    string? Numero, decimal TotalOrig, string Moneda, DateOnly FechaEmision, int? Motivo, string? Afectacion)
{
    public static FacturaRespuesta De(FacturaPersistida factura) => new(
        factura.FacturaId, factura.Estado, factura.ProveedorCodigo, factura.RucProveedor, factura.TipoComprobante,
        factura.Numero, factura.TotalOrig, factura.Moneda, factura.FechaEmision, factura.Motivo, factura.Afectacion);
}
