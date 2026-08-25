namespace SmartNet.Facturacion.Core;

/// <summary>
/// design D7 — "GET /api/integraciones/estado derives the pill, never stores it": espejo read-only
/// de una fila de <c>fact.EstadoIntegracion</c> (última corrida/éxito/error, fallos consecutivos).
/// La derivación de la "pill" (Conectado/Con error) es de <c>SmartNet.Api</c> (#11 Phase 4) — este
/// tipo transporta solo los hechos crudos.
/// </summary>
public sealed record EstadoIntegracion(
    string Nombre,
    DateTimeOffset? UltimaEjecucion,
    DateTimeOffset? UltimoExito,
    string? UltimoError,
    int FallosConsecutivos);

/// <summary>
/// design D7 — lee <c>fact.EstadoIntegracion</c>, nunca escribe (esa tabla la escriben ambos
/// runtimes por fila, ADR 0003 "Publicación con múltiples orígenes"; #11 solo la lee).
/// </summary>
public interface IEstadoIntegracionRepository
{
    Task<IReadOnlyList<EstadoIntegracion>> ListarAsync(CancellationToken ct);
}
