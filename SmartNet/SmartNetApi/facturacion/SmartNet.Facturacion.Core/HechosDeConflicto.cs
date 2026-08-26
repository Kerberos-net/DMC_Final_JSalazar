namespace SmartNet.Facturacion.Core;

/// <summary>
/// design D4 — hechos de negocio pre-calculados por <c>IUnidadDeTrabajo.CargarAsientoAsync</c>
/// (Infrastructure: consulta <c>fact.Factura</c> + <c>IX_Factura_Identidad</c> +
/// <c>ITipoCambioRepository.ObtenerVigenteAsync</c>) para que el gate de <see cref="CasoConflicto"/>
/// se evalúe en Core sin que Core haga ningún SELECT — mantiene ADR 0019. NO incluye
/// <see cref="CasoConflicto.FechaAnteriorAlCorte"/> ni <see cref="CasoConflicto.ProveedorGenericoNoResuelto"/>:
/// esos dos se derivan re-mapeando los fallos Global 3/4 de <c>InvariantesDeConfirmacion.Evaluar</c>
/// (decisión ratificada del dueño del producto, obs #138), no de un hecho pre-calculado aquí.
/// </summary>
public sealed record HechosDeConflicto(
    bool DuplicadoNoResuelto,
    bool ComprobanteEmitidoDomingo,
    bool SinTipoCambio,
    bool NotaCreditoReferenciaIrresoluble,
    bool AfectacionMixta,
    bool AfectacionNoVerificada)
{
    /// <summary>Ningún hecho de conflicto activo — el estado por defecto para la mayoría de facturas.</summary>
    public static readonly HechosDeConflicto Ninguno = new(false, false, false, false, false, false);
}
