using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SmartNet.Api.Tests;

/// <summary>
/// Task 4.26/4.27 -- unknown user, wrong password, and locked account all produce byte-for-byte
/// identical 401 application/problem+json documents (design.md Decision 6: "Every login failure ...
/// returns the identical 401 problem document").
/// </summary>
public class ProblemJsonByteIdenticalTests : SesionEndpointsTestBase
{
    [Fact]
    public async Task UnknownUser_WrongPassword_AndLockedAccount_ProduceByteForByteIdentical401Bodies()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var unknownUserResponse = await client.PostAsJsonAsync(
            "/api/sesion", new LoginRequest("usuario-inexistente", "cualquier-clave"));
        var unknownUserBytes = await unknownUserResponse.Content.ReadAsByteArrayAsync();

        var wrongPasswordResponse = await client.PostAsJsonAsync(
            "/api/sesion", new LoginRequest(NombreUsuario, "clave-incorrecta-1"));
        var wrongPasswordBytes = await wrongPasswordResponse.Content.ReadAsByteArrayAsync();

        // Arm the lock (5 consecutive failures), then capture the locked-account body.
        for (var i = 0; i < 4; i++)
        {
            await client.PostAsJsonAsync("/api/sesion", new LoginRequest(NombreUsuario, "clave-incorrecta-2"));
        }
        var lockedAccountResponse = await client.PostAsJsonAsync(
            "/api/sesion", new LoginRequest(NombreUsuario, "clave-incorrecta-3"));
        Assert.Equal(1, await GetNivelBloqueoAsync()); // confirms the lock actually armed on this call
        var lockedAccountBytes = await lockedAccountResponse.Content.ReadAsByteArrayAsync();

        Assert.Equal(unknownUserResponse.StatusCode, wrongPasswordResponse.StatusCode);
        Assert.Equal(unknownUserResponse.StatusCode, lockedAccountResponse.StatusCode);
        Assert.Equal("application/problem+json", unknownUserResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("application/problem+json", wrongPasswordResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("application/problem+json", lockedAccountResponse.Content.Headers.ContentType?.MediaType);

        Assert.Equal(unknownUserBytes, wrongPasswordBytes);
        Assert.Equal(unknownUserBytes, lockedAccountBytes);
    }
}
