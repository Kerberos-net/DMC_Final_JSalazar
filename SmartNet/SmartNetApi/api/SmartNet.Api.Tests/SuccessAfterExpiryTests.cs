using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Time.Testing;

namespace SmartNet.Api.Tests;

/// <summary>
/// Task 4.18 -- successful login after BloqueadoHasta has passed: authenticates, resets
/// IntentosFallidos and NivelBloqueo to 0.
/// </summary>
public class SuccessAfterExpiryTests : SesionEndpointsTestBase
{
    [Fact]
    public async Task SuccessfulLogin_AfterLockExpiry_Authenticates_AndResetsIntentosFallidosAndNivelBloqueoToZero()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath, timeProvider);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/api/sesion", new LoginRequest(NombreUsuario, "clave-incorrecta"));
        }
        Assert.Equal(1, await GetNivelBloqueoAsync());

        timeProvider.Advance(TimeSpan.FromMinutes(16));

        var response = await client.PostAsJsonAsync(
            "/api/sesion", new LoginRequest(NombreUsuario, ClavePlanaCorrecta));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(0, await GetIntentosFallidosAsync());
        Assert.Equal(0, await GetNivelBloqueoAsync());
        Assert.Null(await GetBloqueadoHastaAsync());
    }
}
