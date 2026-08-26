using Microsoft.Data.SqlClient;
using SmartNet.Catalogos.Core;

namespace SmartNet.Catalogos.Infrastructure;

/// <summary>
/// SQL adapter over <c>dbo.Motivo</c> for <see cref="IMotivoRepository"/> (design.md
/// Interfaces/Contracts). Read-only — ADR 0003 external catalog. <c>cuenta</c> holds
/// comma-separated PREFIXES, never complete account codes (raw input to
/// <c>ResolucionDePrefijos</c>).
/// </summary>
public sealed class SqlMotivoRepository : IMotivoRepository
{
    private readonly string _connectionString;

    public SqlMotivoRepository(string connectionString) => _connectionString = connectionString;

    public async Task<Motivo?> ObtenerAsync(int codigo, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT codigo, motivo, cuenta
            FROM dbo.Motivo
            WHERE codigo = @codigo;
            """;
        command.Parameters.AddWithValue("@codigo", codigo);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return Map(reader);
    }

    public async Task<IReadOnlyList<Motivo>> ListarAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT codigo, motivo, cuenta FROM dbo.Motivo;";

        var resultado = new List<Motivo>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            resultado.Add(Map(reader));
        }

        return resultado;
    }

    private static Motivo Map(SqlDataReader reader) =>
        new(
            Codigo: reader.GetInt32(0),
            Descripcion: reader.GetString(1),
            Cuenta: reader.IsDBNull(2) ? null : reader.GetString(2));
}
