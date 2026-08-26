using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SmartNet.Api.Tests;

/// <summary>
/// Task 4.20/4.21 -- DELETE /api/sesion revokes the fact.Sesion row (MotivoRevocacion =
/// CIERRE_SESION), and the same, now-stale cookie stops authenticating.
/// Task 4.22/4.23 -- GET /api/sesion: 200 { nombreUsuario } when authenticated, 401 otherwise.
/// </summary>
public class LogoutAndProbeTests : SesionEndpointsTestBase
{
    [Fact]
    public async Task GetSesion_WithoutACookie_Returns401()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/sesion");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSesion_WithAValidCookie_Returns200WithNombreUsuario()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var loginResponse = await client.PostAsJsonAsync(
            "/api/sesion", new LoginRequest(NombreUsuario, ClavePlanaCorrecta));
        var cookie = ExtractSessionCookie(loginResponse)!;
        client.DefaultRequestHeaders.Add("Cookie", cookie);

        var response = await client.GetAsync("/api/sesion");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ProbeResponse>();
        Assert.Equal(NombreUsuario, body!.NombreUsuario);
    }

    [Fact]
    public async Task DeleteSesion_RevokesTheRow_AndTheStaleCookieNoLongerAuthenticates()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var loginResponse = await client.PostAsJsonAsync(
            "/api/sesion", new LoginRequest(NombreUsuario, ClavePlanaCorrecta));
        var cookie = ExtractSessionCookie(loginResponse)!;
        client.DefaultRequestHeaders.Add("Cookie", cookie);

        var logoutResponse = await client.DeleteAsync("/api/sesion");
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var motivo = await Db.ExecuteScalarAsync<string>(
            $"SELECT MotivoRevocacion FROM fact.Sesion WHERE UsuarioId = {UsuarioId};");
        Assert.Equal("CIERRE_SESION", motivo);

        // Same, now-stale cookie must no longer authenticate.
        var probeResponse = await client.GetAsync("/api/sesion");
        Assert.Equal(HttpStatusCode.Unauthorized, probeResponse.StatusCode);
    }

    private sealed record ProbeResponse(string NombreUsuario);
}
