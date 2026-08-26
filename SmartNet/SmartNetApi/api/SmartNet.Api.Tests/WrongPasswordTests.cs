using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SmartNet.Api.Tests;

/// <summary>Task 4.13 -- wrong password on an unlocked account: IntentosFallidos +1, generic failure.</summary>
public class WrongPasswordTests : SesionEndpointsTestBase
{
    [Fact]
    public async Task PostSesion_WithWrongPassword_OnUnlockedAccount_IncrementsIntentosFallidosByExactlyOne_WithGenericFailure()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.PostAsJsonAsync(
            "/api/sesion", new LoginRequest(NombreUsuario, "clave-incorrecta"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(1, await GetIntentosFallidosAsync());
        Assert.Equal(0, await GetNivelBloqueoAsync());
        Assert.Null(await GetBloqueadoHastaAsync());
    }
}
