using Microsoft.Data.SqlClient;
using SmartNet.Catalogos.Core;

namespace SmartNet.Catalogos.Infrastructure;

/// <summary>
/// SQL adapter over <c>fact.SugerenciaCuenta</c> for <see cref="ISugerenciaCuentaRepository"/>
/// (design.md Interfaces/Contracts). Storage access only -- no method ranks, sorts, or selects a
/// single "best" candidate (design.md Decision 2, spec.md; that logic belongs to item #9). No
/// existence check against <c>dbo.Proveedor</c>/<c>dbo.Motivo</c> is issued here (design.md
/// Decision 2).
/// </summary>
public sealed class SqlSugerenciaCuentaRepository : ISugerenciaCuentaRepository
{
    private readonly string _connectionString;

    public SqlSugerenciaCuentaRepository(string connectionString) => _connectionString = connectionString;

    public async Task<IReadOnlyList<SugerenciaCuenta>> ListarPorProveedorYMotivoAsync(
        string proveedorCodigo, int motivo, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ProveedorCodigo, Motivo, CuentaCodigo, Veces, UltimoUso
            FROM fact.SugerenciaCuenta
            WHERE ProveedorCodigo = @proveedorCodigo AND Motivo = @motivo;
            """;
        command.Parameters.AddWithValue("@proveedorCodigo", proveedorCodigo);
        command.Parameters.AddWithValue("@motivo", motivo);

        return await LeerListaAsync(command, ct);
    }

    public async Task<IReadOnlyList<SugerenciaCuenta>> ListarPorMotivoAsync(int motivo, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ProveedorCodigo, Motivo, CuentaCodigo, Veces, UltimoUso
            FROM fact.SugerenciaCuenta
            WHERE Motivo = @motivo;
            """;
        command.Parameters.AddWithValue("@motivo", motivo);

        return await LeerListaAsync(command, ct);
    }

    public async Task<IReadOnlyList<SugerenciaCuenta>> ListarPorProveedorAsync(
        string proveedorCodigo, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ProveedorCodigo, Motivo, CuentaCodigo, Veces, UltimoUso
            FROM fact.SugerenciaCuenta
            WHERE ProveedorCodigo = @proveedorCodigo;
            """;
        command.Parameters.AddWithValue("@proveedorCodigo", proveedorCodigo);

        return await LeerListaAsync(command, ct);
    }

    // Single statement, both branches write EVERY column the caller cares about (Veces AND
    // UltimoUso) -- the known bug class in this project is an UPDATE that forgets a field. The
    // instant is always the caller-supplied parameter, never SYSUTCDATETIME(), so callers (item #9)
    // stay deterministic to test.
    public async Task RegistrarUsoAsync(
        string proveedorCodigo, int motivo, string cuentaCodigo, DateTimeOffset instante, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE fact.SugerenciaCuenta
            SET Veces = Veces + 1, UltimoUso = @instante
            WHERE ProveedorCodigo = @proveedorCodigo AND Motivo = @motivo AND CuentaCodigo = @cuentaCodigo;

            IF @@ROWCOUNT = 0
                INSERT INTO fact.SugerenciaCuenta (ProveedorCodigo, Motivo, CuentaCodigo, Veces, UltimoUso)
                VALUES (@proveedorCodigo, @motivo, @cuentaCodigo, 1, @instante);
            """;
        command.Parameters.AddWithValue("@proveedorCodigo", proveedorCodigo);
        command.Parameters.AddWithValue("@motivo", motivo);
        command.Parameters.AddWithValue("@cuentaCodigo", cuentaCodigo);
        command.Parameters.AddWithValue("@instante", instante.UtcDateTime);

        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IReadOnlyList<SugerenciaCuenta>> LeerListaAsync(SqlCommand command, CancellationToken ct)
    {
        var resultado = new List<SugerenciaCuenta>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            resultado.Add(Map(reader));
        }

        return resultado;
    }

    private static SugerenciaCuenta Map(SqlDataReader reader) =>
        new(
            ProveedorCodigo: reader.GetString(0).TrimEnd(),
            Motivo: reader.GetInt32(1),
            CuentaCodigo: reader.GetString(2),
            Veces: reader.GetInt32(3),
            UltimoUso: new DateTimeOffset(reader.GetDateTime(4), TimeSpan.Zero));
}
