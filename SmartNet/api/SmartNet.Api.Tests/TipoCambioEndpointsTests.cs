using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartNet.Db.TestBootstrap;

namespace SmartNet.Api.Tests;

/// <summary>
/// tasks.md Phase 4 (PR 4), task 4.1 — <c>TipoCambioEndpoints</c> (spec.md
/// <c>tipos-de-cambio</c>: "POST /api/tipos-cambio exposes carga-manual over HTTP with
/// problem+json errors") against the real database via <see cref="SmartNetApiFactory"/>.
/// </summary>
public sealed class TipoCambioEndpointsTests : SesionEndpointsTestBase
{
    private async Task<HttpClient> AuthenticatedClientAsync(SmartNetApiFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var loginResponse = await client.PostAsJsonAsync("/api/sesion", new LoginRequest(NombreUsuario, ClavePlanaCorrecta));
        var cookie = ExtractSessionCookie(loginResponse)!;
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    [Fact]
    public async Task Post_ForAnUncoveredDate_Returns201_AndInsertsAManualRow()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsJsonAsync("/api/tipos-cambio", new TipoCambioManualRequest(new DateOnly(2026, 8, 15), 3.85m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var cantidad = await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.TipoCambio WHERE Fecha = '2026-08-15' AND Origen = 'MANUAL';");
        Assert.Equal(1, cantidad);
    }

    [Fact]
    public async Task Post_ForADateThatAlreadyHasAManualRow_Returns409_AndDoesNotOverwrite()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);
        await client.PostAsJsonAsync("/api/tipos-cambio", new TipoCambioManualRequest(new DateOnly(2026, 8, 16), 3.85m));

        var response = await client.PostAsJsonAsync("/api/tipos-cambio", new TipoCambioManualRequest(new DateOnly(2026, 8, 16), 3.90m));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var tasa = await Db.ExecuteScalarAsync<decimal>(
            "SELECT Venta FROM fact.TipoCambio WHERE Fecha = '2026-08-16' AND Origen = 'MANUAL';");
        Assert.Equal(3.85m, tasa);
    }

    [Fact]
    public async Task Post_WithAMissingTasa_Returns400_AndInsertsNoRow()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsJsonAsync("/api/tipos-cambio", new TipoCambioManualRequest(new DateOnly(2026, 8, 17), null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var cantidad = await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.TipoCambio WHERE Fecha = '2026-08-17';");
        Assert.Equal(0, cantidad);
    }

    [Fact]
    public async Task Post_WithANonPositiveTasa_Returns400_AndInsertsNoRow()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsJsonAsync("/api/tipos-cambio", new TipoCambioManualRequest(new DateOnly(2026, 8, 18), 0m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var cantidad = await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.TipoCambio WHERE Fecha = '2026-08-18';");
        Assert.Equal(0, cantidad);
    }

    [Fact]
    public async Task Post_WhenASbsRowAlreadyExistsForTheDate_ManualLoadStillSucceedsIndependently()
    {
        await Db.ExecuteNonQueryAsync(
            """
            INSERT INTO fact.TipoCambio (Fecha, Origen, Compra, Venta, FechaConsulta)
            VALUES ('2026-08-19', 'SBS', 3.80, 3.82, SYSUTCDATETIME());
            """);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsJsonAsync("/api/tipos-cambio", new TipoCambioManualRequest(new DateOnly(2026, 8, 19), 3.85m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var cantidad = await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.TipoCambio WHERE Fecha = '2026-08-19';");
        Assert.Equal(2, cantidad);
    }

    [Fact]
    public async Task Post_WithoutACookie_Returns401()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.PostAsJsonAsync("/api/tipos-cambio", new TipoCambioManualRequest(new DateOnly(2026, 8, 20), 3.85m));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
