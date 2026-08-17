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
}
