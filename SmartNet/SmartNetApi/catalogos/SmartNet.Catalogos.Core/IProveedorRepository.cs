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
}

/// <summary>
/// One page of <see cref="IProveedorRepository.BuscarAsync"/> results plus whether further pages
/// exist (the picker uses <see cref="HayMas"/> to decide if a "load more" affordance shows).
/// </summary>
public sealed record BusquedaProveedores(IReadOnlyList<Proveedor> Resultados, bool HayMas);
