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
        app.MapGet("/api/facturas/{id:long}/asiento", (Delegate)GetAsientoDeFacturaAsync).RequireAuthorization();
        app.MapPatch("/api/facturas/{id:long}", (Delegate)PatchFacturaAsync).RequireAuthorization();
        app.MapPost("/api/facturas/{id:long}/abrir", (Delegate)AbrirAsync).RequireAuthorization();
        app.MapPost("/api/facturas/{id:long}/validar", (Delegate)ValidarAsync).RequireAuthorization();
        app.MapPost("/api/facturas/{id:long}/descartar", (Delegate)DescartarAsync).RequireAuthorization();
        app.MapPost("/api/facturas/{id:long}/confirmar-afectacion", (Delegate)ConfirmarAfectacionAsync)
            .RequireAuthorization();
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

    /// <summary>tasks.md 3.8/3.9, spec.md asiento-lectura-api, design D3 — resuelve
    /// <c>FacturaId -&gt; AsientoContableId</c> vigente por HTTP y devuelve el asiento completo con su
    /// propio ETag (la pantalla lo necesita de todos modos para editar líneas): 404 si la FACTURA no
    /// existe; <c>200</c> con <see cref="FacturaAsientoRespuesta.AsientoContableId"/> en <c>null</c>
    /// (sin ETag) si la factura existe pero aún no tiene asiento (<c>abrir</c> no llamado) --
    /// distinto de un 404, exactamente como pide el spec.</summary>
    private static async Task<IResult> GetAsientoDeFacturaAsync(
        long id, HttpContext http, IFacturacionStore store, CancellationToken ct)
    {
        await using var uow = await store.AbrirAsync(ct);
        var factura = await uow.CargarFacturaAsync(id, ct);
        if (factura is null)
        {
            return Results.NotFound();
        }

        var asientoId = await uow.ObtenerAsientoVigenteIdAsync(id, ct);
        if (asientoId is null)
        {
            return Results.Ok(new FacturaAsientoRespuesta(null, null));
        }

        var asiento = await uow.CargarAsientoAsync(asientoId.Value, ct);
        if (asiento is null)
        {
            // Defensivo: UQ_Asiento_Vigente garantiza a lo sumo un asiento no ANULADO por factura,
            // pero si el id resuelto ya no carga (borrado concurrente, teóricamente imposible hoy),
            // se degrada al mismo "sin vigente" en vez de un 500.
            return Results.Ok(new FacturaAsientoRespuesta(null, null));
        }

        var lineas = await uow.CargarLineasPersistidasAsync(asiento.AsientoContableId, ct);
        http.Response.Headers.ETag = TokenDeConcurrencia.Codificar(asiento.Version);
        return Results.Ok(new FacturaAsientoRespuesta(asiento.AsientoContableId, AsientoRespuesta.De(asiento, lineas)));
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

    /// <summary>diseno-visual-spa-item-12 (design D10) -- mismo shape CAS que <see cref="DescartarAsync"/>:
    /// If-Match obligatorio, delega en <see cref="ServicioDeFacturas.ConfirmarAfectacionAsync"/>.
    /// El gate <c>CasoConflicto.AfectacionNoVerificada</c> permanece deliberadamente dormido -- esta
    /// ruta solo registra la afirmación del asistente, no desbloquea ni bloquea <c>validar</c>.</summary>
    private static async Task<IResult> ConfirmarAfectacionAsync(
        long id, ConfirmarAfectacionRequest cuerpo, HttpContext http, ServicioDeFacturas servicio, TimeProvider tiempo,
        CancellationToken ct)
    {
        if (!IfMatch.Requerido(http, out var version, out var error))
        {
            return error!;
        }

        var resultado = await servicio.ConfirmarAfectacionAsync(
            id, version, cuerpo.EsMixta, ResolverUsuarioId(http), tiempo.GetUtcNow(), ct);
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
    string? Afectacion,
    // BACKLOG #18 PR5 (api-facturas delta) -- 2 params TRAILING con default: los ~call sites
    // posicionales de 7 argumentos existentes (tests incluidos) siguen compilando.
    string? TipoComprobante = null,
    string? Numero = null,
    // BACKLOG #19 -- base/IGV como par atomico (design D1) + glosa (schema 021). TRAILING con
    // default: los call sites posicionales existentes (tests incluidos) siguen compilando.
    decimal? BaseImponible = null,
    decimal? Igv = null,
    string? Glosa = null)
{
    public CorreccionFactura ACorreccion() =>
        new(ProveedorCodigo, RucProveedor, Moneda, TotalOrig, FechaEmision, Motivo, Afectacion, TipoComprobante, Numero,
            BaseImponible, Igv, Glosa);
}

internal sealed record RegistrarAdjuntoRequest(string NombreArchivo, string RutaRelativa, string MimeType, long TamanoBytes);

internal sealed record EliminarAdjuntoRequest(string? Motivo);

/// <summary>diseno-visual-spa-item-12 (design D10, REGLAS.md §8 "La factura mixta") -- la
/// afirmación del asistente tras revisar el documento: <c>true</c> si declara más de un código de
/// afectación, <c>false</c> si confirma uno solo. Nunca se infiere del lado del servidor.</summary>
internal sealed record ConfirmarAfectacionRequest(bool EsMixta);

/// <summary>Forma de respuesta de <c>GET</c>/<c>PATCH /api/facturas/{id}</c>. diseno-visual-spa-
/// item-12 (design D9, spec.md api-facturas delta): las 4 columnas indicadoras van AL FINAL,
/// proyección puramente aditiva -- ningún campo existente cambia de nombre, tipo o significado.</summary>
internal sealed record FacturaRespuesta(
    long FacturaId, string Estado, string ProveedorCodigo, string? RucProveedor, string TipoComprobante,
    string? Numero, decimal TotalOrig, string Moneda, DateOnly FechaEmision, int? Motivo, string? Afectacion,
    bool EsProveedorGenerico, bool PosibleDuplicado, bool TieneCamposNoExtraidos, bool? AfectacionMixta,
    // BACKLOG #19 (design D8/D9) -- lista por campo del OCR + glosa, AL FINAL, puramente aditivo.
    // TieneCamposNoExtraidos se conserva: es el fallback coarse para facturas pre-021.
    string[] CamposNoExtraidos, string? Glosa)
{
    public static FacturaRespuesta De(FacturaPersistida factura) => new(
        factura.FacturaId, factura.Estado, factura.ProveedorCodigo, factura.RucProveedor, factura.TipoComprobante,
        factura.Numero, factura.TotalOrig, factura.Moneda, factura.FechaEmision, factura.Motivo, factura.Afectacion,
        factura.EsProveedorGenerico, factura.PosibleDuplicado, factura.TieneCamposNoExtraidos, factura.AfectacionMixta,
        factura.CamposNoExtraidos?.ToArray() ?? Array.Empty<string>(), factura.Glosa);
}

/// <summary>Forma de respuesta de <c>GET /api/facturas/{id}/asiento</c> (design D3) --
/// <see cref="AsientoContableId"/> y <see cref="Asiento"/> en <c>null</c> juntos significan "la
/// factura existe pero no tiene asiento vigente", distinto del 404 de factura desconocida.</summary>
internal sealed record FacturaAsientoRespuesta(long? AsientoContableId, AsientoRespuesta? Asiento);
