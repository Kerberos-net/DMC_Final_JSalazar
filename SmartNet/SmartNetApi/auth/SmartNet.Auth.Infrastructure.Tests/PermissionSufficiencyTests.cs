using Microsoft.Data.SqlClient;
using SmartNet.Db.TestBootstrap;

namespace SmartNet.Auth.Infrastructure.Tests;

/// <summary>
/// Task 3.13/3.14 -- the load-bearing check design.md's Testing Strategy names explicitly: "a
/// missing GRANT would ship green" without it. Every other test in this project runs against
/// `TestDatabaseFixture.ConnectionString`, which is effectively an elevated/db_owner connection
/// (the database was just CREATEd by this same principal) -- it would never surface a missing
/// `usr_api` grant. This suite replays the EXACT SQL text every adapter above issues, byte-for-byte
/// copied from the adapter source, through `ExecuteAsUserAsync("usr_api", ...)` against the real
/// grants shipped in 002/008/011/012 -- not a mock, not an elevated connection.
/// </summary>
public sealed class PermissionSufficiencyTests : IAsyncLifetime
{
    private const string UsrApi = "usr_api";

    private TestDatabaseFixture _db = null!;
    private long _usuarioId;

    public async Task InitializeAsync()
    {
        _db = await MigratedDatabase();
        // Seeding as the test-fixture connection (setup, not the thing under test) -- the same
        // pattern PermissionMatrixTests.SeedUsuario uses.
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO fact.Usuario (NombreUsuario, ClaveHash) VALUES ('usr_permsuff_owner', 'hash-de-prueba');");
        _usuarioId = await _db.ExecuteScalarAsync<long>(
            "SELECT UsuarioId FROM fact.Usuario WHERE NombreUsuario = 'usr_permsuff_owner';");
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static async Task<TestDatabaseFixture> MigratedDatabase()
    {
        var db = await TestDatabaseFixture.CreateAsync();
        try
        {
            await db.CreateWithoutLoginUserAsync("usr_api");
            await db.CreateWithoutLoginUserAsync("usr_worker");
            await db.CreateExternalDboCatalogsAsync();
            await db.SeedDboMotivoFixtureRowsAsync();
            var exitCode = db.RunMigrations();
            Assert.Equal(0, exitCode);
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    // ---------------------------------------------------------------------------------------
    // SqlUsuarioRepository -- exact SQL text from SqlUsuarioRepository.cs.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task UsrApi_CanExecute_FindByNameAsyncSelect()
    {
        var rowsRead = await _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT UsuarioId, NombreUsuario, ClaveHash, IntentosFallidos, NivelBloqueo, BloqueadoHasta, Activo
                FROM fact.Usuario
                WHERE NombreUsuario = @nombreUsuario;
                """;
            command.Parameters.AddWithValue("@nombreUsuario", "usr_permsuff_owner");
            await using var reader = await command.ExecuteReaderAsync();
            var read = 0;
            while (await reader.ReadAsync())
            {
                read++;
            }
            return read;
        });

        Assert.Equal(1, rowsRead);
    }

    [Fact]
    public async Task UsrApi_CanExecute_SaveCredentialStateAsyncUpdate()
    {
        var rowsAffected = await _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE fact.Usuario
                SET IntentosFallidos = @intentosFallidos,
                    NivelBloqueo = @nivelBloqueo,
                    BloqueadoHasta = @bloqueadoHasta
                WHERE UsuarioId = @usuarioId;
                """;
            command.Parameters.AddWithValue("@intentosFallidos", 3);
            command.Parameters.AddWithValue("@nivelBloqueo", 1);
            command.Parameters.AddWithValue("@bloqueadoHasta", DateTime.UtcNow.AddMinutes(15));
            command.Parameters.AddWithValue("@usuarioId", _usuarioId);
            return await command.ExecuteNonQueryAsync();
        });

        Assert.Equal(1, rowsAffected);
    }

    [Fact]
    public async Task UsrApi_CanExecute_UpdateClaveHashAsyncUpdate()
    {
        var rowsAffected = await _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE fact.Usuario
                SET ClaveHash = @claveHash
                WHERE UsuarioId = @usuarioId;
                """;
            command.Parameters.AddWithValue(
                "@claveHash",
                "$argon2id$v=19$m=19456,t=2,p=1$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
            command.Parameters.AddWithValue("@usuarioId", _usuarioId);
            return await command.ExecuteNonQueryAsync();
        });

        Assert.Equal(1, rowsAffected);
    }

    // ---------------------------------------------------------------------------------------
    // SqlSesionRepository -- exact SQL text from SqlSesionRepository.cs.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task UsrApi_CanExecute_CreateAsyncInsert()
    {
        var tokenHash = "a".PadLeft(64, '0');
        var rowsAffected = await _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO fact.Sesion (TokenHash, UsuarioId, ExpiraEn, UltimaActividadEn, Ticket)
                VALUES (@tokenHash, @usuarioId, @expiraEn, SYSUTCDATETIME(), @ticket);
                """;
            command.Parameters.AddWithValue("@tokenHash", tokenHash);
            command.Parameters.AddWithValue("@usuarioId", _usuarioId);
            command.Parameters.AddWithValue("@expiraEn", DateTime.UtcNow.AddHours(8));
            command.Parameters.AddWithValue("@ticket", "ticket-de-prueba-permission-sufficiency");
            return await command.ExecuteNonQueryAsync();
        });

