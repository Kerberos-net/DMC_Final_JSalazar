using Microsoft.Data.SqlClient;
using SmartNet.Auth.Core;

namespace SmartNet.Auth.Infrastructure;

/// <summary>
/// SQL adapter over <c>fact.Sesion</c> for <see cref="ISesionRepository"/> (design.md
/// Decision 5). <see cref="SqlSesionTicketStore"/> is a THIN adapter over this repository, never
/// the other way around.
/// </summary>
public sealed class SqlSesionRepository : ISesionRepository
{
    private readonly string _connectionString;

    public SqlSesionRepository(string connectionString) => _connectionString = connectionString;

    public async Task CreateAsync(
        long usuarioId, string tokenHash, DateTimeOffset expiraEn, string ticket, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO fact.Sesion (TokenHash, UsuarioId, ExpiraEn, UltimaActividadEn, Ticket)
            VALUES (@tokenHash, @usuarioId, @expiraEn, SYSUTCDATETIME(), @ticket);
            """;
        command.Parameters.AddWithValue("@tokenHash", tokenHash);
        command.Parameters.AddWithValue("@usuarioId", usuarioId);
        command.Parameters.AddWithValue("@expiraEn", expiraEn.UtcDateTime);
        command.Parameters.AddWithValue("@ticket", ticket);

        await command.ExecuteNonQueryAsync(ct);
    }

    // The boundary design.md's Testing Strategy calls out explicitly: RevocadaEn IS NULL AND
    // ExpiraEn > @ahora. An expired-but-not-revoked row must NOT come back.
    public async Task<SesionActiva?> FindActiveAsync(string tokenHash, DateTimeOffset ahora, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT SesionId, UsuarioId, TokenHash, CreadaEn, ExpiraEn, UltimaActividadEn, Ticket
            FROM fact.Sesion
            WHERE TokenHash = @tokenHash AND RevocadaEn IS NULL AND ExpiraEn > @ahora;
            """;
        command.Parameters.AddWithValue("@tokenHash", tokenHash);
        command.Parameters.AddWithValue("@ahora", ahora.UtcDateTime);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new SesionActiva(
            SesionId: reader.GetInt64(0),
            UsuarioId: reader.GetInt64(1),
            TokenHash: reader.GetString(2),
            CreadaEn: AsUtcOffset(reader.GetDateTime(3)),
            ExpiraEn: AsUtcOffset(reader.GetDateTime(4)),
            UltimaActividadEn: AsUtcOffset(reader.GetDateTime(5)),
            Ticket: reader.GetString(6));
    }

    public async Task RenewAsync(
        string tokenHash, DateTimeOffset expiraEn, DateTimeOffset ahora, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE fact.Sesion
            SET ExpiraEn = @expiraEn, UltimaActividadEn = @ahora
            WHERE TokenHash = @tokenHash;
            """;
        command.Parameters.AddWithValue("@expiraEn", expiraEn.UtcDateTime);
        command.Parameters.AddWithValue("@ahora", ahora.UtcDateTime);
        command.Parameters.AddWithValue("@tokenHash", tokenHash);

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task RevokeAsync(
        string tokenHash, MotivoRevocacion motivo, DateTimeOffset ahora, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE fact.Sesion
            SET RevocadaEn = @ahora, MotivoRevocacion = @motivo
            WHERE TokenHash = @tokenHash;
            """;
        command.Parameters.AddWithValue("@ahora", ahora.UtcDateTime);
        command.Parameters.AddWithValue("@motivo", MotivoRevocacionCodec.ToDbValue(motivo));
        command.Parameters.AddWithValue("@tokenHash", tokenHash);

        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task RevokeAllForUsuarioAsync(
        long usuarioId, MotivoRevocacion motivo, DateTimeOffset ahora, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE fact.Sesion
            SET RevocadaEn = @ahora, MotivoRevocacion = @motivo
            WHERE UsuarioId = @usuarioId AND RevocadaEn IS NULL;
            """;
        command.Parameters.AddWithValue("@ahora", ahora.UtcDateTime);
        command.Parameters.AddWithValue("@motivo", MotivoRevocacionCodec.ToDbValue(motivo));
        command.Parameters.AddWithValue("@usuarioId", usuarioId);

        await command.ExecuteNonQueryAsync(ct);
    }

    private static DateTimeOffset AsUtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
