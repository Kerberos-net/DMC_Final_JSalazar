namespace SmartNet.Inbox.Core;

/// <summary>One <c>fact.FacturaExtraccion</c> row to INSERT — mirrors <see cref="EvidenciaCampo"/>, 1:1.</summary>
public sealed record FacturaExtraccionPromovida(string CampoNombre, string ValorExtraido, string Fuente);

/// <summary>
/// The in-memory <c>Factura</c> + <c>FacturaExtraccion</c> rows <c>SqlPromocionRepository</c>
/// (WU3) INSERTs inside one transaction (design D2). <see cref="Indicadores"/> deliberately
/// excludes <c>EsReferenciaExterna</c> — Infrastructure never writes it either, leaving
/// <c>fact.Factura</c>'s own DDL default 0/false to apply.
/// </summary>
public sealed record FacturaPromovida(
    string ProveedorCodigo,
    string TipoComprobante,
    string? Numero,
    string? RucProveedor,
    decimal TotalOrig,
    string Moneda,
    DateOnly FechaEmision,
    IndicadoresFactura Indicadores,
    IReadOnlyList<FacturaExtraccionPromovida> Extracciones,
    string Estado);
