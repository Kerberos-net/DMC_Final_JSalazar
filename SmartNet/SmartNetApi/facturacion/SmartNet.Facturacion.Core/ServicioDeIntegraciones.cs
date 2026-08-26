namespace SmartNet.Facturacion.Core;

/// <summary>
/// design D7 — "sincronizar/reconectar/reprocesar enqueue only": traduce cada comando a un
/// <c>INSERT fact.CommandQueue</c> vía <see cref="ICommandQueueRepository"/>, nunca llama a Python
/// directamente (ADR 0003). <see cref="ObtenerEstadoAsync"/> es un passthrough de solo lectura sobre
/// <see cref="IEstadoIntegracionRepository"/> — la derivación de la "pill" es de <c>SmartNet.Api</c>.
/// </summary>
public sealed class ServicioDeIntegraciones
{
    private readonly ICommandQueueRepository _commandQueue;
    private readonly IEstadoIntegracionRepository _estados;

    public ServicioDeIntegraciones(ICommandQueueRepository commandQueue, IEstadoIntegracionRepository estados)
    {
        _commandQueue = commandQueue;
        _estados = estados;
    }

    public Task EncolarAsync(string tipo, long? referencia, string payload, Guid correlationId, CancellationToken ct) =>
        _commandQueue.EncolarAsync(tipo, referencia, payload, correlationId, ct);

    public Task<IReadOnlyList<EstadoIntegracion>> ObtenerEstadoAsync(CancellationToken ct) =>
        _estados.ListarAsync(ct);
}
