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

/// <summary>
/// BACKLOG #12 (design D1): the <c>fact.DocumentoFactura</c> row <c>SqlPromocionRepository</c>
/// INSERTs in the SAME transaction as <see cref="FacturaPromovida"/> (schema 016). Built directly
/// from <see cref="EventoInbox"/>'s document fields -- never from a SELECT against
/// <c>fact.DocumentoRecibido</c> (ADR 0003 DENY, 008; unchanged, task 2.3). <see cref="DocumentoRecibidoId"/>
/// is the idempotency key (<c>UQ_DocumentoFactura_DocumentoRecibidoId</c>): a re-processed
/// <c>InboxEvent</c> for the same ingested document projects at most one row.
/// </summary>
public sealed record DocumentoPromovido(
    long DocumentoRecibidoId,
    string NombreArchivo,
    string MimeType,
    string RutaRelativa,
    long TamanoBytes);
