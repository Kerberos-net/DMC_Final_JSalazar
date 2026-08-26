using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SmartNet.Api.Tests;

/// <summary>
/// Task 4.15 -- the 5th consecutive failure: BloqueadoHasta 15 minutes out, NivelBloqueo -> 1,
/// IntentosFallidos reset to 0 (arming, not expiry, is the reset event -- ADR 0007 Revisión 4).
/// </summary>
public class FifthFailureLockoutTests : SesionEndpointsTestBase
{
    [Fact]
    public async Task FifthConsecutiveFailure_ArmsA15MinuteLock_AndSetsNivelBloqueoTo1()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/api/sesion", new LoginRequest(NombreUsuario, "clave-incorrecta"));
        }

        Assert.Equal(0, await GetIntentosFallidosAsync());
        Assert.Equal(1, await GetNivelBloqueoAsync());

        var bloqueadoHasta = await GetBloqueadoHastaAsync();
        Assert.NotNull(bloqueadoHasta);
        var minutosRestantes = (bloqueadoHasta!.Value - DateTime.UtcNow).TotalMinutes;
        Assert.InRange(minutosRestantes, 14.5, 15.5);
    }
}
