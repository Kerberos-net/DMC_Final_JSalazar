namespace SmartNet.Catalogos.Core;

/// <summary>
/// Port over <c>fact.ProveedorAtributo</c> (design.md Interfaces/Contracts). SELECT/INSERT/UPDATE
/// only — `008_usuarios_y_permisos.sql` grants `fact_api` no `DELETE`, so no `Eliminar*` method.
/// <see cref="GuardarAsync"/> upserts (spec.md "satelites-propios" scenario).
/// </summary>
public interface IProveedorAtributoRepository
{
    Task<ProveedorAtributo?> ObtenerAsync(string proveedorCodigo, CancellationToken ct);

    Task GuardarAsync(ProveedorAtributo atributo, CancellationToken ct);
}
