namespace SmartNet.Inbox.Core;

/// <summary>
/// The indicator values <see cref="CalculoDeIndicadores"/> actually computes (design D5,
/// confirmed). <c>fact.Factura.EsReferenciaExterna</c> is deliberately absent from this record —
/// it always keeps the DDL default 0/false; <c>DatosExtraidos</c> (#6) has no reference-nota
/// columns to derive it from, and notas de crédito are item #10.
///
/// BACKLOG #19 (Phase 2): <see cref="CamposNoExtraidos"/> is the per-field list the worker
/// reported, carried BESIDE the derived boolean <see cref="TieneCamposNoExtraidos"/> instead of
/// collapsed away. Consistency invariant (enforced by <see cref="CalculoDeIndicadores.Calcular"/>
/// and asserted by its tests): <see cref="TieneCamposNoExtraidos"/> is true iff
/// <see cref="CamposNoExtraidos"/> is non-empty. The list is what the SPA needs to highlight the
/// individual OCR-unverified fields (<c>fact.Factura.CamposNoExtraidos</c>, schema 021); the
/// boolean stays for the coarse bandeja badge.
/// </summary>
public sealed record IndicadoresFactura(
    bool EsProveedorGenerico,
    bool PosibleDuplicado,
    bool TieneCamposNoExtraidos,
    bool FechaEnDomingo,
    bool? AfectacionMixta,
    IReadOnlyList<string> CamposNoExtraidos);
