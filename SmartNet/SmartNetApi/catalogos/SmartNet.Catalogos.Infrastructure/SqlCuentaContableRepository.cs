using Microsoft.Data.SqlClient;
using SmartNet.Catalogos.Core;

namespace SmartNet.Catalogos.Infrastructure;

/// <summary>
/// SQL adapter over <c>dbo.CuentaContable</c> for <see cref="ICuentaContableRepository"/>
/// (design.md Interfaces/Contracts). Read-only — ADR 0003 external catalog, no <c>INSERT</c>/
/// <c>UPDATE</c>/<c>DELETE</c> ever issued (spec.md "No SQL adapter writes to a dbo.* table").
/// </summary>
public sealed class SqlCuentaContableRepository : ICuentaContableRepository
{
    private readonly string _connectionString;

    public SqlCuentaContableRepository(string connectionString) => _connectionString = connectionString;

    public async Task<IReadOnlyList<CuentaContable>> ListarPlanCompletoAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT cuenta, descripcion, nivel, ctarefleja, ctapuente
            FROM dbo.CuentaContable;
            """;

        var resultado = new List<CuentaContable>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            resultado.Add(Map(reader));
        }

        return resultado;
    }

    public async Task<CuentaContable?> ObtenerAsync(string cuenta, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT cuenta, descripcion, nivel, ctarefleja, ctapuente
            FROM dbo.CuentaContable
            WHERE cuenta = @cuenta;
            """;
        command.Parameters.AddWithValue("@cuenta", cuenta);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return Map(reader);
    }

    private static CuentaContable Map(SqlDataReader reader) =>
        new(
            Cuenta: reader.GetString(0),
            Descripcion: reader.GetString(1),
            Nivel: reader.IsDBNull(2) ? null : reader.GetByte(2),
            CtaReflejaCodigo: reader.IsDBNull(3) ? null : reader.GetString(3),
            CtaPuenteCodigo: reader.IsDBNull(4) ? null : reader.GetString(4));
}
