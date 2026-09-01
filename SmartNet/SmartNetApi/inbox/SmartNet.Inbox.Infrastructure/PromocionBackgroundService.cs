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

            // design.md Decision 1: the paired PDF's InboxEvent is routed here, BEFORE
            // PoliticaDePromocion.Decidir, instead of running the structural sufficiency check.
            if (PoliticaDeDocumentoAsociado.EsDocumentoAsociado(evento))
            {
                await ProcesarDocumentoAsociadoAsync(pendiente, evento, ct);
                continue;
            }

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

    /// <summary>design.md data flow -- resolves the associated PDF's paired partner and merges,
    /// defers, or discards accordingly (design D2/D3/D4). Never calls <c>PromoverAsync</c>: this
    /// path creates zero <c>fact.Factura</c> rows.</summary>
    private async Task ProcesarDocumentoAsociadoAsync(EventoInboxPendiente pendiente, EventoInbox evento, CancellationToken ct)
    {
        var resolucion = await _promocionRepository.ResolverParAsync(evento.DocumentoAsociadoId!.Value, ct);
        var decision = PoliticaDeDocumentoAsociado.Decidir(resolucion);

        switch (decision)
        {
            case DecisionDocumentoAsociado.Fusiona fusiona:
                var documentoPromovido = new DocumentoPromovido(
                    evento.DocumentoRecibidoId, evento.NombreArchivo, evento.MimeType, evento.RutaRelativa, evento.TamanoBytes);
                await _promocionRepository.FusionarDocumentoAsync(pendiente.InboxEventId, fusiona.FacturaId, documentoPromovido, ct);
                break;
            case DecisionDocumentoAsociado.Difiere:
                // design D3: defer = do nothing. Row stays PENDIENTE, self-heals next cycle.
                break;
            case DecisionDocumentoAsociado.Descarta descarta:
                await _promocionRepository.DescartarAsync(pendiente.InboxEventId, descarta.Motivo, ct);
                break;
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
        // BACKLOG #12 (design D1): built directly from the already-parsed EventoInbox document
        // fields -- never a SELECT against fact.DocumentoRecibido (ADR 0003 DENY, task 2.3).
        var documentoPromovido = new DocumentoPromovido(
            evento.DocumentoRecibidoId, evento.NombreArchivo, evento.MimeType, evento.RutaRelativa, evento.TamanoBytes);

        await _promocionRepository.PromoverAsync(
            pendiente.InboxEventId, pendiente.ProcesamientoId, facturaPromovida, documentoPromovido, ct);
    }
}
