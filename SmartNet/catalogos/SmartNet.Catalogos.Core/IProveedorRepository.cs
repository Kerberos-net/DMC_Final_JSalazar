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
}
