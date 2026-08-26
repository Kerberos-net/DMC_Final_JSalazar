using Microsoft.Data.SqlClient;
using SmartNet.Catalogos.Core;

namespace SmartNet.Catalogos.Infrastructure;

/// <summary>
/// SQL adapter over <c>dbo.DocumentoIdentidad</c> for <see cref="IDocumentoIdentidadRepository"/>
/// (design.md Interfaces/Contracts, 6 rows). Read-only — ADR 0003 external catalog.
/// </summary>
public sealed class SqlDocumentoIdentidadRepository : IDocumentoIdentidadRepository
{
    private readonly string _connectionString;

    public SqlDocumentoIdentidadRepository(string connectionString) => _connectionString = connectionString;

    public async Task<IReadOnlyList<DocumentoIdentidad>> ListarAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT coddocide, nomdocide FROM dbo.DocumentoIdentidad;";

        var resultado = new List<DocumentoIdentidad>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            resultado.Add(new DocumentoIdentidad(reader.GetString(0), reader.GetString(1)));
        }

        return resultado;
    }
}
