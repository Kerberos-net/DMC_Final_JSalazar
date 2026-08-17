using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SmartNet.Api.Tests;

/// <summary>
/// Task 4.11/4.12 -- successful login: sets <c>__Host-session</c> with every mandated attribute,
/// creates a <c>fact.Sesion</c> row, resets <c>IntentosFallidos</c> to <c>0</c>.
/// </summary>
public class LoginSuccessTests : SesionEndpointsTestBase
{
    [Fact]
    public async Task PostSesion_WithCorrectCredentials_SetsCookie_CreatesSesionRow_AndResetsIntentosFallidos()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.PostAsJsonAsync(
            "/api/sesion", new LoginRequest(NombreUsuario, ClavePlanaCorrecta));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var setCookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join(" | ", values)
            : null;
        Assert.NotNull(setCookie);
        Assert.Contains("__Host-session=", setCookie, StringComparison.Ordinal);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);

        var sesionRowCount = await Db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.Sesion WHERE UsuarioId = {UsuarioId};");
        Assert.Equal(1, sesionRowCount);

        Assert.Equal(0, await GetIntentosFallidosAsync());
    }
}