        Assert.Equal(1, rowsAffected);
    }

    [Fact]
    public async Task UsrApi_CanExecute_FindActiveAsyncSelect()
    {
        var tokenHash = "b".PadLeft(64, '0');
        await _db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.Sesion (TokenHash, UsuarioId, ExpiraEn, UltimaActividadEn, Ticket)
             VALUES ('{tokenHash}', {_usuarioId}, DATEADD(HOUR, 8, SYSUTCDATETIME()), SYSUTCDATETIME(), 'ticket');
             """);

        var rowsRead = await _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT SesionId, UsuarioId, TokenHash, CreadaEn, ExpiraEn, UltimaActividadEn, Ticket
                FROM fact.Sesion
                WHERE TokenHash = @tokenHash AND RevocadaEn IS NULL AND ExpiraEn > @ahora;
                """;
            command.Parameters.AddWithValue("@tokenHash", tokenHash);
            command.Parameters.AddWithValue("@ahora", DateTime.UtcNow);
            await using var reader = await command.ExecuteReaderAsync();
            var read = 0;
            while (await reader.ReadAsync())
            {
                read++;
            }
            return read;
        });

        Assert.Equal(1, rowsRead);
    }

    [Fact]
    public async Task UsrApi_CanExecute_RenewAsyncUpdate()
    {
        var tokenHash = "c".PadLeft(64, '0');
        await _db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.Sesion (TokenHash, UsuarioId, ExpiraEn, UltimaActividadEn, Ticket)
             VALUES ('{tokenHash}', {_usuarioId}, DATEADD(HOUR, 4, SYSUTCDATETIME()), SYSUTCDATETIME(), 'ticket');
             """);

        var rowsAffected = await _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE fact.Sesion
                SET ExpiraEn = @expiraEn, UltimaActividadEn = @ahora
                WHERE TokenHash = @tokenHash;
                """;
            command.Parameters.AddWithValue("@expiraEn", DateTime.UtcNow.AddHours(8));
            command.Parameters.AddWithValue("@ahora", DateTime.UtcNow);
            command.Parameters.AddWithValue("@tokenHash", tokenHash);
            return await command.ExecuteNonQueryAsync();
        });

        Assert.Equal(1, rowsAffected);
    }

    [Fact]
    public async Task UsrApi_CanExecute_RevokeAsyncUpdate()
    {
        var tokenHash = "d".PadLeft(64, '0');
        await _db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.Sesion (TokenHash, UsuarioId, ExpiraEn, UltimaActividadEn, Ticket)
             VALUES ('{tokenHash}', {_usuarioId}, DATEADD(HOUR, 8, SYSUTCDATETIME()), SYSUTCDATETIME(), 'ticket');
             """);

        var rowsAffected = await _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE fact.Sesion
                SET RevocadaEn = @ahora, MotivoRevocacion = @motivo
                WHERE TokenHash = @tokenHash;
                """;
            command.Parameters.AddWithValue("@ahora", DateTime.UtcNow);
            command.Parameters.AddWithValue("@motivo", "CIERRE_SESION");
            command.Parameters.AddWithValue("@tokenHash", tokenHash);
            return await command.ExecuteNonQueryAsync();
        });

        Assert.Equal(1, rowsAffected);
    }

    [Fact]
    public async Task UsrApi_CanExecute_RevokeAllForUsuarioAsyncUpdate()
    {
        var tokenHash = "e".PadLeft(64, '0');
        await _db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.Sesion (TokenHash, UsuarioId, ExpiraEn, UltimaActividadEn, Ticket)
             VALUES ('{tokenHash}', {_usuarioId}, DATEADD(HOUR, 8, SYSUTCDATETIME()), SYSUTCDATETIME(), 'ticket');
             """);

        var rowsAffected = await _db.ExecuteAsUserAsync(UsrApi, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE fact.Sesion
                SET RevocadaEn = @ahora, MotivoRevocacion = @motivo
                WHERE UsuarioId = @usuarioId AND RevocadaEn IS NULL;
                """;
            command.Parameters.AddWithValue("@ahora", DateTime.UtcNow);
            command.Parameters.AddWithValue("@motivo", "RESTABLECIMIENTO");
            command.Parameters.AddWithValue("@usuarioId", _usuarioId);
            return await command.ExecuteNonQueryAsync();
        });

        Assert.Equal(1, rowsAffected);
    }

}
