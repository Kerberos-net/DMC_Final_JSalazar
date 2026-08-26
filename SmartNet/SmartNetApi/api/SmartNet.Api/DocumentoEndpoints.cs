using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using SmartNet.Facturacion.Core;

namespace SmartNet.Api;

/// <summary>
/// tasks.md Phase 3 (PR 3, BACKLOG #12) — <c>documento-contenido-api</c> /
/// <c>documentos-lista-unificada-api</c> (design D1/D2): dos rutas, thin -- resolver el id, delegar
/// en <see cref="IUnidadDeTrabajo"/>, traducir a HTTP. La decisión de qué es "seguro servir" vive en
/// <see cref="DocumentoContenido"/> (sin I/O, unit-testable); este archivo es el único lugar que
/// abre un archivo de verdad (ADR 0013's "disco compartido").
///
/// Blocking Architecture Finding (design.md): ningún miembro de este archivo lee
/// <c>fact.DocumentoRecibido</c> -- las dos fuentes son <c>fact.DocumentoFactura</c> (proyección
/// .NET-owned, schema 016) y <c>fact.AdjuntoManual</c>, ambas leídas vía
/// <see cref="IUnidadDeTrabajo.CargarDocumentosFacturaAsync"/>/<see cref="IUnidadDeTrabajo.CargarAdjuntosDeFacturaAsync"/>
/// (ADR 0003 §Privadas).
/// </summary>
public static class DocumentoEndpoints
{
    private const string PrefijoManual = "manual";
    private const string PrefijoIngesta = "ingesta";

    public static IEndpointRouteBuilder MapDocumentoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/documentos/{id}/contenido", (Delegate)ObtenerContenidoAsync).RequireAuthorization();
        app.MapGet("/api/facturas/{facturaId:long}/documentos", (Delegate)ObtenerDocumentosAsync).RequireAuthorization();

