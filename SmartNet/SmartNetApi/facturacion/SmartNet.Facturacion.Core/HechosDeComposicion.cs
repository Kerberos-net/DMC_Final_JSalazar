using SmartNet.Catalogos.Core;
using SmartNet.Contable.Core;

namespace SmartNet.Facturacion.Core;

/// <summary>
/// BACKLOG #24 (design A1/A3) — the facts that <see cref="SembradoDeAsiento"/> needs but a
/// <see cref="FacturaPersistida"/> does not carry, resolved by Infrastructure and passed in
/// (the <c>ProveedorResuelto</c> precedent, ADR 0019: Core never reads a catálogo or SQL).
/// <see cref="IUnidadDeTrabajo.ResolverHechosDeComposicionAsync"/> produces this value.
/// </summary>
/// <param name="EsRelacionada"><c>fact.ProveedorAtributo.EsRelacionada</c> — <c>false</c> when absent.</param>
/// <param name="MotivoDescripcion"><c>dbo.Motivo.descripcion</c> for <c>fact.Factura.Motivo</c>; <c>null</c> when the factura has no motivo.</param>
/// <param name="TipoCambio">The frozen VENTA rate for the emission date; <c>null</c> for a PEN comprobante (no §6 conversion).</param>
/// <param name="CuentaSugerida">The <c>ServicioDeSugerencia</c> winner (REGLAS.md §3 cascade); <c>null</c> when there is no suggestion — design A2 placeholder line.</param>
public sealed record HechosDeComposicion(
    bool EsRelacionada,
    string? MotivoDescripcion,
    TipoCambioCongelado? TipoCambio,
    CuentaContable? CuentaSugerida);
