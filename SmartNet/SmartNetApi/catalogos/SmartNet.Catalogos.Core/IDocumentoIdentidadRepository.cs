namespace SmartNet.Catalogos.Core;

/// <summary>
/// Port over <c>dbo.DocumentoIdentidad</c> (design.md Interfaces/Contracts, 6 rows). Read-only —
/// ADR 0003 external catalog.
/// </summary>
public interface IDocumentoIdentidadRepository
{
    Task<IReadOnlyList<DocumentoIdentidad>> ListarAsync(CancellationToken ct);
}
