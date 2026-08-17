using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using SmartNet.Db.TestBootstrap;

namespace SmartNet.Auth.Infrastructure.Tests;

/// <summary>
/// Task 3.11/3.12 -- <see cref="SqlSesionTicketStore"/>, the thin <c>ITicketStore</c> adapter over
/// <see cref="SqlSesionRepository"/> (design.md Decision 4/5). The load-bearing property this
/// suite proves: the persisted payload that ends up (Data-Protection-wrapped) in the cookie is the
/// raw 256-bit token, NEVER the deserialized claims principal.
/// </summary>
public sealed class SqlSesionTicketStoreTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;
    private long _usuarioId;

    public async Task InitializeAsync()
    {
        _db = await MigratedDatabase();
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO fact.Usuario (NombreUsuario, ClaveHash) VALUES ('usr_ticket_owner', 'hash-de-prueba');");
        _usuarioId = await _db.ExecuteScalarAsync<long>(
            "SELECT UsuarioId FROM fact.Usuario WHERE NombreUsuario = 'usr_ticket_owner';");
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

    private AuthenticationTicket BuildTicket(string nombreUsuario, DateTimeOffset expiresUtc)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, nombreUsuario), new Claim("usuarioId", _usuarioId.ToString())],
            CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        var properties = new AuthenticationProperties { ExpiresUtc = expiresUtc, IssuedUtc = DateTimeOffset.UtcNow };
        return new AuthenticationTicket(principal, properties, CookieAuthenticationDefaults.AuthenticationScheme);
    }

    [Fact]
    public async Task StoreAsync_ReturnsA43CharacterBase64UrlToken_AsTheKey()
    {
        var sut = new SqlSesionTicketStore(_db.ConnectionString);
        var ticket = BuildTicket("usr_ticket_owner", DateTimeOffset.UtcNow.AddHours(8));

        var key = await sut.StoreAsync(ticket);

        Assert.Equal(43, key.Length);
        Assert.DoesNotContain('+', key);
        Assert.DoesNotContain('/', key);
        Assert.DoesNotContain('=', key);
    }

    // THE load-bearing assertion: the row's TokenHash is SHA-256(key), never a hash of the
    // serialized claims -- proving the key IS the raw 256-bit token, and that no serialized claims
    // payload is what identifies the row.
    [Fact]
    public async Task StoreAsync_PersistsSha256OfTheKey_AsTheTokenHash_NeverTheClaims()
    {
        var sut = new SqlSesionTicketStore(_db.ConnectionString);
        var ticket = BuildTicket("usr_ticket_owner", DateTimeOffset.UtcNow.AddHours(8));

        var key = await sut.StoreAsync(ticket);

        var expectedTokenHash = Convert.ToHexStringLower(SHA256.HashData(Base64UrlDecode(key)));
        var actualTokenHash = await _db.ExecuteScalarAsync<string>(
            $"SELECT TokenHash FROM fact.Sesion WHERE UsuarioId = {_usuarioId};");

        Assert.Equal(expectedTokenHash, actualTokenHash);
    }

    [Fact]
    public async Task RetrieveAsync_ReturnsAnEquivalentTicket_ForAStoredKey()
    {
        var sut = new SqlSesionTicketStore(_db.ConnectionString);
        var ticket = BuildTicket("usr_ticket_owner", DateTimeOffset.UtcNow.AddHours(8));
        var key = await sut.StoreAsync(ticket);

        var retrieved = await sut.RetrieveAsync(key);

        Assert.NotNull(retrieved);
        Assert.Equal("usr_ticket_owner", retrieved!.Principal.Identity!.Name);
    }

    [Fact]
    public async Task RetrieveAsync_ReturnsNull_ForAnUnknownKey()
    {
        var sut = new SqlSesionTicketStore(_db.ConnectionString);

        var retrieved = await sut.RetrieveAsync("un-token-que-nunca-existio-en-la-base00000");

        Assert.Null(retrieved);
    }

    [Fact]
    public async Task RetrieveAsync_ReturnsNull_AfterExpiry()
    {
        var sut = new SqlSesionTicketStore(_db.ConnectionString);
        var ticket = BuildTicket("usr_ticket_owner", DateTimeOffset.UtcNow.AddSeconds(-1));
        var key = await sut.StoreAsync(ticket);

        var retrieved = await sut.RetrieveAsync(key);

        Assert.Null(retrieved);
    }

    [Fact]
    public async Task RenewAsync_ExtendsExpiraEn_ForTheSameKey()
    {
        var sut = new SqlSesionTicketStore(_db.ConnectionString);
        var ticket = BuildTicket("usr_ticket_owner", DateTimeOffset.UtcNow.AddHours(4));
        var key = await sut.StoreAsync(ticket);

        var renewedTicket = BuildTicket("usr_ticket_owner", DateTimeOffset.UtcNow.AddHours(8));
        await sut.RenewAsync(key, renewedTicket);

        var retrieved = await sut.RetrieveAsync(key);
        Assert.NotNull(retrieved);
        Assert.True(retrieved!.Properties.ExpiresUtc > DateTimeOffset.UtcNow.AddHours(7));
    }

    [Fact]
    public async Task RemoveAsync_RevokesTheSession_AndRetrieveAsyncThenReturnsNull()
    {
        var sut = new SqlSesionTicketStore(_db.ConnectionString);
        var ticket = BuildTicket("usr_ticket_owner", DateTimeOffset.UtcNow.AddHours(8));
        var key = await sut.StoreAsync(ticket);

        await sut.RemoveAsync(key);

        var retrieved = await sut.RetrieveAsync(key);
        Assert.Null(retrieved);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}
