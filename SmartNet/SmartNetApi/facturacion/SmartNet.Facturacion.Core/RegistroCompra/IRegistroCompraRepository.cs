namespace SmartNet.Facturacion.Core.RegistroCompra;

/// <summary>
/// spec registro-compra-api req 4 — a dedicated, read-only Core port over the purchase register
/// (<c>fact.AsientoContable</c> + <c>fact.Factura</c> + LEFT JOIN <c>dbo.Proveedor</c> +
/// <c>fact.AsientoContableDetalle</c>). Same shape as the inbox <c>SqlBandejaRepository</c> pattern.
///
/// No accounting rule lives here (ADR 0019) and the adapter issues only <c>SELECT</c> under the
/// existing <c>008</c> <c>fact_api</c> grants — no <c>dbo.*</c> write, no new GRANT, no versioned
/// SQL (ADR 0003).
/// </summary>
public interface IRegistroCompraRepository
{
    /// <summary>
    /// spec req 1 — one page of the register for <paramref name="periodo"/>. Row predicate:
    /// <c>fact.Factura.Estado = 'VALIDADA'</c> AND the vigente asiento is NOT <c>ANULADO</c>.
    /// <c>fact.AsientoContable.FechaContable</c> is filtered by the half-open month range. An empty
    /// period yields an empty page with <c>TotalRegistros = 0</c> (the endpoint returns 200, not 404).
    /// </summary>
    Task<PaginaRegistroCompra<RegistroCompraCabecera>> ListarPeriodoAsync(
        PeriodoContable periodo, int pagina, int tamanioPagina, CancellationToken ct);

    /// <summary>
    /// spec req 2 — the cabecera + ordered lines for one asiento. Re-applies the SAME row predicate
    /// in its own SQL (design D3): an <c>ANULADO</c> / non-<c>VALIDADA</c> asiento is
    /// indistinguishable from a nonexistent one — both yield <c>null</c> here and a 404 at the
    /// endpoint. A qualifying asiento with no lines yields a detalle with an empty <c>Lineas</c>.
    /// </summary>
    Task<RegistroCompraDetalle?> ObtenerAsync(long asientoId, CancellationToken ct);

    /// <summary>
    /// spec req 3 / design D7 — the whole register for <paramref name="periodo"/> with NO
    /// pagination, for the Excel export. Same filter and ordering semantics as
    /// <see cref="ListarPeriodoAsync"/>.
    /// </summary>
    Task<IReadOnlyList<RegistroCompraCabecera>> ListarPeriodoCompletoAsync(
        PeriodoContable periodo, CancellationToken ct);
}
