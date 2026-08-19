using Microsoft.Extensions.Hosting;
using SmartNet.Inbox.Core;

namespace SmartNet.Inbox.Infrastructure;

/// <summary>
/// Hosted consumer of <c>fact.InboxEvent</c> (design.md data flow, design D7: <see cref="PeriodicTimer"/>
/// with an injected <see cref="TimeProvider"/>, never a bare 1-minute <c>Task.Delay</c> loop, so
/// tests can advance a <c>FakeTimeProvider</c> instead of sleeping). Writes no
/// <c>fact.EstadoIntegracion</c> row (design D8 -- <c>CK_EstadoIntegracion_Nombre</c> has no
/// <c>INBOX</c> value and reusing <c>WORKER</c> would mask #6's own heartbeat; un-notified rows
/// self-heal on the next tick, so no separate liveness signal is needed here).
/// </summary>
public sealed class PromocionBackgroundService : BackgroundService
{
    private static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(1);

    private readonly IEventoInboxRepository _eventoInboxRepository;
    private readonly IPromocionRepository _promocionRepository;
    private readonly TimeProvider _timeProvider;

    public PromocionBackgroundService(
        IEventoInboxRepository eventoInboxRepository,
        IPromocionRepository promocionRepository,
        TimeProvider timeProvider)
    {
        _eventoInboxRepository = eventoInboxRepository;
        _promocionRepository = promocionRepository;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Intervalo, _timeProvider);
        do
        {
            await ProcesarPendientesAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One poll cycle (spec.md "Consumer runs on its own schedule") -- `internal` so tests
    /// can drive exactly one cycle deterministically instead of racing the timer.</summary>
    internal async Task ProcesarPendientesAsync(CancellationToken ct)
    {
        var pendientes = await _eventoInboxRepository.ListarPendientesAsync(ct);

        foreach (var pendiente in pendientes)
        {
            var evento = PayloadInboxParser.Parse(pendiente.PayloadJson);
            var decision = PoliticaDePromocion.Decidir(evento);

            switch (decision)
            {
                case DecisionPromocion.Promueve:
                    await PromoverAsync(pendiente, evento, ct);
                    break;
                case DecisionPromocion.Descarta descarta:
                    await _promocionRepository.DescartarAsync(pendiente.InboxEventId, descarta.Motivo, ct);
                    break;
            }
        }
    }

    private async Task PromoverAsync(EventoInboxPendiente pendiente, EventoInbox evento, CancellationToken ct)
    {
        var comprobante = evento.Comprobante!; // PoliticaDePromocion.Decidir already confirmed presence.
        var proveedor = await _promocionRepository.ResolverProveedorAsync(comprobante.RucProveedor, ct);
        var existeIdentidadPrevia = await _promocionRepository.ExisteIdentidadPreviaAsync(
            comprobante.RucProveedor, comprobante.TipoComprobante!, comprobante.Numero, ct);

        var indicadores = CalculoDeIndicadores.Calcular(evento, proveedor.Existe, existeIdentidadPrevia);
        var facturaPromovida = ConstruccionDeFactura.Construir(evento, proveedor.Codigo, indicadores);

        await _promocionRepository.PromoverAsync(pendiente.InboxEventId, pendiente.ProcesamientoId, facturaPromovida, ct);
    }
}
