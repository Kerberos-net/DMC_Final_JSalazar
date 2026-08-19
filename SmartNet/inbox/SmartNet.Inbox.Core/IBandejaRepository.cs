namespace SmartNet.Inbox.Core;

/// <summary>
/// One row of the <c>GET /api/bandeja</c> projection (design D6 — reuses ADR 0008's endpoint
/// shape, #7-shaped, widened by #13 later). <see cref="Indicadores"/>/<see cref="FacturaId"/> are
/// only present once <see cref="EstadoConsumo"/> is <c>PROMOVIDO</c>.
/// </summary>
public sealed record BandejaItem(
    long InboxEventId,
    string EstadoConsumo,
    DateTime CreadoEn,
    long? FacturaId,
    IndicadoresFactura? Indicadores,
    string? MotivoDescarte);

/// <summary>
/// Port over the SPA-facing read projection (design.md data flow: Angular Inbox →
/// <c>GET /api/bandeja?estado=&amp;orden=</c>). Implementation (<c>SqlBandejaRepository</c>,
/// <c>usr_api</c> login) lives in <c>SmartNet.Inbox.Infrastructure</c> (Phase 3);
/// <c>BandejaEndpoints.cs</c> (Phase 4) is a thin delegator, never a second query surface.
/// </summary>
public interface IBandejaRepository
{
    /// <summary><paramref name="estado"/> null means every <c>EstadoConsumo</c>; <paramref name="orden"/> is <c>fecha</c> asc/desc.</summary>
    Task<IReadOnlyList<BandejaItem>> ListarAsync(string? estado, string orden, CancellationToken ct);
}
