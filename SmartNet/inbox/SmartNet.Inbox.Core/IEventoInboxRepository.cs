namespace SmartNet.Inbox.Core;

/// <summary>
/// One <c>fact.InboxEvent</c> row awaiting a promotion decision — the raw, still-unparsed
/// <see cref="PayloadJson"/> crosses this port as text; only
/// <c>SmartNet.Inbox.Infrastructure</c>'s <c>PayloadInboxParser</c> turns it into
/// <see cref="EventoInbox"/> (design D9 — JSON parsing never lives in Core).
/// </summary>
public sealed record EventoInboxPendiente(long InboxEventId, long ProcesamientoId, string PayloadJson);

/// <summary>
/// Port over the read/consume side of <c>fact.InboxEvent</c> (design.md data flow,
/// <c>PromocionBackgroundService</c>). Implementation (<c>SqlEventoInboxRepository</c>,
/// <c>usr_api</c> login) lives in <c>SmartNet.Inbox.Infrastructure</c> (Phase 3) and MUST NOT
/// read <c>fact.Procesamiento</c> or any other worker-private table (ADR 0003).
/// </summary>
public interface IEventoInboxRepository
{
    Task<IReadOnlyList<EventoInboxPendiente>> ListarPendientesAsync(CancellationToken ct);
}
