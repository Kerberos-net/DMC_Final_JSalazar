using System.Globalization;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using SmartNet.Contable.Core;
using SmartNet.Facturacion.Core;

namespace SmartNet.Api;

/// <summary>
/// tasks.md Phase 3 (PR 3) — <c>api-asientos</c> (spec.md), design D2/D3/D4/D6/D8: seis rutas, todas
/// thin -- deserializar, resolver el usuario autenticado, delegar en <see cref="ServicioDeAsientos"/>,
/// traducir con <see cref="IfMatch"/>/<see cref="ProblemasDeNegocio"/>. Ninguna regla contable ni
/// mapeo SQL vive aquí (ADR 0019).
/// </summary>
public static class AsientoEndpoints
{
    public static IEndpointRouteBuilder MapAsientoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/asientos/{id:long}", (Delegate)GetAsientoAsync).RequireAuthorization();
        app.MapPatch("/api/asientos/{id:long}", (Delegate)PatchAsientoAsync).RequireAuthorization();
        app.MapPost("/api/asientos/{id:long}/lineas", (Delegate)AgregarLineaAsync).RequireAuthorization();
        app.MapPatch("/api/asientos/{id:long}/lineas/{lineaId:long}", (Delegate)ActualizarLineaAsync).RequireAuthorization();
        app.MapDelete("/api/asientos/{id:long}/lineas/{lineaId:long}", (Delegate)EliminarLineaAsync).RequireAuthorization();
        app.MapPost("/api/asientos/{id:long}/reabrir", (Delegate)ReabrirAsync).RequireAuthorization();
        app.MapPost("/api/asientos/{id:long}/anular", (Delegate)AnularAsync).RequireAuthorization();

