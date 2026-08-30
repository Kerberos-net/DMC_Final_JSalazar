namespace SmartNet.Catalogos.Core;

/// <summary>
/// Port over <c>dbo.Proveedor</c> (design.md Interfaces/Contracts). Read-only — ADR 0003
/// external catalog. <see cref="BuscarPorRucAsync"/> returns a list, never a single row: `rucpro`
/// is non-unique (`IX_Proveedor_Ruc` is a non-unique index, not a key).
/// </summary>
public interface IProveedorRepository
{
    Task<Proveedor?> ObtenerPorCodigoAsync(string codigo, CancellationToken ct);

    Task<IReadOnlyList<Proveedor>> BuscarPorRucAsync(string ruc, CancellationToken ct);

    /// <summary>
    /// BACKLOG #18 PR8 (api-catalogos-proveedores) — paged name/RUC search for the SPA proveedor
    /// picker: <c>proveedor LIKE @q OR rucpro LIKE @q</c>, ordered by <c>proveedor</c>, with
    /// <c>P00000</c> ("Varios") filtered out (it is a system fallback, never hand-picked). A blank
    /// or too-short <paramref name="consulta"/> yields an empty page and issues no scan. Read-only
    /// (ADR 0003).
    /// </summary>
    Task<BusquedaProveedores> BuscarAsync(string consulta, int pagina, CancellationToken ct);

    /// <summary>
    /// BACKLOG #22 PR5 (catalog-queries-api spec req 1-3, design D6/D7) — the SPA proveedores
    /// browse-all screen: lists ALL proveedores INCLUDING <c>P00000</c>, ignores the picker's
    /// minimum-length rule, applies the same <c>proveedor / rucpro / codpro LIKE</c> text filter
    /// when <paramref name="consulta"/> is non-blank, and pages the full filtered+sorted set.
    /// <paramref name="orden"/> is one of <see cref="OrdenProveedor.Valores"/> and
    /// <paramref name="direccion"/> is <c>asc</c> or <c>desc</c> — the endpoint validates both;
    /// the adapter maps them to a compile-time column + ASC/DESC and always appends
    /// <c>, codpro ASC</c> as a unique tiebreak. <see cref="PaginaProveedores.TotalRegistros"/>
    /// comes from <c>COUNT(*) OVER()</c> in the same paged pass. Read-only (ADR 0003).
    /// </summary>
    Task<PaginaProveedores> ListarCatalogoAsync(
        string? consulta, string orden, string direccion, int pagina, int tamanio, CancellationToken ct);

    /// <summary>
    /// BACKLOG #22 PR5 (catalog-queries-api spec req 6, ADR 0021) — the whole filtered+sorted set
    /// with NO pagination, for the proveedores Excel export. Same filter/sort semantics as
    /// <see cref="ListarCatalogoAsync"/>. Read-only (ADR 0003).
    /// </summary>
    Task<IReadOnlyList<Proveedor>> ListarCatalogoCompletoAsync(
        string? consulta, string orden, string direccion, CancellationToken ct);
}

/// <summary>
/// One page of <see cref="IProveedorRepository.ListarCatalogoAsync"/> results plus the pagination
/// metadata the SPA needs. Field names mirror the project's existing <c>PaginaBandeja&lt;T&gt;</c>
/// envelope (design D6): <c>{ items, pagina, tamanioPagina, totalRegistros, totalPaginas }</c>.
/// <see cref="TotalRegistros"/> is the full filtered count (from <c>COUNT(*) OVER()</c>), correct
/// even for an out-of-range page whose <see cref="Items"/> is empty.
/// </summary>
public sealed record PaginaProveedores(
    IReadOnlyList<Proveedor> Items,
    int Pagina,
    int TamanioPagina,
    int TotalRegistros,
    int TotalPaginas);

/// <summary>
/// One page of <see cref="IProveedorRepository.BuscarAsync"/> results plus whether further pages
/// exist (the picker uses <see cref="HayMas"/> to decide if a "load more" affordance shows).
/// </summary>
public sealed record BusquedaProveedores(IReadOnlyList<Proveedor> Resultados, bool HayMas);
