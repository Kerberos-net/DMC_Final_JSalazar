namespace SmartNet.Catalogos.Core;

/// <summary>
/// Port over <c>fact.MotivoAtributo</c> (design.md Interfaces/Contracts). SELECT/INSERT/UPDATE
/// only. <c>Activo</c>/`origen '02'` filtering happens in Core, never inside the SQL adapter
/// (design.md Interfaces/Contracts table) — this port returns raw rows.
/// </summary>
public interface IMotivoAtributoRepository
{
    Task<MotivoAtributo?> ObtenerAsync(int motivo, CancellationToken ct);

    Task<IReadOnlyList<MotivoAtributo>> ListarAsync(CancellationToken ct);

    Task GuardarAsync(MotivoAtributo atributo, CancellationToken ct);
}
