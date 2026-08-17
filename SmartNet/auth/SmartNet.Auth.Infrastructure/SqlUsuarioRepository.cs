using Microsoft.Data.SqlClient;
using SmartNet.Auth.Core;

namespace SmartNet.Auth.Infrastructure;

/// <summary>
/// SQL adapter over <c>fact.Usuario</c> for <see cref="IUsuarioRepository"/> (design.md
/// Decision 5). Runs under <c>usr_api</c>'s grants (permission-sufficiency is proved separately,
/// task 3.13, against the real grants shipped in 002/008/012).
/// </summary>
public sealed class SqlUsuarioRepository : IUsuarioRepository
{
    private readonly string _connectionString;

    public SqlUsuarioRepository(string connectionString) => _connectionString = connectionString;

    public async Task<UsuarioCredentialState?> FindByNameAsync(string nombreUsuario, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT UsuarioId, NombreUsuario, ClaveHash, IntentosFallidos, NivelBloqueo, BloqueadoHasta, Activo
            FROM fact.Usuario
            WHERE NombreUsuario = @nombreUsuario;
            """;
        command.Parameters.AddWithValue("@nombreUsuario", nombreUsuario);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new UsuarioCredentialState(
            UsuarioId: reader.GetInt64(0),
            NombreUsuario: reader.GetString(1),
            ClaveHash: reader.GetString(2),
            IntentosFallidos: reader.GetInt32(3),
            NivelBloqueo: reader.GetInt32(4),
            BloqueadoHasta: reader.IsDBNull(5) ? null : new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc)),
            Activo: reader.GetBoolean(6));
    }

    // The sole INSERT: 002_seguridad.sql's own header — "the first user is created later by the
    // application's administration command, never by migration" — this is that command's write
    // path (SmartNet.Admin's `usuario crear`, tasks.md 5.4/5.5). OUTPUT INSERTED.UsuarioId reads
    // the generated identity back on the same round-trip.
    public async Task<long> CreateAsync(string nombreUsuario, string claveHash, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO fact.Usuario (NombreUsuario, ClaveHash)
            OUTPUT INSERTED.UsuarioId
            VALUES (@nombreUsuario, @claveHash);
            """;
        command.Parameters.AddWithValue("@nombreUsuario", nombreUsuario);
        command.Parameters.AddWithValue("@claveHash", claveHash);

        var usuarioId = await command.ExecuteScalarAsync(ct);
        return (long)usuarioId!;
    }

    // Widens to THREE columns per design.md Decision 8 -- the exact "state field the UPDATE
    // forgets to write" bug class this signature (state-shaped, not field-shaped) exists to guard
    // against. Deliberately does NOT touch ClaveHash: that column has exactly one writer,
    // UpdateClaveHashAsync, per design.md's separation of the two concerns.
    public async Task SaveCredentialStateAsync(UsuarioCredentialState estado, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE fact.Usuario
            SET IntentosFallidos = @intentosFallidos,
                NivelBloqueo = @nivelBloqueo,
                BloqueadoHasta = @bloqueadoHasta
            WHERE UsuarioId = @usuarioId;
            """;
        command.Parameters.AddWithValue("@intentosFallidos", estado.IntentosFallidos);
        command.Parameters.AddWithValue("@nivelBloqueo", estado.NivelBloqueo);
        command.Parameters.AddWithValue("@bloqueadoHasta", (object?)estado.BloqueadoHasta?.UtcDateTime ?? DBNull.Value);
        command.Parameters.AddWithValue("@usuarioId", estado.UsuarioId);

        await command.ExecuteNonQueryAsync(ct);
    }

    // The sole writer of ClaveHash. Deliberately touches nothing else -- proven by
    // Argon2idPasswordHasherTests' sibling test in this project, and by
    // SqlUsuarioRepositoryTests.UpdateClaveHashAsync_UpdatesOnlyClaveHash, which sets the lockout
    // fields to non-default values first and asserts they survive this call untouched.
    public async Task UpdateClaveHashAsync(long usuarioId, string claveHash, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE fact.Usuario
            SET ClaveHash = @claveHash
            WHERE UsuarioId = @usuarioId;
            """;
        command.Parameters.AddWithValue("@claveHash", claveHash);
        command.Parameters.AddWithValue("@usuarioId", usuarioId);

        await command.ExecuteNonQueryAsync(ct);
    }
}
