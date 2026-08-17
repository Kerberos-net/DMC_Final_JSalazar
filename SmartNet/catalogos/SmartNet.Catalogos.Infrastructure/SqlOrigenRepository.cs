using Microsoft.Data.SqlClient;
using SmartNet.Catalogos.Core;

namespace SmartNet.Catalogos.Infrastructure;

/// <summary>
/// SQL adapter over <c>dbo.Origen</c> for <see cref="IOrigenRepository"/> (design.md
/// Interfaces/Contracts, 13 rows). Read-only — ADR 0003 external catalog.
/// </summary>
public sealed class SqlOrigenRepository : IOrigenRepository
{
    private readonly string _connectionString;

    public SqlOrigenRepository(string connectionString) => _connectionString = connectionString;

    public async Task<IReadOnlyList<Origen>> ListarAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT codigo, origen FROM dbo.Origen;";

        var resultado = new List<Origen>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            resultado.Add(new Origen(reader.GetString(0), reader.GetString(1)));
        }

        return resultado;
    }
}
