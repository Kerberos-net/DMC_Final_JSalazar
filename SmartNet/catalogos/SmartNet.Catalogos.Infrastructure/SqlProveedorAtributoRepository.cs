using Microsoft.Data.SqlClient;
using SmartNet.Catalogos.Core;

namespace SmartNet.Catalogos.Infrastructure;

/// <summary>
/// SQL adapter over <c>fact.ProveedorAtributo</c> for <see cref="IProveedorAtributoRepository"/>
/// (design.md Interfaces/Contracts). SELECT/INSERT/UPDATE only -- `008_usuarios_y_permisos.sql`
/// grants `fact_api` no `DELETE` on this table, so there is no delete method to omit by mistake.
/// No existence check against <c>dbo.Proveedor</c> is issued here (design.md Decision 2): that
/// would be a WEAKER rule than the real one (candidacy per motivo, not raw existence) and a
/// TOCTOU hazard across systems -- the real guard is <c>EsCandidata</c>-style read-time filtering,
/// owned by item #9.
/// </summary>
public sealed class SqlProveedorAtributoRepository : IProveedorAtributoRepository
{
    private readonly string _connectionString;

    public SqlProveedorAtributoRepository(string connectionString) => _connectionString = connectionString;

    public async Task<ProveedorAtributo?> ObtenerAsync(string proveedorCodigo, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ProveedorCodigo, EsRelacionada
            FROM fact.ProveedorAtributo
            WHERE ProveedorCodigo = @proveedorCodigo;
            """;
        command.Parameters.AddWithValue("@proveedorCodigo", proveedorCodigo);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return Map(reader);
    }

    // Single statement, both branches write every column -- the known bug class in this project is
    // an UPDATE that forgets a field. Upsert keyed on the primary key (ProveedorCodigo), never on a
    // dbo.* existence check (design.md Decision 2).
    public async Task GuardarAsync(ProveedorAtributo atributo, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE fact.ProveedorAtributo
            SET EsRelacionada = @esRelacionada
            WHERE ProveedorCodigo = @proveedorCodigo;

            IF @@ROWCOUNT = 0
                INSERT INTO fact.ProveedorAtributo (ProveedorCodigo, EsRelacionada)
                VALUES (@proveedorCodigo, @esRelacionada);
            """;
        command.Parameters.AddWithValue("@proveedorCodigo", atributo.ProveedorCodigo);
        command.Parameters.AddWithValue("@esRelacionada", atributo.EsRelacionada);

        await command.ExecuteNonQueryAsync(ct);
    }

    private static ProveedorAtributo Map(SqlDataReader reader) =>
        new(
            ProveedorCodigo: reader.GetString(0).TrimEnd(),
            EsRelacionada: reader.GetBoolean(1));
}
