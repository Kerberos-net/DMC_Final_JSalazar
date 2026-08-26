namespace SmartNet.Facturacion.Core;

/// <summary>
/// PR 3 (Phase 3) — espejo de <c>fact.DocumentoFactura</c> (schema 016, PR 1): la proyección
/// .NET-owned de un documento ingerido por Python, poblada en la transacción de promoción (PR 2,
/// <see cref="IUnidadDeTrabajo.RegistrarAdjuntoAsync"/>'s análogo del lado de ingesta, en
/// <c>SqlPromocionRepository</c>). Análogo de <see cref="AdjuntoManual"/> pero de solo lectura desde
/// este puerto — nada en <see cref="IUnidadDeTrabajo"/> INSERTA esta forma; la fila nace en
/// <c>SmartNet.Inbox.Infrastructure.SqlPromocionRepository</c>, fuera de este agregado.
///
/// design D1: la lista unificada de documentos (spec.md documentos-lista-unificada-api) lee esta
/// forma junto con <see cref="AdjuntoManual"/>, nunca <c>fact.DocumentoRecibido</c> (DENY, ADR 0003).
/// </summary>
public sealed record DocumentoFacturaPersistido(
    long DocumentoFacturaId,
    long FacturaId,
    string NombreArchivo,
    string MimeType,
    string RutaRelativa,
    long TamanoBytes,
    DateTimeOffset CreadoEn);