        return app;
    }

    /// <summary>
    /// design D2 threat matrix, en orden: id no reconocible -&gt; 404; fila inexistente -&gt; 404;
    /// <see cref="DocumentoContenido.ResolverRutaSegura"/> devuelve <c>null</c> (traversal) -&gt; 404;
    /// archivo ausente en disco (huérfano) -&gt; 404, la ruta NUNCA se refleja en el cuerpo de
    /// ninguna de estas cuatro respuestas -- todas son <see cref="Results.NotFound()"/> puro, sin
    /// detalle. Solo al llegar aquí con archivo real se sirven los bytes, con el MIME de la
    /// allow-list (<see cref="DocumentoContenido.ContentTypeFor"/>), <c>nosniff</c> y
    /// <c>Content-Disposition: inline</c> (visor mismo-origen, ADR 0013).
    /// </summary>
    private static async Task<IResult> ObtenerContenidoAsync(
        string id, HttpContext http, IFacturacionStore store, IConfiguration configuracion, CancellationToken ct)
    {
        if (!TryParseDocumentoId(id, out var origenIngesta, out var idNumerico))
        {
            return Results.NotFound();
        }

        await using var uow = await store.AbrirAsync(ct);

        string nombreArchivo;
        string mimeType;
        string rutaRelativa;

        if (origenIngesta)
        {
            var documento = await uow.CargarDocumentoFacturaPorIdAsync(idNumerico, ct);
            if (documento is null)
            {
                return Results.NotFound();
            }

            (nombreArchivo, mimeType, rutaRelativa) = (documento.NombreArchivo, documento.MimeType, documento.RutaRelativa);
        }
        else
        {
            var adjunto = await uow.CargarAdjuntoPorIdAsync(idNumerico, ct);
            if (adjunto is null)
            {
                return Results.NotFound();
            }

            (nombreArchivo, mimeType, rutaRelativa) = (adjunto.NombreArchivo, adjunto.MimeType, adjunto.RutaRelativa);
        }

        var raiz = DocumentoStorageOptions.Resolve(configuracion);
        var rutaSegura = DocumentoContenido.ResolverRutaSegura(raiz, rutaRelativa);
        if (rutaSegura is null || !File.Exists(rutaSegura))
        {
            return Results.NotFound();
        }

        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        http.Response.Headers["Content-Disposition"] = "inline";
        return Results.File(rutaSegura, DocumentoContenido.ContentTypeFor(mimeType), enableRangeProcessing: false);
    }

    /// <summary>
    /// spec.md documentos-lista-unificada-api: unión de <c>fact.DocumentoFactura</c> (origen
    /// <see cref="DocumentoRespuesta.OrigenIngesta"/>) y <c>fact.AdjuntoManual</c> no eliminado
    /// (origen <see cref="DocumentoRespuesta.OrigenManual"/>), sin duplicados (los dos orígenes
    /// tienen espacios de id disjuntos -- se prefijan). Una factura sin documentos -&gt; lista vacía,
    /// nunca error; una factura con documentos pre-schema-016 sin proyección degrada silenciosamente
    /// a solo <c>AdjuntoManual</c> (no hay backfill posible, ADR 0003).
    /// </summary>
    private static async Task<IResult> ObtenerDocumentosAsync(long facturaId, IFacturacionStore store, CancellationToken ct)
    {
        await using var uow = await store.AbrirAsync(ct);

        var documentos = await uow.CargarDocumentosFacturaAsync(facturaId, ct);
        var adjuntos = await uow.CargarAdjuntosDeFacturaAsync(facturaId, ct);

        var respuesta = documentos
            .Select(d => new DocumentoRespuesta(
                $"{PrefijoIngesta}-{d.DocumentoFacturaId}", DocumentoRespuesta.OrigenIngesta, d.NombreArchivo, d.MimeType, d.CreadoEn))
            .Concat(adjuntos.Select(a => new DocumentoRespuesta(
                $"{PrefijoManual}-{a.AdjuntoManualId}", DocumentoRespuesta.OrigenManual, a.NombreArchivo, a.MimeType, a.SubidoEn)))
            .OrderBy(d => d.Fecha)
            .ToArray();

        return Results.Ok(respuesta);
    }

    /// <summary>
    /// Unified-list id scheme: <c>"manual-{AdjuntoManualId}"</c> / <c>"ingesta-{DocumentoFacturaId}"</c>
    /// -- the two source tables have independent, potentially colliding numeric PKs, so the origin
    /// prefix disambiguates which table <see cref="ObtenerContenidoAsync"/> must read. Any format
    /// that does not match exactly one of the two known prefixes followed by a positive integer is
    /// treated as unknown -- 404, never a 400 that would hint at the expected shape.
    /// </summary>
    private static bool TryParseDocumentoId(string id, out bool origenIngesta, out long idNumerico)
    {
        origenIngesta = false;
        idNumerico = 0;

        var separador = id.IndexOf('-');
        if (separador <= 0 || separador == id.Length - 1)
        {
            return false;
        }

        var prefijo = id[..separador];
        var resto = id[(separador + 1)..];

        if (!long.TryParse(resto, out idNumerico) || idNumerico <= 0)
        {
            return false;
        }

        if (prefijo == PrefijoIngesta)
        {
            origenIngesta = true;
            return true;
        }

        return prefijo == PrefijoManual;
    }
}

/// <summary>Forma unificada de <c>GET /api/facturas/{id}/documentos</c> — un elemento por documento,
/// de cualquier origen, con el <see cref="Id"/> exacto que <c>GET /api/documentos/{id}/contenido</c>
/// espera.</summary>
internal sealed record DocumentoRespuesta(string Id, string Origen, string NombreArchivo, string MimeType, DateTimeOffset Fecha)
{
    public const string OrigenIngesta = "INGESTA";
    public const string OrigenManual = "MANUAL";
}
