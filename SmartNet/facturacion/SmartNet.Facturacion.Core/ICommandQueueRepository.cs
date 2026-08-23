namespace SmartNet.Facturacion.Core;

/// <summary>
/// design D7 — "sincronizar/reconectar/reprocesar enqueue only": <c>INSERT fact.CommandQueue</c>,
/// 202 Accepted + <c>{ correlationId }</c>. .NET NUNCA llama a Python directamente (ADR 0003); este
/// puerto es la única forma en que <see cref="ServicioDeIntegraciones"/> le habla al worker.
/// <c>correlationId</c> lo genera la API ANTES de escribir (design D7, comentario de esquema).
/// </summary>
public interface ICommandQueueRepository
{
    Task EncolarAsync(string tipo, long? referencia, string payload, Guid correlationId, CancellationToken ct);
}
