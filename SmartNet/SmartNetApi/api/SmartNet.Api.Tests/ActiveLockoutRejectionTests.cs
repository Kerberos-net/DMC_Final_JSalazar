using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SmartNet.Api.Tests;

/// <summary>
/// Task 4.16 -- an attempt during an active lockout: rejected, and IPasswordHasher.Verify (or the
/// decoy path) is NEVER invoked -- asserted via a real call-count double, not just the response
/// shape. IntentosFallidos unchanged; response shape matches the generic failure.
/// </summary>
public class ActiveLockoutRejectionTests : SesionEndpointsTestBase
{
    [Fact]
    public async Task AttemptDuringActiveLockout_IsRejected_WithoutInvokingPasswordVerification_EvenWithTheCorrectPassword()
    {
        var hasher = new CountingPasswordHasher();
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath, passwordHasher: hasher);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Arm the lock with 5 wrong-password failures first.
        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/api/sesion", new LoginRequest(NombreUsuario, "clave-incorrecta"));
        }

        Assert.Equal(1, await GetNivelBloqueoAsync());
        var callsBeforeLockedAttempt = hasher.TotalVerifyCallCount;
        var intentosBeforeLockedAttempt = await GetIntentosFallidosAsync();

        // Even with the CORRECT password, an attempt during the lock must be rejected without
        // ever calling Verify -- this is the "rejected before hashing" success criterion.
        var response = await client.PostAsJsonAsync(
            "/api/sesion", new LoginRequest(NombreUsuario, ClavePlanaCorrecta));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(callsBeforeLockedAttempt, hasher.TotalVerifyCallCount);
        Assert.Equal(intentosBeforeLockedAttempt, await GetIntentosFallidosAsync());
    }
}
