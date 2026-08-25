namespace SmartNet.Inbox.Core;

/// <summary>
/// BACKLOG #13, design.md Interfaces/Contracts — filter parameters for
/// <c>GET /api/bandeja</c>. <see cref="Pagina"/> is 1-based; <see cref="TamanioPagina"/> is fixed
/// at 20 (product-owner decision, proposal.md) but kept as a parameter so
/// <c>SqlBandejaRepository</c> never hardcodes it twice.
/// </summary>
public sealed record FiltrosBandeja(
    string? Estado,
    DateOnly? Desde,
    DateOnly? Hasta,
    string? Proveedor,
    string Orden,
    int Pagina,
    int TamanioPagina = 20);

/// <summary>
/// One <c>fact.ProcesamientoError</c> entry projected for the panel de errores (design.md D1/D3).
/// .NET only reads this table (ADR 0003 revision 6, asymmetric-read), never writes it.
/// </summary>
public sealed record ErrorProcesamiento(
    long ProcesamientoErrorId,
    string Integracion,
    string Mensaje,
    string Clasificacion,
    DateTime OcurridoEn);

/// <summary>
/// One row of the <c>GET /api/bandeja</c> projection (design D2/D3/D5/D7, widened for BACKLOG
/// #13). <see cref="Indicadores"/>/<see cref="FacturaId"/> are only present once
/// <see cref="EstadoConsumo"/> is <c>PROMOVIDO</c>. <see cref="Errores"/> is never null — an empty
/// list means no error history, for either <see cref="Origen"/>. <see cref="ReprocesarDisponibleEn"/>
/// null means the reprocesar control is enabled now.
/// </summary>
public sealed record BandejaItem(
    long InboxEventId,
    string Origen,
    long ProcesamientoId,
    string EstadoConsumo,
    DateTime CreadoEn,
    long? FacturaId,
    string? ProveedorCodigo,
    string? RucProveedor,
    IndicadoresFactura? Indicadores,
    string? MotivoDescarte,
    IReadOnlyList<ErrorProcesamiento> Errores,
    DateTime? ReprocesarDisponibleEn);

/// <summary>
/// BACKLOG #13, design.md Interfaces/Contracts — the pagination envelope every bandeja response
/// carries. <see cref="TotalPaginas"/> is <see cref="EnvelopeBandeja.CalcularTotalPaginas"/> over
/// <see cref="TotalRegistros"/>/<see cref="TamanioPagina"/>.
/// </summary>
public sealed record PaginaBandeja<T>(
    IReadOnlyList<T> Items,
    int Pagina,
    int TamanioPagina,
    int TotalRegistros,
    int TotalPaginas);

/// <summary>
/// Port over the SPA-facing read projection (design.md data flow: Angular Inbox →
/// <c>GET /api/bandeja?...</c>). Implementation (<c>SqlBandejaRepository</c>,
/// <c>usr_api</c> login) lives in <c>SmartNet.Inbox.Infrastructure</c> (Phase 3);
/// <c>BandejaEndpoints.cs</c> (Phase 4) is a thin delegator, never a second query surface.
/// </summary>
public interface IBandejaRepository
{
    Task<PaginaBandeja<BandejaItem>> ListarAsync(FiltrosBandeja filtros, CancellationToken ct);
}
