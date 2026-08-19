namespace SmartNet.Inbox.Core;

/// <summary>
/// Outcome of <see cref="IPromocionRepository.PromoverAsync"/>. <see cref="YaExistia"/> is
/// <c>true</c> only on the idempotent-catch path (design D2 — INSERT, catch SQL 2601/2627 from
/// <c>UQ_Factura_Procesamiento</c>, then resolve the existing <c>FacturaId</c>); it never means a
/// second <c>Factura</c> row was created.
/// </summary>
public sealed record ResultadoPromocion(long FacturaId, bool YaExistia);

/// <summary>
/// Port over the write side of promotion (design.md data flow: one <c>SqlTransaction</c> per
/// event). Implementation (<c>SqlPromocionRepository</c>, <c>usr_api</c> login) lives in
/// <c>SmartNet.Inbox.Infrastructure</c> (Phase 3).
/// </summary>
public interface IPromocionRepository
{
    /// <summary>
    /// INSERTs <c>Factura</c> (<c>PENDIENTE_VALIDACION</c>) + <c>FacturaExtraccion</c> rows and
    /// updates the source <c>InboxEvent</c> to <c>PROMOVIDO</c> in one transaction — or, on a
    /// duplicate <c>ProcesamientoId</c>, resolves the existing <c>FacturaId</c> without inserting
    /// a second row (design D2, spec.md "Idempotent promotion").
    /// </summary>
    Task<ResultadoPromocion> PromoverAsync(
        long inboxEventId, long procesamientoId, FacturaPromovida factura, CancellationToken ct);

    /// <summary>Updates the source <c>InboxEvent</c> to <c>DESCARTADO</c> — creates zero <c>Factura</c> rows.</summary>
    Task DescartarAsync(long inboxEventId, string motivoDescarte, CancellationToken ct);
}
