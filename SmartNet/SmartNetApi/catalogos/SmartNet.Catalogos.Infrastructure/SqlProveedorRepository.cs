using Microsoft.Data.SqlClient;
using SmartNet.Catalogos.Core;

namespace SmartNet.Catalogos.Infrastructure;

/// <summary>
/// SQL adapter over <c>dbo.Proveedor</c> for <see cref="IProveedorRepository"/> (design.md
/// Interfaces/Contracts). Read-only — ADR 0003 external catalog. <see cref="BuscarPorRucAsync"/>
/// returns a list, never a single row: <c>rucpro</c> is non-unique (<c>IX_Proveedor_Ruc</c> is a
/// non-unique index, not a key).
/// </summary>
public sealed class SqlProveedorRepository : IProveedorRepository
{
    private readonly string _connectionString;

    public SqlProveedorRepository(string connectionString) => _connectionString = connectionString;

    public async Task<Proveedor?> ObtenerPorCodigoAsync(string codigo, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT codpro, proveedor, coddocide, rucpro
            FROM dbo.Proveedor
            WHERE codpro = @codpro;
            """;
        command.Parameters.AddWithValue("@codpro", codigo);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return Map(reader);
    }

    public async Task<IReadOnlyList<Proveedor>> BuscarPorRucAsync(string ruc, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT codpro, proveedor, coddocide, rucpro
            FROM dbo.Proveedor
            WHERE rucpro = @rucpro;
            """;
        command.Parameters.AddWithValue("@rucpro", ruc);

        var resultado = new List<Proveedor>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            resultado.Add(Map(reader));
        }

        return resultado;
    }

    private static Proveedor Map(SqlDataReader reader) =>
        new(
            Codigo: reader.GetString(0).TrimEnd(),
            Nombre: reader.GetString(1),
            CodigoTipoDocumento: reader.IsDBNull(2) ? null : reader.GetString(2),
            Ruc: reader.IsDBNull(3) ? null : reader.GetString(3));
}
