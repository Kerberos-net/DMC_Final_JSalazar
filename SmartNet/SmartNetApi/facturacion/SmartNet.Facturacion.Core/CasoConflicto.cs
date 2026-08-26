namespace SmartNet.Facturacion.Core;

/// <summary>
/// design.md D4 — "409 gate before the engine": una fila por caso de la tabla 409 de ADR 0008.
/// Evaluado en la unidad de trabajo antes de componer/confirmar el asiento — nunca dentro de
/// <c>InvariantesDeConfirmacion</c> (eso sería #8 reabriendo su núcleo puro para reglas de estado de
/// negocio). <see cref="FechaAnteriorAlCorte"/> y <see cref="ProveedorGenericoNoResuelto"/>
/// coinciden con los nombres de <c>InvarianteContable.FechaAnteriorAlCorte</c>/
/// <c>InvarianteContable.ProveedorVarios</c> (Global 3/4) a propósito — decisión ratificada del
/// dueño del producto (sdd/api-facturas-asientos, obs #138): esos dos globales del motor puro se
/// re-mapean a 409, no a 422, cuando <c>ServicioDeFacturas.ValidarAsync</c> separa los fallos de
/// <c>InvariantesDeConfirmacion.Evaluar</c>.
/// </summary>
public enum CasoConflicto
{
    /// <summary>Identidad duplicada sin resolver (IX_Factura_Identidad).</summary>
    DuplicadoNoResuelto,

    /// <summary>El comprobante fue emitido un domingo (REGLAS.md).</summary>
    ComprobanteEmitidoDomingo,

    /// <summary>Factura en moneda extranjera sin tipo de cambio vigente.</summary>
    SinTipoCambio,

    /// <summary>Proveedor P00000 (Varios) sin resolver antes de confirmar.</summary>
    ProveedorGenericoNoResuelto,

    /// <summary>FechaContable anterior a la fecha de corte contable.</summary>
    FechaAnteriorAlCorte,

    /// <summary>Nota de crédito con referencia interna irresoluble (factura ausente/no validada/
    /// descartada/con asiento vigente anulado).</summary>
    NotaCreditoReferenciaIrresoluble,

    /// <summary>El asiento ya está CONFIRMADO o ANULADO — edítelo vía reabrir, no vía validar/PATCH.</summary>
    AsientoYaConfirmado,

    /// <summary>El comprobante declara más de un código de afectación tributaria (ADR 0017).</summary>
    AfectacionMixta,

    /// <summary>Comprobante solo-PDF cuya afectación aún no fue confirmada por el usuario.</summary>
    AfectacionNoVerificada,

    /// <summary>outbox-mensajeria (BACKLOG #14, OQ5/ADR 0020 decisión 5) — <c>validar</c> sobre una
    /// factura <c>DESCARTADA</c> (<see cref="TransicionEstadoFactura.NoTransicionable"/>): terminal,
    /// 409, revierte la confirmación del asiento. No reutiliza <see cref="AsientoYaConfirmado"/>: ese
    /// caso es la regla ESPEJO ("la factura ya fue validada"), no esta.</summary>
    FacturaDescartada,
}
