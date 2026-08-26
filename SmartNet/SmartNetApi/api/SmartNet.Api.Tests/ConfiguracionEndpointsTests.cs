using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartNet.Db.TestBootstrap;

namespace SmartNet.Api.Tests;

/// <summary>
/// tasks.md 5.4/5.5 — <c>ConfiguracionEndpoints</c> (spec.md configuracion-api-spa, design D6)
/// against the real database via <see cref="SmartNetApiFactory"/>: GET by section, PUT valid,
/// PUT invalid rejected (prior value retained), PUT unauthenticated rejected, unknown key -&gt; 404.
/// </summary>
public sealed class ConfiguracionEndpointsTests : SesionEndpointsTestBase
{
    private async Task<HttpClient> AuthenticatedClientAsync(SmartNetApiFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var loginResponse = await client.PostAsJsonAsync("/api/sesion", new LoginRequest(NombreUsuario, ClavePlanaCorrecta));
        var cookie = ExtractSessionCookie(loginResponse)!;
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    // --- GET ---

    [Fact]
    public async Task Get_WithASeccion_ReturnsOnlyThatSectionsEntries()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/configuracion?seccion=TELEGRAM");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await response.Content.ReadFromJsonAsync<ConfiguracionEntradaRespuesta[]>();
        Assert.NotEmpty(cuerpo!);
        Assert.All(cuerpo!, e => Assert.Equal("TELEGRAM", e.Seccion));
    }

    [Fact]
    public async Task Get_WithoutASeccion_ReturnsEntriesFromMultipleSections()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/configuracion");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await response.Content.ReadFromJsonAsync<ConfiguracionEntradaRespuesta[]>();
        Assert.Contains(cuerpo!, e => e.Seccion == "TELEGRAM");
        Assert.Contains(cuerpo!, e => e.Seccion == "CORREO");
    }

    [Fact]
    public async Task Get_WithoutACookie_Returns401()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/configuracion");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- PUT ---

    [Fact]
    public async Task Put_WithAValidValue_UpdatesTheStoredValue()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PutAsJsonAsync(
            "/api/configuracion/TELEGRAM/DESTINO_CHAT_ID", new PutConfiguracionRequest("-100200300"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var getResponse = await client.GetAsync("/api/configuracion?seccion=TELEGRAM");
        var cuerpo = await getResponse.Content.ReadFromJsonAsync<ConfiguracionEntradaRespuesta[]>();
        var entrada = Assert.Single(cuerpo!, e => e.Clave == "DESTINO_CHAT_ID");
        Assert.Equal("-100200300", entrada.Valor);
    }

    [Fact]
    public async Task Put_WithAnInvalidValueForTheDeclaredTipo_RejectsIt_AndRetainsThePriorValue()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);
        // INGESTA.FRECUENCIA_SONDEO_MINUTOS is Tipo=ENTERO (009_datos_base.sql).
        await client.PutAsJsonAsync("/api/configuracion/INGESTA/FRECUENCIA_SONDEO_MINUTOS", new PutConfiguracionRequest("5"));

        var response = await client.PutAsJsonAsync(
            "/api/configuracion/INGESTA/FRECUENCIA_SONDEO_MINUTOS", new PutConfiguracionRequest("no-es-numero"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var getResponse = await client.GetAsync("/api/configuracion?seccion=INGESTA");
        var cuerpo = await getResponse.Content.ReadFromJsonAsync<ConfiguracionEntradaRespuesta[]>();
        var entrada = Assert.Single(cuerpo!, e => e.Clave == "FRECUENCIA_SONDEO_MINUTOS");
        Assert.Equal("5", entrada.Valor);
    }

    [Fact]
    public async Task Put_WithAnUnknownKey_Returns404_AndInsertsNothing()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PutAsJsonAsync(
            "/api/configuracion/NO_EXISTE/TAMPOCO", new PutConfiguracionRequest("x"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var cantidad = await Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fact.Configuracion WHERE Seccion = 'NO_EXISTE';");
        Assert.Equal(0, cantidad);
    }

    [Fact]
    public async Task Put_WithoutACookie_Returns401_AndDoesNotChangeTheValue()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.PutAsJsonAsync(
            "/api/configuracion/TELEGRAM/DESTINO_CHAT_ID", new PutConfiguracionRequest("-1"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var valor = await Db.ExecuteScalarAsync<string?>(
            "SELECT Valor FROM fact.Configuracion WHERE Seccion = 'TELEGRAM' AND Clave = 'DESTINO_CHAT_ID';");
        Assert.Null(valor);
    }
}

internal sealed record PutConfiguracionRequest(string? Valor);

internal sealed record ConfiguracionEntradaRespuesta(
    string Seccion, string Clave, string Tipo, string? Valor, string? ValorPorDefecto, string Descripcion);
