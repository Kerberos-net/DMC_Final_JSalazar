using SmartNet.Auth.Core;
using SmartNet.Db.TestBootstrap;

namespace SmartNet.Auth.Infrastructure.Tests;

/// <summary>
/// Task 3.7/3.8 -- <see cref="SqlUsuarioRepository"/> against a real, migrated
/// <c>fact_test_&lt;id&gt;</c> database (`TestDatabaseFixture`). Covers design.md's Testing
/// Strategy bug class by name: "a state field the UPDATE forgets to write".
/// </summary>
public sealed class SqlUsuarioRepositoryTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;

    public async Task InitializeAsync() => _db = await MigratedDatabase();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    // Mirrors SmartNet.Db.Runner.Tests.PermissionMatrixTests.MigratedDatabaseWithUsers: 010's
    // INSERT ... SELECT ... FROM dbo.Motivo needs the external dbo catalogs seeded first, or the
    // runner halts there and 011/012 never apply. try/catch avoids leaking the already-created
    // database if migration throws before returning it (the diagnosed Work Unit 3 leak cause).
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

    private async Task<long> InsertUsuarioAsync(
        string nombreUsuario,
        string claveHash = "$argon2id$v=19$m=19456,t=2,p=1$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
        int intentosFallidos = 0,
        int nivelBloqueo = 0,
        DateTimeOffset? bloqueadoHasta = null)
    {
        var bloqueadoHastaLiteral = bloqueadoHasta is null ? "NULL" : $"'{bloqueadoHasta:O}'";
        await _db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.Usuario (NombreUsuario, ClaveHash, IntentosFallidos, NivelBloqueo, BloqueadoHasta)
             VALUES ('{nombreUsuario}', '{claveHash}', {intentosFallidos}, {nivelBloqueo}, {bloqueadoHastaLiteral});
             """);

        return await _db.ExecuteScalarAsync<long>(
            $"SELECT UsuarioId FROM fact.Usuario WHERE NombreUsuario = '{nombreUsuario}';");
    }

    [Fact]
    public async Task FindByNameAsync_MapsEveryColumn_IncludingNivelBloqueo()
    {
        var bloqueadoHasta = DateTimeOffset.UtcNow.AddMinutes(15);
        var usuarioId = await InsertUsuarioAsync(
            "usr_findbyname", intentosFallidos: 3, nivelBloqueo: 2, bloqueadoHasta: bloqueadoHasta);

        var sut = new SqlUsuarioRepository(_db.ConnectionString);

        var estado = await sut.FindByNameAsync("usr_findbyname", CancellationToken.None);

        Assert.NotNull(estado);
        Assert.Equal(usuarioId, estado!.UsuarioId);
        Assert.Equal("usr_findbyname", estado.NombreUsuario);
        Assert.Equal(3, estado.IntentosFallidos);
        Assert.Equal(2, estado.NivelBloqueo);
        Assert.NotNull(estado.BloqueadoHasta);
        Assert.Equal(bloqueadoHasta.ToUnixTimeMilliseconds(), estado.BloqueadoHasta!.Value.ToUnixTimeMilliseconds());
        Assert.True(estado.Activo);
    }

    [Fact]
    public async Task FindByNameAsync_ReturnsNull_ForAnUnknownUser()
    {
        var sut = new SqlUsuarioRepository(_db.ConnectionString);

        var estado = await sut.FindByNameAsync("no-existe-este-usuario", CancellationToken.None);

        Assert.Null(estado);
    }

    // The bug class design.md's Testing Strategy names explicitly: "a state field the UPDATE
    // forgets to write". This asserts ALL THREE lockout columns round-trip in one call, not just a
    // happy-path single-field check that a dropped column in the UPDATE would not catch.
    [Fact]
    public async Task SaveCredentialStateAsync_WritesAllThreeLockoutFields_InOneUpdate()
    {
        var usuarioId = await InsertUsuarioAsync("usr_save_lockout");
        var sut = new SqlUsuarioRepository(_db.ConnectionString);
        var nuevoBloqueoHasta = DateTimeOffset.UtcNow.AddMinutes(30);

        var estadoOriginal = (await sut.FindByNameAsync("usr_save_lockout", CancellationToken.None))!;
        var estadoNuevo = estadoOriginal with
        {
            IntentosFallidos = 4,
            NivelBloqueo = 2,
            BloqueadoHasta = nuevoBloqueoHasta,
        };

        await sut.SaveCredentialStateAsync(estadoNuevo, CancellationToken.None);

        var intentosFallidos = await _db.ExecuteScalarAsync<int>(
            $"SELECT IntentosFallidos FROM fact.Usuario WHERE UsuarioId = {usuarioId};");
        var nivelBloqueo = await _db.ExecuteScalarAsync<int>(
            $"SELECT NivelBloqueo FROM fact.Usuario WHERE UsuarioId = {usuarioId};");
        var bloqueadoHasta = await _db.ExecuteScalarAsync<DateTime>(
            $"SELECT BloqueadoHasta FROM fact.Usuario WHERE UsuarioId = {usuarioId};");

        Assert.Equal(4, intentosFallidos);
        Assert.Equal(2, nivelBloqueo);
        Assert.Equal(
            nuevoBloqueoHasta.UtcDateTime,
            bloqueadoHasta,
            TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    public async Task SaveCredentialStateAsync_CanClearBloqueadoHasta_ToNull()
    {
        var usuarioId = await InsertUsuarioAsync(
            "usr_clear_lockout", intentosFallidos: 0, nivelBloqueo: 1,
            bloqueadoHasta: DateTimeOffset.UtcNow.AddMinutes(15));
        var sut = new SqlUsuarioRepository(_db.ConnectionString);

        var estadoOriginal = (await sut.FindByNameAsync("usr_clear_lockout", CancellationToken.None))!;
        var estadoLimpio = estadoOriginal with { IntentosFallidos = 0, NivelBloqueo = 0, BloqueadoHasta = null };

        await sut.SaveCredentialStateAsync(estadoLimpio, CancellationToken.None);

        var bloqueadoHastaEsNulo = await _db.ExecuteScalarAsync<bool?>(
            $"SELECT CAST(CASE WHEN BloqueadoHasta IS NULL THEN 1 ELSE 0 END AS BIT) FROM fact.Usuario WHERE UsuarioId = {usuarioId};");

        Assert.True(bloqueadoHastaEsNulo);
    }

    [Fact]
    public async Task UpdateClaveHashAsync_UpdatesOnlyClaveHash()
    {
        const string claveHashOriginal = "$argon2id$v=19$m=19456,t=2,p=1$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        const string claveHashNueva = "$argon2id$v=19$m=19456,t=2,p=1$BBBBBBBBBBBBBBBBBBBBBB$BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
        var bloqueadoHasta = DateTimeOffset.UtcNow.AddMinutes(45);
        var usuarioId = await InsertUsuarioAsync(
            "usr_update_hash", claveHash: claveHashOriginal,
            intentosFallidos: 3, nivelBloqueo: 2, bloqueadoHasta: bloqueadoHasta);
        var sut = new SqlUsuarioRepository(_db.ConnectionString);

        await sut.UpdateClaveHashAsync(usuarioId, claveHashNueva, CancellationToken.None);

        var estado = await sut.FindByNameAsync("usr_update_hash", CancellationToken.None);

        Assert.NotNull(estado);
        Assert.Equal(claveHashNueva, estado!.ClaveHash);
        // Lockout fields set to NON-DEFAULT values before the call must be untouched by it.
        Assert.Equal(3, estado.IntentosFallidos);
        Assert.Equal(2, estado.NivelBloqueo);
        Assert.NotNull(estado.BloqueadoHasta);
        // DATETIME2(3) round-trip via an 'O'-formatted literal may round the last millisecond.
        var deltaMs = Math.Abs(
            bloqueadoHasta.ToUnixTimeMilliseconds() - estado.BloqueadoHasta!.Value.ToUnixTimeMilliseconds());
        Assert.True(deltaMs <= 1, $"Expected BloqueadoHasta within 1ms, actual delta {deltaMs}ms.");
    }
}
