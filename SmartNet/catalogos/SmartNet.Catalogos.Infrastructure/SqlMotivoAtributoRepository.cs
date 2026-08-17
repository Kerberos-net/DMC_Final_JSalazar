using Microsoft.Data.SqlClient;
using SmartNet.Catalogos.Core;

namespace SmartNet.Catalogos.Infrastructure;

/// <summary>
/// SQL adapter over <c>fact.MotivoAtributo</c> for <see cref="IMotivoAtributoRepository"/>
/// (design.md Interfaces/Contracts). SELECT/INSERT/UPDATE only, no <c>DELETE</c> method
/// (`008_usuarios_y_permisos.sql` grants none to `fact_api`). <c>Activo</c>/`origen '02'`
/// filtering never happens in this adapter's SQL -- it returns raw rows, Core applies the filter.
/// </summary>
public sealed class SqlMotivoAtributoRepository : IMotivoAtributoRepository
{
    private readonly string _connectionString;

    public SqlMotivoAtributoRepository(string connectionString) => _connectionString = connectionString;

    public async Task<MotivoAtributo?> ObtenerAsync(int motivo, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Motivo, Activo, OrigenLibro
            FROM fact.MotivoAtributo
            WHERE Motivo = @motivo;
            """;
        command.Parameters.AddWithValue("@motivo", motivo);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return Map(reader);
    }

    public async Task<IReadOnlyList<MotivoAtributo>> ListarAsync(CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Motivo, Activo, OrigenLibro FROM fact.MotivoAtributo;";

        var resultado = new List<MotivoAtributo>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            resultado.Add(Map(reader));
        }

        return resultado;
    }

    // Single statement, both branches write EVERY column (Activo AND OrigenLibro) -- the known bug
    // class in this project is an UPDATE that forgets a field.
    public async Task GuardarAsync(MotivoAtributo atributo, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE fact.MotivoAtributo
            SET Activo = @activo, OrigenLibro = @origenLibro
            WHERE Motivo = @motivo;

            IF @@ROWCOUNT = 0
                INSERT INTO fact.MotivoAtributo (Motivo, Activo, OrigenLibro)
                VALUES (@motivo, @activo, @origenLibro);
            """;
        command.Parameters.AddWithValue("@motivo", atributo.Motivo);
        command.Parameters.AddWithValue("@activo", atributo.Activo);
        command.Parameters.AddWithValue("@origenLibro", atributo.OrigenLibro);

        await command.ExecuteNonQueryAsync(ct);
    }

    private static MotivoAtributo Map(SqlDataReader reader) =>
        new(
            Motivo: reader.GetInt32(0),
            Activo: reader.GetBoolean(1),
            OrigenLibro: reader.GetString(2).TrimEnd());
}
