namespace SmartNet.Inbox.Core;

/// <summary>
/// The five indicator values <see cref="CalculoDeIndicadores"/> actually computes (design D5,
/// confirmed). <c>fact.Factura.EsReferenciaExterna</c> is deliberately absent from this record —
/// it always keeps the DDL default 0/false; <c>DatosExtraidos</c> (#6) has no reference-nota
/// columns to derive it from, and notas de crédito are item #10.
/// </summary>
public sealed record IndicadoresFactura(
    bool EsProveedorGenerico,
    bool PosibleDuplicado,
    bool TieneCamposNoExtraidos,
    bool FechaEnDomingo,
    bool? AfectacionMixta);