        return app;
    }

    /// <summary>tasks.md 3.8/3.9, spec.md asiento-lectura-api — lectura pura, sin comando: mismo
    /// patrón de <c>FacturaEndpoints.GetFacturaAsync</c> (ETag en el encabezado, nunca en el
    /// cuerpo).</summary>
    private static async Task<IResult> GetAsientoAsync(long id, HttpContext http, IFacturacionStore store, CancellationToken ct)
    {
        await using var uow = await store.AbrirAsync(ct);
        var asiento = await uow.CargarAsientoAsync(id, ct);
        if (asiento is null)
        {
            return Results.NotFound();
        }

        var lineas = await uow.CargarLineasPersistidasAsync(id, ct);
        http.Response.Headers.ETag = TokenDeConcurrencia.Codificar(asiento.Version);
        return Results.Ok(AsientoRespuesta.De(asiento, lineas));
    }

    private static async Task<IResult> PatchAsientoAsync(
        long id, CorreccionAsientoRequest cuerpo, HttpContext http, ServicioDeAsientos servicio, TimeProvider tiempo,
        CancellationToken ct)
    {
        if (!IfMatch.Requerido(http, out var version, out var error))
        {
            return error!;
        }

        var resultado = await servicio.ActualizarAsync(
            id, version, cuerpo.Campo, cuerpo.ValorOriginal, cuerpo.ValorNuevo, ResolverUsuarioId(http), tiempo.GetUtcNow(), ct);

        return await ResponderConAsientoActualizadoAsync(id, resultado, http, ct);
    }

    private static async Task<IResult> AgregarLineaAsync(
        long id, LineaAsientoRequest cuerpo, HttpContext http, ServicioDeAsientos servicio, TimeProvider tiempo,
        CancellationToken ct)
    {
        if (!IfMatch.Requerido(http, out var version, out var error))
        {
            return error!;
        }

        var (resultado, lineaId) = await servicio.AgregarLineaAsync(
            id, version, cuerpo.ALinea(), ResolverUsuarioId(http), tiempo.GetUtcNow(), ct);

        if (resultado is not ResultadoComando.Aplicado)
        {
            return ProblemasDeNegocio.Map(resultado);
        }

        var store = http.RequestServices.GetRequiredService<IFacturacionStore>();
        await using var uow = await store.AbrirAsync(ct);
        var asiento = await uow.CargarAsientoAsync(id, ct);
        if (asiento is null)
        {
            return Results.NotFound();
        }

        http.Response.Headers.ETag = TokenDeConcurrencia.Codificar(asiento.Version);
        return Results.Created($"/api/asientos/{id}/lineas/{lineaId}", new LineaCreadaRespuesta(lineaId!.Value));
    }

    private static async Task<IResult> ActualizarLineaAsync(
        long id, long lineaId, LineaAsientoRequest cuerpo, HttpContext http, ServicioDeAsientos servicio, TimeProvider tiempo,
        CancellationToken ct)
    {
        if (!IfMatch.Requerido(http, out var version, out var error))
        {
            return error!;
        }

        var resultado = await servicio.ActualizarLineaAsync(
            id, lineaId, version, cuerpo.ALinea(), ResolverUsuarioId(http), tiempo.GetUtcNow(), ct);

        return await ResponderConAsientoActualizadoAsync(id, resultado, http, ct);
    }

    private static async Task<IResult> EliminarLineaAsync(
        long id, long lineaId, HttpContext http, ServicioDeAsientos servicio, TimeProvider tiempo, CancellationToken ct)
    {
        if (!IfMatch.Requerido(http, out var version, out var error))
        {
            return error!;
        }

        var resultado = await servicio.EliminarLineaAsync(id, lineaId, version, ResolverUsuarioId(http), tiempo.GetUtcNow(), ct);

        return await ResponderConAsientoActualizadoAsync(id, resultado, http, ct);
    }

    /// <summary>spec.md "reabrir without motivo -&gt; 400 Bad Request" -- validado ANTES de exigir
    /// If-Match (mismo orden que <c>FacturaEndpoints.EliminarAdjuntoAsync</c>'s cheque de motivo).</summary>
    private static async Task<IResult> ReabrirAsync(
        long id, ReabrirAnularRequest cuerpo, HttpContext http, ServicioDeAsientos servicio, TimeProvider tiempo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cuerpo.Motivo))
        {
            return Results.BadRequest();
        }

        if (!IfMatch.Requerido(http, out var version, out var error))
        {
            return error!;
        }

        var resultado = await servicio.ReabrirAsync(id, version, cuerpo.Motivo, ResolverUsuarioId(http), tiempo.GetUtcNow(), ct);

        return await ResponderConAsientoActualizadoAsync(id, resultado, http, ct);
    }

    /// <summary>design D6 -- Motivo requerido para ANULACION (misma tabla que REAPERTURA), aunque
    /// <c>ServicioDeAsientos.AnularAsync</c> (PR 1) no lo valida internamente -- se refuerza aquí,
    /// igual que el motivo de <c>DeleteAdjunto</c> en <c>FacturaEndpoints</c> (deviación documentada
    /// en apply-progress: Core no lo comprueba, el host HTTP sí).</summary>
    private static async Task<IResult> AnularAsync(
        long id, ReabrirAnularRequest cuerpo, HttpContext http, ServicioDeAsientos servicio, TimeProvider tiempo, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cuerpo.Motivo))
        {
            return Results.BadRequest();
        }

        if (!IfMatch.Requerido(http, out var version, out var error))
        {
            return error!;
        }

        var resultado = await servicio.AnularAsync(id, version, cuerpo.Motivo, ResolverUsuarioId(http), tiempo.GetUtcNow(), ct);

        return await ResponderConAsientoActualizadoAsync(id, resultado, http, ct);
    }

    /// <summary>design D2 -- tras un comando exitoso, recarga el asiento y devuelve su ETag nuevo
    /// (mismo patrón que <c>FacturaEndpoints.PatchFacturaAsync</c>): el cliente necesita el rowversion
    /// fresco para la siguiente escritura CAS sobre el mismo recurso.
    ///
    /// PR5 gap closure (BACKLOG #12, Phase 5) -- también recarga las líneas por
    /// <see cref="IUnidadDeTrabajo.CargarLineasPersistidasAsync"/> y las expone en la misma respuesta:
    /// la pantalla de detalle necesita el <c>LineaId</c> estable de cada línea recién escrita para su
    /// próxima edición (spec.md api-asientos: "never position").</summary>
    private static async Task<IResult> ResponderConAsientoActualizadoAsync(
        long id, ResultadoComando resultado, HttpContext http, CancellationToken ct)
    {
        if (resultado is not ResultadoComando.Aplicado)
        {
            return ProblemasDeNegocio.Map(resultado);
        }

        var store = http.RequestServices.GetRequiredService<IFacturacionStore>();
        await using var uow = await store.AbrirAsync(ct);
        var asiento = await uow.CargarAsientoAsync(id, ct);
        if (asiento is null)
        {
            return Results.NotFound();
        }

        var lineas = await uow.CargarLineasPersistidasAsync(id, ct);

        http.Response.Headers.ETag = TokenDeConcurrencia.Codificar(asiento.Version);
        return Results.Ok(AsientoRespuesta.De(asiento, lineas));
    }

    private static long ResolverUsuarioId(HttpContext http)
    {
        var claim = http.User.FindFirst("usuarioId")?.Value;
        return long.TryParse(claim, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;
    }
}

internal sealed record CorreccionAsientoRequest(string Campo, string? ValorOriginal, string? ValorNuevo);

/// <summary>Cuerpo de <c>POST/PATCH /api/asientos/{id}/lineas[/{lineaId}]</c> -- espejo HTTP de
/// <see cref="LineaAsiento"/> (#8), que no se serializa directamente (sus enums <see cref="Bloque"/>/
/// <see cref="TipoLinea"/> se representan como texto ADR-0008-friendly, no como número).</summary>
internal sealed record LineaAsientoRequest(
    short Orden, string Bloque, string Tipo, decimal Debe, decimal Haber, string? CuentaCodigo,
    string? CuentaDescripcion, string? CtaReflejaCodigo, string? CtaPuenteCodigo)
{
    public LineaAsiento ALinea() => new(
        Orden,
        Bloque == "PRINCIPAL" ? global::SmartNet.Contable.Core.Bloque.Principal : global::SmartNet.Contable.Core.Bloque.Destino,
        Tipo == "D" ? TipoLinea.D : TipoLinea.H,
        Debe, Haber, CuentaCodigo, CuentaDescripcion, CtaReflejaCodigo, CtaPuenteCodigo);
}

