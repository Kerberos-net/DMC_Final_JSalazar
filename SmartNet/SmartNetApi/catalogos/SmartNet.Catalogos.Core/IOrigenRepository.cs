namespace SmartNet.Catalogos.Core;

/// <summary>
/// Port over <c>dbo.Origen</c> (design.md Interfaces/Contracts, 13 rows). Read-only — ADR 0003
/// external catalog.
/// </summary>
public interface IOrigenRepository
{
    Task<IReadOnlyList<Origen>> ListarAsync(CancellationToken ct);
}
