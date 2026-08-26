namespace SmartNet.Auth.Core;

/// <summary>
/// Port over <c>fact.Sesion</c> (design.md Decision 5). <c>ITicketStore</c> is an ADAPTER over
/// this port, not the port itself — it belongs to Phase 3's
/// <c>SmartNet.Auth.Infrastructure</c>, not here.
/// </summary>
public interface ISesionRepository
{
    Task CreateAsync(
        long usuarioId, string tokenHash, DateTimeOffset expiraEn, string ticket, CancellationToken ct);

    Task<SesionActiva?> FindActiveAsync(string tokenHash, DateTimeOffset ahora, CancellationToken ct);

    Task RenewAsync(
        string tokenHash, DateTimeOffset expiraEn, DateTimeOffset ahora, CancellationToken ct);

    Task RevokeAsync(
        string tokenHash, MotivoRevocacion motivo, DateTimeOffset ahora, CancellationToken ct);

    Task RevokeAllForUsuarioAsync(
        long usuarioId, MotivoRevocacion motivo, DateTimeOffset ahora, CancellationToken ct);

    // Added for SmartNet.Admin's `sesion purgar` verb (design.md Decision 3/7, tasks.md 5.8/5.9):
    // the sole DELETE caller in the whole permission matrix. Anchored on CreadaEn (the row's own
    // birth date), not ExpiraEn or UltimaActividadEn — "older than the retention window" is a
    // statement about how long the record has existed, matching design.md's "it scans a table
    // that grows by ~1-2k rows a year" framing. Returns the number of rows deleted, for the CLI to
    // report back to the operator.
    Task<int> DeleteOlderThanAsync(DateTimeOffset corte, CancellationToken ct);
}
