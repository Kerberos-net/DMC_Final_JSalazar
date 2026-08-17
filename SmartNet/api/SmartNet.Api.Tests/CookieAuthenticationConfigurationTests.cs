using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartNet.Db.TestBootstrap;

namespace SmartNet.Api.Tests;

/// <summary>
/// Task 4.6/4.7 -- cookie configuration matching ADR 0007 Revisión 4 and spec.md's cookie-attribute
/// scenario EXACTLY: name <c>__Host-session</c>, <c>HttpOnly</c>, <c>SecurePolicy=Always</c>,
/// <c>SameSite=Lax</c>, <c>ExpireTimeSpan=8h</c>, <c>SlidingExpiration=true</c>.
/// </summary>
public class CookieAuthenticationConfigurationTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;
    private SmartNetApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _db = await TestDatabaseFixture.CreateAsync();
        await _db.CreateWithoutLoginUserAsync("usr_api");
        await _db.CreateWithoutLoginUserAsync("usr_worker");
        await _db.CreateExternalDboCatalogsAsync();
        await _db.SeedDboMotivoFixtureRowsAsync();
        Assert.Equal(0, _db.RunMigrations());

        _factory = new SmartNetApiFactory(_db.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public void CookieOptions_MatchAdr0007Revision4Exactly()
    {
        using var scope = _factory.Services.CreateScope();
        var monitor = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        var options = monitor.Get(CookieAuthenticationDefaults.AuthenticationScheme);

        Assert.Equal("__Host-session", options.Cookie.Name);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
        Assert.Equal(TimeSpan.FromHours(8), options.ExpireTimeSpan);
        Assert.True(options.SlidingExpiration);
    }

    [Fact]
    public void CookieOptions_UsesARealServerSideSessionStore_NotTheDefaultInMemoryTicketDataFormat()
    {
        using var scope = _factory.Services.CreateScope();
        var monitor = scope.ServiceProvider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>();
        var options = monitor.Get(CookieAuthenticationDefaults.AuthenticationScheme);

        // design.md: "ADR 0007's 'el cierre de sesión invalida la sesión del lado del servidor' is
        // structural" -- only true if a real ITicketStore is wired, not the default ticket-in-cookie
        // behavior (SessionStore is null by default).
        Assert.NotNull(options.SessionStore);
    }
}