internal sealed record LineaCreadaRespuesta(long LineaId);

internal sealed record ReabrirAnularRequest(string? Motivo);

/// <summary>Forma de respuesta de <c>GET</c>/<c>PATCH /api/asientos/{id}</c> y de los comandos de
/// línea/reabrir/anular (todos devuelven el asiento actualizado, igual que <c>FacturaRespuesta</c>).
///
/// <see cref="TipoCambioVenta"/> — tasks.md 3.10/3.11, design D4 (BACKLOG #12): la tasa de venta
/// congelada al generar/confirmar el asiento (<c>fact.AsientoContable.TipoCambioVenta</c>, ADR 0018
/// pt.1 — nunca "compra"), ya persistida desde #11 (<c>SqlUnidadDeTrabajo.cs</c>) y ya cargada en
/// <see cref="AsientoContable.TipoCambioVenta"/> -- este campo solo la expone, sin tocar el store.
/// <c>null</c> para una factura en PEN, nunca un valor fabricado.
///
/// DEVIACIÓN DOCUMENTADA de la spec delta `factura-respuesta-asiento-respuesta.md` (que pide el
/// campo en AMBAS `FacturaRespuesta` y `AsientoRespuesta`): design.md D4 corrige explícitamente la
/// propuesta -- `FacturaPersistida` no tiene esa columna, y exponer una tasa DISTINTA junto a la
/// congelada dejaría dos tasas divergiendo en pantalla. Se sigue design.md (la corrección
/// documentada), no la spec delta sin corregir; ver apply-progress para el detalle.
///
/// <see cref="BasePEN"/> / <see cref="IgvPEN"/> — BACKLOG #18 PR6, proyección aditiva de solo
/// lectura: <c>fact.AsientoContable.BasePEN</c> / <c>IgvPEN</c> ya se computan al generar el asiento
/// (ADR 0019 — la lógica contable vive en el núcleo) y ya se cargan en
/// <see cref="AsientoContable.BasePEN"/> / <see cref="AsientoContable.IgvPEN"/>; este campo solo las
/// expone para las filas de solo lectura "base imponible" / "IGV" de <c>factura-form</c>. No toca
/// ninguna ruta de escritura ni las hace editables.</summary>
internal sealed record AsientoRespuesta(
    long AsientoContableId, string Estado, string? NumeroAsiento, string ProveedorCodigo, DateOnly FechaContable,
    string? MotivoDescripcion, decimal? TipoCambioVenta, decimal BasePEN, decimal IgvPEN,
    IReadOnlyList<LineaRespuesta> Lineas)
{
    public static AsientoRespuesta De(AsientoPersistido asiento, IReadOnlyList<LineaPersistida> lineas) => new(
        asiento.AsientoContableId, asiento.Estado, asiento.NumeroAsiento, asiento.Asiento.ProveedorCodigo,
        asiento.Asiento.FechaContable, asiento.Asiento.MotivoDescripcion, asiento.Asiento.TipoCambioVenta,
        asiento.Asiento.BasePEN, asiento.Asiento.IgvPEN,
        lineas.Select(LineaRespuesta.De).ToArray());
}

/// <summary>Forma HTTP de una <see cref="LineaPersistida"/> -- espejo de <see cref="LineaAsientoRequest"/>
/// más su <see cref="LineaId"/> estable (spec.md api-asientos: "never position"). PR5 gap closure
/// (BACKLOG #12, Phase 5): Phase 3 nunca serializó las líneas en ninguna respuesta de
/// <c>AsientoEndpoints</c>, aunque <c>IUnidadDeTrabajo.CargarLineasPersistidasAsync</c> ya existía.</summary>
internal sealed record LineaRespuesta(
    long LineaId, short Orden, string Bloque, string Tipo, decimal Debe, decimal Haber, string? CuentaCodigo,
    string? CuentaDescripcion, string? CtaReflejaCodigo, string? CtaPuenteCodigo)
{
    public static LineaRespuesta De(LineaPersistida persistida) => new(
        persistida.LineaId,
        persistida.Linea.Orden,
        persistida.Linea.Bloque == global::SmartNet.Contable.Core.Bloque.Principal ? "PRINCIPAL" : "DESTINO",
        persistida.Linea.Tipo == TipoLinea.D ? "D" : "H",
        persistida.Linea.Debe, persistida.Linea.Haber, persistida.Linea.CuentaCodigo,
        persistida.Linea.CuentaDescripcion, persistida.Linea.CtaReflejaCodigo, persistida.Linea.CtaPuenteCodigo);
}
