using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SmartNet.Api.Tests;

/// <summary>
/// Task 4.8 GATE CHECK: task 0.2 (key-ring path resolution, <see cref="ApiKeyRingOptions"/>) is
/// CLOSED per tasks.md line 64-71 and design.md Decision 4 -- confirmed before this test was
/// written, cited rather than redone.
///
/// Task 4.9 -- the exact failure mode design.md flagged: with the DEFAULT ephemeral/profile-local
/// Data Protection key ring, a host restart invalidates every live cookie, silently defeating the
/// reason `fact.Sesion` (a TABLE) was chosen over an in-memory session store. A login issued
/// against ONE <see cref="WebApplicationFactory{Program}"/> instance must still authenticate
/// against a SECOND, freshly constructed instance pointed at the SAME key-ring path -- the closest
/// in-process proxy for "the process restarted" available to an integration test, since the two
/// factories build two independent `IServiceProvider`/`IDataProtectionProvider` trees exactly as
/// two separate `dotnet SmartNet.Api.dll` process launches would.
/// </summary>
public class KeyRingPersistenceTests : SesionEndpointsTestBase
{
    [Fact]
    public async Task CookieIssuedByOneHostInstance_StillAuthenticates_AgainstASecondFreshInstance_SameKeyRingPath()
    {
        string sessionCookie;
        await using (var firstHost = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath))
        {
            using var firstClient = firstHost.CreateClient(
                new WebApplicationFactoryClientOptions { HandleCookies = false });

            var loginResponse = await firstClient.PostAsJsonAsync(
                "/api/sesion", new LoginRequest(NombreUsuario, ClavePlanaCorrecta));
            Assert.Equal(HttpStatusCode.NoContent, loginResponse.StatusCode);

            sessionCookie = ExtractSessionCookie(loginResponse)
                ?? throw new InvalidOperationException("Login did not set __Host-session.");
        }
        // firstHost is fully disposed here -- its IServiceProvider, and everything built from it
        // (including the IDataProtectionProvider instance held in memory), is gone. Only what
        // PersistKeysToFileSystem wrote to KeyRingPath on disk survives, exactly like a real
        // process exit.

        await using var secondHost = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var secondClient = secondHost.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = false });
        secondClient.DefaultRequestHeaders.Add("Cookie", sessionCookie);

        var probeResponse = await secondClient.GetAsync("/api/sesion");

        Assert.Equal(HttpStatusCode.OK, probeResponse.StatusCode);
        var body = await probeResponse.Content.ReadFromJsonAsync<ProbeResponse>();
        Assert.Equal(NombreUsuario, body!.NombreUsuario);
    }

    private sealed record ProbeResponse(string NombreUsuario);
}
