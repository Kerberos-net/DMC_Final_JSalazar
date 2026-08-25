namespace SmartNet.Facturacion.Core;

/// <summary>
/// design D7 — lectura read-only del historial de corrección (<c>fact.AuditoriaCorreccion</c>) de
/// una factura, unificando FACTURA + ASIENTO (incluido ANULADO) + ADJUNTO. Deliberadamente NO es un
/// miembro de <see cref="IUnidadDeTrabajo"/>: esa interfaz es una sesión de transacción
/// (<c>IAsyncDisposable</c> que posee un <c>SqlTransaction</c>), y un SELECT puro no necesita abrir
/// ni revertir una transacción de negocio (design D7 tabla de comparación). Sigue el mismo patrón
/// read-side dedicado que <see cref="IEstadoIntegracionRepository"/>.
/// </summary>
public interface IAuditoriaRepository
{
    /// <summary>Todas las entradas de <c>fact.AuditoriaCorreccion</c> que corresponden a la
    /// factura, a su(s) asiento(s) (incluido cualquiera en estado ANULADO — una anulación es
    /// exactamente lo que un historial de auditoría debe mostrar), y a sus adjuntos manuales,
    /// ordenadas de la más reciente a la más antigua (<c>OcurridoEn DESC</c>). Lista vacía si la
    /// factura no existe o no tiene ninguna entrada — nunca lanza por un id desconocido (design
    /// D7: el endpoint que la consume ya validó existencia de la factura antes de llamar, o
    /// deliberadamente no lo hace para <c>GET /historial</c>, ver AuditoriaEndpoints.cs).</summary>
    Task<IReadOnlyList<EntradaAuditoria>> ListarPorFacturaAsync(long facturaId, CancellationToken ct);
}
