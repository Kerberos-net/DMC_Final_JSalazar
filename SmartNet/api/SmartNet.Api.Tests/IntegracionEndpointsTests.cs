using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartNet.Db.TestBootstrap;

namespace SmartNet.Api.Tests;

/// <summary>
/// tasks.md Phase 4 (PR 4), task 4.2 — <c>IntegracionEndpoints</c> (spec.md
/// <c>api-incidencias-integraciones</c>, design D7) against the real database via
/// <see cref="SmartNetApiFactory"/>: reprocesar/sincronizar/reconectar enqueue-only (no
/// <c>AuditoriaCorreccion</c>, no Python call), unknown integration name -&gt; 404, and
/// <c>GET /api/integraciones/estado</c> deriving the Conectado/Con error "pill".
/// </summary>
public sealed class IntegracionEndpointsTests : SesionEndpointsTestBase
{
    private async Task<HttpClient> AuthenticatedClientAsync(SmartNetApiFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var loginResponse = await client.PostAsJsonAsync("/api/sesion", new LoginRequest(NombreUsuario, ClavePlanaCorrecta));
        var cookie = ExtractSessionCookie(loginResponse)!;
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    private async Task<int> ContarAuditoriaAsync() =>
        await Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fact.AuditoriaCorreccion;");

    // --- reprocesar ---

    [Fact]
    public async Task Reprocesar_EnqueuesACommandQueueRow_AndWritesNoAudit()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsync("/api/incidencias/42/reprocesar", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var cantidad = await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.CommandQueue WHERE Tipo = 'REPROCESAR_DOCUMENTO' AND Referencia = 42;");
        Assert.Equal(1, cantidad);
        Assert.Equal(0, await ContarAuditoriaAsync());
    }

    // --- sincronizar ---

    [Fact]
    public async Task Sincronizar_Gmail_EnqueuesACommandQueueRow()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsync("/api/integraciones/gmail/sincronizar", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var cantidad = await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.CommandQueue WHERE Tipo = 'SINCRONIZAR_GMAIL';");
        Assert.Equal(1, cantidad);
        Assert.Equal(0, await ContarAuditoriaAsync());
    }

    [Fact]
    public async Task Sincronizar_Sbs_EnqueuesACommandQueueRow()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsync("/api/integraciones/sbs/sincronizar", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var cantidad = await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.CommandQueue WHERE Tipo = 'SINCRONIZAR_SBS';");
        Assert.Equal(1, cantidad);
    }

    [Fact]
    public async Task Sincronizar_AnUnknownIntegrationName_Returns404_AndEnqueuesNothing()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsync("/api/integraciones/telegram/sincronizar", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var cantidad = await Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fact.CommandQueue;");
        Assert.Equal(0, cantidad);
    }

    // --- reconectar ---

    [Fact]
    public async Task Reconectar_Google_EnqueuesACommandQueueRow_AndWritesNoAudit()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsync("/api/integraciones/google/reconectar", content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var cantidad = await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.CommandQueue WHERE Tipo = 'RECONECTAR_GOOGLE';");
        Assert.Equal(1, cantidad);
        Assert.Equal(0, await ContarAuditoriaAsync());
    }

    // --- GET estado ---

    [Fact]
    public async Task GetEstado_WithARecentSuccess_DerivesConectado()
    {
        await Db.ExecuteNonQueryAsync(
            "UPDATE fact.EstadoIntegracion SET UltimoIntento = SYSUTCDATETIME(), UltimoExito = SYSUTCDATETIME(), " +
            "FallosSeguidos = 0, UltimoError = NULL WHERE Nombre = 'SBS';");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/integraciones/estado");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await response.Content.ReadFromJsonAsync<IntegracionEstadoRespuesta[]>();
        var sbs = Assert.Single(cuerpo!, e => e.Nombre == "SBS");
        Assert.Equal("Conectado", sbs.Estado);
    }

    [Fact]
    public async Task GetEstado_WithConsecutiveFailures_DerivesConError()
    {
        await Db.ExecuteNonQueryAsync(
            "UPDATE fact.EstadoIntegracion SET UltimoIntento = SYSUTCDATETIME(), FallosSeguidos = 3, " +
            "UltimoError = 'timeout' WHERE Nombre = 'GMAIL';");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/integraciones/estado");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await response.Content.ReadFromJsonAsync<IntegracionEstadoRespuesta[]>();
        var gmail = Assert.Single(cuerpo!, e => e.Nombre == "GMAIL");
        Assert.Equal("Con error", gmail.Estado);
    }

    // --- 401 guard ---

    [Fact]
    public async Task GetEstado_WithoutACookie_Returns401()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/integraciones/estado");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
