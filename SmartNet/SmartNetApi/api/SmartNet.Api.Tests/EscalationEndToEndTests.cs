using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Time.Testing;

namespace SmartNet.Api.Tests;

/// <summary>
/// Task 4.17 -- escalation end-to-end, driven through the REAL API + DB (not a re-test of Core's
/// in-memory logic): lock A (15 min) -> margin (no re-lock) -> lock B (30 min) -> cap holding at a
/// later lock, confirming the three lockout columns round-trip through the real
/// SaveCredentialStateAsync call path. A FakeTimeProvider substituted into DI lets the 15/30-minute
/// windows advance without real waiting.
/// </summary>
public class EscalationEndToEndTests : SesionEndpointsTestBase
{
    [Fact]
    public async Task EscalationSequence_LockA15Min_Margin_LockB30Min_ThroughTheRealApiAndDb()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath, timeProvider);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        async Task FallarAsync() =>
            await client.PostAsJsonAsync("/api/sesion", new LoginRequest(NombreUsuario, "clave-incorrecta"));

        // Failures 1-5: lock A arms at 15 minutes, NivelBloqueo -> 1.
        for (var i = 0; i < 5; i++)
        {
            await FallarAsync();
        }
        Assert.Equal(0, await GetIntentosFallidosAsync());
        Assert.Equal(1, await GetNivelBloqueoAsync());
        var bloqueadoHastaA = await GetBloqueadoHastaAsync();
        Assert.NotNull(bloqueadoHastaA);

        // Advance past lock A's expiry.
        timeProvider.Advance(TimeSpan.FromMinutes(16));

        // Failure 6 -- the margin: does not re-lock, NivelBloqueo stays 1.
        await FallarAsync();
        Assert.Equal(1, await GetIntentosFallidosAsync());
        Assert.Equal(1, await GetNivelBloqueoAsync());

        // Failures 7-9: still inside the margin.
        await FallarAsync();
        await FallarAsync();
        await FallarAsync();
        Assert.Equal(4, await GetIntentosFallidosAsync());
        Assert.Equal(1, await GetNivelBloqueoAsync());

        // Failure 10: exhausts the margin -- lock B arms at 30 minutes, NivelBloqueo -> 2.
        await FallarAsync();
        Assert.Equal(0, await GetIntentosFallidosAsync());
        Assert.Equal(2, await GetNivelBloqueoAsync());
        var bloqueadoHastaB = await GetBloqueadoHastaAsync();
        Assert.NotNull(bloqueadoHastaB);
        Assert.True(bloqueadoHastaB > bloqueadoHastaA, "Lock B must be longer than lock A.");

        // Advance past lock B and its margin, drive to lock C (60 min), then to the cap (120 min).
        timeProvider.Advance(TimeSpan.FromMinutes(31));
        for (var i = 0; i < 5; i++)
        {
            await FallarAsync();
        }
        Assert.Equal(3, await GetNivelBloqueoAsync());
        var bloqueadoHastaC = await GetBloqueadoHastaAsync();
        Assert.NotNull(bloqueadoHastaC);

        timeProvider.Advance(TimeSpan.FromMinutes(61));
        for (var i = 0; i < 5; i++)
        {
            await FallarAsync();
        }
        // Cap holds: NivelBloqueo stays at 3, never increases further.
        Assert.Equal(3, await GetNivelBloqueoAsync());
        var bloqueadoHastaD = await GetBloqueadoHastaAsync();
        Assert.NotNull(bloqueadoHastaD);
        var minutosD = (bloqueadoHastaD!.Value - timeProvider.GetUtcNow().UtcDateTime).TotalMinutes;
        Assert.InRange(minutosD, 119, 121);
    }
}
