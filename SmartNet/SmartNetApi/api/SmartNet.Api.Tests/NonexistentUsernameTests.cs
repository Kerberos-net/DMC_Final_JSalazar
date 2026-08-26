using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SmartNet.Api.Tests;

/// <summary>
/// Task 4.14 -- a nonexistent username. Implementation-time call, recorded explicitly: instead of
/// a raw wall-clock comparison (flaky under CI load/machine variance), this asserts the MECHANISM
/// -- the decoy Argon2id verification path (<see cref="CountingPasswordHasher"/>) is invoked
/// EXACTLY ONCE, with the real Argon2id parameters (delegated to the genuine
/// <c>Argon2idPasswordHasher</c>, never a shortcut), for the unknown-username case. This is a
/// call-count/identity assertion on <c>IPasswordHasher</c>, not a timing measurement.
/// </summary>
public class NonexistentUsernameTests : SesionEndpointsTestBase
{
    [Fact]
    public async Task PostSesion_WithNonexistentUsername_IsIndistinguishableFromWrongPassword_AndRunsExactlyOneDecoyVerification()
    {
        var hasher = new CountingPasswordHasher();
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath, passwordHasher: hasher);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.PostAsJsonAsync(
            "/api/sesion", new LoginRequest("usuario-que-no-existe", "cualquier-clave"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        // The mechanism task 4.14 asks for, precisely: one real decoy Argon2id verification,
        // same parameters as a real hash, and nothing else.
        Assert.Equal(1, hasher.DecoyVerifyCallCount);
        Assert.Equal(0, hasher.RealVerifyCallCount);
        Assert.Equal(1, hasher.TotalVerifyCallCount);
    }

    [Fact]
    public async Task PostSesion_WithNonexistentUsername_ProducesTheSameBodyAsWrongPassword()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var unknownUserResponse = await client.PostAsJsonAsync(
            "/api/sesion", new LoginRequest("usuario-que-no-existe", "cualquier-clave"));
        var wrongPasswordResponse = await client.PostAsJsonAsync(
            "/api/sesion", new LoginRequest(NombreUsuario, "clave-incorrecta"));

        var unknownUserBody = await unknownUserResponse.Content.ReadAsStringAsync();
        var wrongPasswordBody = await wrongPasswordResponse.Content.ReadAsStringAsync();

        Assert.Equal(unknownUserResponse.StatusCode, wrongPasswordResponse.StatusCode);
        Assert.Equal(wrongPasswordBody, unknownUserBody);
    }
}
