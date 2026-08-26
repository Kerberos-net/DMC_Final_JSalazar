using SmartNet.Auth.Core;
using SmartNet.Db.TestBootstrap;

namespace SmartNet.Auth.Infrastructure.Tests;

/// <summary>
/// Task 3.9/3.10 -- <see cref="SqlSesionRepository"/> against a real, migrated
/// <c>fact_test_&lt;id&gt;</c> database. Covers the boundary design.md's Testing Strategy calls
/// out for <c>FindActiveAsync</c>: an expired-but-not-revoked row must NOT come back.
/// </summary>
public sealed class SqlSesionRepositoryTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;
    private long _usuarioId;

    public async Task InitializeAsync()
    {
        _db = await MigratedDatabase();
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO fact.Usuario (NombreUsuario, ClaveHash) VALUES ('usr_sesion_owner', 'hash-de-prueba');");
        _usuarioId = await _db.ExecuteScalarAsync<long>(
            "SELECT UsuarioId FROM fact.Usuario WHERE NombreUsuario = 'usr_sesion_owner';");
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

    [Fact]
    public async Task CreateAsync_InsertsARow()
    {
        var sut = new SqlSesionRepository(_db.ConnectionString);
        var ahora = DateTimeOffset.UtcNow;

        await sut.CreateAsync(_usuarioId, "a".PadLeft(64, '0'), ahora.AddHours(8), "ticket-de-prueba", CancellationToken.None);

        var count = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.Sesion WHERE UsuarioId = {_usuarioId};");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task FindActiveAsync_ReturnsTheRow_WhenNotRevokedAndNotExpired()
    {
        var sut = new SqlSesionRepository(_db.ConnectionString);
        var ahora = DateTimeOffset.UtcNow;
        var tokenHash = "b".PadLeft(64, '0');
        await sut.CreateAsync(_usuarioId, tokenHash, ahora.AddHours(8), "ticket-vivo", CancellationToken.None);

        var activa = await sut.FindActiveAsync(tokenHash, ahora, CancellationToken.None);

        Assert.NotNull(activa);
        Assert.Equal(_usuarioId, activa!.UsuarioId);
        Assert.Equal(tokenHash, activa.TokenHash);
        Assert.Equal("ticket-vivo", activa.Ticket);
    }

    // The boundary design.md's Testing Strategy names explicitly: RevocadaEn IS NULL AND
    // ExpiraEn > @ahora. An expired-but-not-revoked row must NOT come back.
    [Fact]
    public async Task FindActiveAsync_ReturnsNull_ForAnExpiredButNotRevokedRow()
    {
        var sut = new SqlSesionRepository(_db.ConnectionString);
        var ahora = DateTimeOffset.UtcNow;
        var tokenHash = "c".PadLeft(64, '0');
        // Expired one second ago -- never revoked (RevocadaEn stays NULL, MotivoRevocacion NULL,
        // satisfying CK_Sesion_Revocacion).
        await sut.CreateAsync(_usuarioId, tokenHash, ahora.AddSeconds(-1), "ticket-expirado", CancellationToken.None);

        var activa = await sut.FindActiveAsync(tokenHash, ahora, CancellationToken.None);

        Assert.Null(activa);
    }

    [Fact]
    public async Task FindActiveAsync_ReturnsNull_ForARevokedRow()
    {
        var sut = new SqlSesionRepository(_db.ConnectionString);
        var ahora = DateTimeOffset.UtcNow;
        var tokenHash = "d".PadLeft(64, '0');
        await sut.CreateAsync(_usuarioId, tokenHash, ahora.AddHours(8), "ticket-revocado", CancellationToken.None);
        await sut.RevokeAsync(tokenHash, MotivoRevocacion.CierreSesion, ahora, CancellationToken.None);

        var activa = await sut.FindActiveAsync(tokenHash, ahora, CancellationToken.None);

        Assert.Null(activa);
    }

    [Fact]
    public async Task FindActiveAsync_ReturnsNull_ForAnUnknownTokenHash()
    {
        var sut = new SqlSesionRepository(_db.ConnectionString);

        var activa = await sut.FindActiveAsync("e".PadLeft(64, '0'), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Null(activa);
    }

    [Fact]
    public async Task RenewAsync_UpdatesExpiraEnAndUltimaActividadEn()
    {
        var sut = new SqlSesionRepository(_db.ConnectionString);
        var ahora = DateTimeOffset.UtcNow;
        var tokenHash = "f".PadLeft(64, '0');
        await sut.CreateAsync(_usuarioId, tokenHash, ahora.AddHours(4), "ticket-a-renovar", CancellationToken.None);

        var nuevaExpiracion = ahora.AddHours(8);
        await sut.RenewAsync(tokenHash, nuevaExpiracion, ahora, CancellationToken.None);

        var expiraEn = await _db.ExecuteScalarAsync<DateTime>(
            $"SELECT ExpiraEn FROM fact.Sesion WHERE TokenHash = '{tokenHash}';");
        var ultimaActividadEn = await _db.ExecuteScalarAsync<DateTime>(
            $"SELECT UltimaActividadEn FROM fact.Sesion WHERE TokenHash = '{tokenHash}';");

        Assert.Equal(nuevaExpiracion.UtcDateTime, expiraEn, TimeSpan.FromMilliseconds(5));
        Assert.Equal(ahora.UtcDateTime, ultimaActividadEn, TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    public async Task RevokeAsync_SetsRevocadaEnAndMotivoRevocacion()
    {
        var sut = new SqlSesionRepository(_db.ConnectionString);
        var ahora = DateTimeOffset.UtcNow;
        var tokenHash = "1".PadLeft(64, '0');
        await sut.CreateAsync(_usuarioId, tokenHash, ahora.AddHours(8), "ticket-a-revocar", CancellationToken.None);

        await sut.RevokeAsync(tokenHash, MotivoRevocacion.CierreSesion, ahora, CancellationToken.None);

        var motivo = await _db.ExecuteScalarAsync<string>(
            $"SELECT MotivoRevocacion FROM fact.Sesion WHERE TokenHash = '{tokenHash}';");
        var revocadaEnEsNula = await _db.ExecuteScalarAsync<bool?>(
            $"SELECT CAST(CASE WHEN RevocadaEn IS NULL THEN 1 ELSE 0 END AS BIT) FROM fact.Sesion WHERE TokenHash = '{tokenHash}';");

        Assert.Equal("CIERRE_SESION", motivo);
        Assert.False(revocadaEnEsNula);
    }

    [Fact]
    public async Task RevokeAllForUsuarioAsync_RevokesEveryLiveSession_ForThatUser()
    {
        var sut = new SqlSesionRepository(_db.ConnectionString);
        var ahora = DateTimeOffset.UtcNow;
        var tokenHash1 = "2".PadLeft(64, '0');
        var tokenHash2 = "3".PadLeft(64, '0');
        await sut.CreateAsync(_usuarioId, tokenHash1, ahora.AddHours(8), "ticket-1", CancellationToken.None);
        await sut.CreateAsync(_usuarioId, tokenHash2, ahora.AddHours(8), "ticket-2", CancellationToken.None);

        await sut.RevokeAllForUsuarioAsync(_usuarioId, MotivoRevocacion.Restablecimiento, ahora, CancellationToken.None);

        var activa1 = await sut.FindActiveAsync(tokenHash1, ahora, CancellationToken.None);
        var activa2 = await sut.FindActiveAsync(tokenHash2, ahora, CancellationToken.None);

        Assert.Null(activa1);
        Assert.Null(activa2);
    }

    [Fact]
    public async Task RevokeAllForUsuarioAsync_DoesNotTouchAnotherUsersSession()
    {
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO fact.Usuario (NombreUsuario, ClaveHash) VALUES ('usr_sesion_otro', 'hash-de-prueba');");
        var otroUsuarioId = await _db.ExecuteScalarAsync<long>(
            "SELECT UsuarioId FROM fact.Usuario WHERE NombreUsuario = 'usr_sesion_otro';");

        var sut = new SqlSesionRepository(_db.ConnectionString);
        var ahora = DateTimeOffset.UtcNow;
        var tokenHashPropio = "4".PadLeft(64, '0');
        var tokenHashAjeno = "5".PadLeft(64, '0');
        await sut.CreateAsync(_usuarioId, tokenHashPropio, ahora.AddHours(8), "ticket-propio", CancellationToken.None);
        await sut.CreateAsync(otroUsuarioId, tokenHashAjeno, ahora.AddHours(8), "ticket-ajeno", CancellationToken.None);

        await sut.RevokeAllForUsuarioAsync(_usuarioId, MotivoRevocacion.Restablecimiento, ahora, CancellationToken.None);

        var activaAjena = await sut.FindActiveAsync(tokenHashAjeno, ahora, CancellationToken.None);
        Assert.NotNull(activaAjena);
    }

    // tasks.md 5.8/5.9 (SmartNet.Admin `sesion purgar`): the sole DELETE caller in the whole
    // permission matrix (design.md Decision 3). Anchored on CreadaEn -- rows older than the
    // retention window are removed, rows within it are left untouched, regardless of revocation
    // or expiry state.
    [Fact]
    public async Task DeleteOlderThanAsync_DeletesOnlyRowsOlderThanTheCutoff()
    {
        var sut = new SqlSesionRepository(_db.ConnectionString);
        var ahora = DateTimeOffset.UtcNow;
        var tokenHashVieja = "6".PadLeft(64, '0');
        var tokenHashNueva = "7".PadLeft(64, '0');
        await sut.CreateAsync(_usuarioId, tokenHashVieja, ahora.AddHours(8), "ticket-viejo", CancellationToken.None);
        await sut.CreateAsync(_usuarioId, tokenHashNueva, ahora.AddHours(8), "ticket-nuevo", CancellationToken.None);
        // CreadaEn defaults to SYSUTCDATETIME() at INSERT time; back-date only the "old" row
        // directly, the same way other tests here reach columns CreateAsync itself never exposes.
        await _db.ExecuteNonQueryAsync(
            $"UPDATE fact.Sesion SET CreadaEn = DATEADD(DAY, -100, SYSUTCDATETIME()) WHERE TokenHash = '{tokenHashVieja}';");

        var corte = ahora.AddDays(-90);
        var eliminadas = await sut.DeleteOlderThanAsync(corte, CancellationToken.None);

        Assert.Equal(1, eliminadas);
        var cuentaVieja = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.Sesion WHERE TokenHash = '{tokenHashVieja}';");
        var cuentaNueva = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.Sesion WHERE TokenHash = '{tokenHashNueva}';");
        Assert.Equal(0, cuentaVieja);
        Assert.Equal(1, cuentaNueva);
    }

    [Fact]
    public async Task DeleteOlderThanAsync_ReturnsZero_WhenNoRowIsOlderThanTheCutoff()
    {
        var sut = new SqlSesionRepository(_db.ConnectionString);
        var ahora = DateTimeOffset.UtcNow;
        var tokenHash = "8".PadLeft(64, '0');
        await sut.CreateAsync(_usuarioId, tokenHash, ahora.AddHours(8), "ticket-reciente", CancellationToken.None);

        var eliminadas = await sut.DeleteOlderThanAsync(ahora.AddDays(-90), CancellationToken.None);

        Assert.Equal(0, eliminadas);
        var cuenta = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.Sesion WHERE TokenHash = '{tokenHash}';");
        Assert.Equal(1, cuenta);
    }
}
