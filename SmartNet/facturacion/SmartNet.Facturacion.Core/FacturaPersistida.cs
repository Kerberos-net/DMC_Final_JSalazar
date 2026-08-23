namespace SmartNet.Facturacion.Core;

/// <summary>
/// PR 2 — espejo de <c>fact.Factura</c> que <see cref="IUnidadDeTrabajo.CargarFacturaAsync"/>
/// devuelve. Análogo de <see cref="AsientoPersistido"/> pero para el agregado Factura: PATCH,
/// abrir, descartar y adjuntos operan sobre esta forma, nunca sobre <see cref="AsientoContable"/>
/// directamente (design.md D1: Core nunca ve SQL, solo estos records).
///
/// DESVIACIÓN DOCUMENTADA (PR 2, misma naturaleza que la #1 de PR 1): design.md solo fijó la forma
/// de <see cref="IUnidadDeTrabajo"/> alrededor de un asiento. Este record y los miembros de
/// <see cref="IUnidadDeTrabajo"/> que lo cargan/guardan son una extensión del contrato, necesaria
/// para que <c>PATCH /api/facturas/{id}</c>, <c>abrir</c>, <c>descartar</c> y adjuntos (Phase 2)
/// tengan un puerto propio en vez de forzar el de asiento.
/// </summary>
public sealed record FacturaPersistida(
    long FacturaId,
    string Estado,
    string ProveedorCodigo,
    string? RucProveedor,
    string TipoComprobante,
    string? Numero,
    decimal TotalOrig,
    string Moneda,
    DateOnly FechaEmision,
    int? Motivo,
    string? Afectacion,
    byte[] Version)
{
    public const string PendienteValidacion = "PENDIENTE_VALIDACION";
    public const string Validada = "VALIDADA";
    public const string Descartada = "DESCARTADA";
}
