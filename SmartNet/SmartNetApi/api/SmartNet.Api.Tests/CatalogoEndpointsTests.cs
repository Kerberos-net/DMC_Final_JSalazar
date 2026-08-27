using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SmartNet.Api.Tests;

/// <summary>
/// BACKLOG #18 PR8 — <c>CatalogoEndpoints</c> (api-catalogos-proveedores spec): authenticated,
/// read-only <c>GET /api/catalogos/proveedores?q=&amp;pagina=</c> over <c>dbo.Proveedor</c> for the
/// SPA proveedor picker. Real database via <see cref="SmartNetApiFactory"/>.
/// </summary>
public sealed class CatalogoEndpointsTests : SesionEndpointsTestBase
{
    private async Task<HttpClient> AuthenticatedClientAsync(SmartNetApiFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var loginResponse = await client.PostAsJsonAsync("/api/sesion", new LoginRequest(NombreUsuario, ClavePlanaCorrecta));
        var cookie = ExtractSessionCookie(loginResponse)!;
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    private async Task SeedProveedorAsync(string codpro, string nombre, string? ruc = null) =>
        await Db.ExecuteNonQueryAsync(
            $"INSERT INTO dbo.Proveedor (codpro, proveedor, coddocide, rucpro) " +
            $"VALUES ('{codpro}', N'{nombre}', NULL, {(ruc is null ? "NULL" : $"'{ruc}'")});");

    private static async Task<JsonElement> LeerCuerpoAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());

    [Fact]
    public async Task Get_MatchesByNameFragment_OrderedByNombre_WithCodigoNombreRuc()
    {
        await SeedProveedorAsync("P00A01", "ACME PERU SAC", "20100000001");
        await SeedProveedorAsync("P00A02", "ACME ANDINA EIRL", "20100000002");
        await SeedProveedorAsync("P00A03", "OTRO PROVEEDOR");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/catalogos/proveedores?q=ACME");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await LeerCuerpoAsync(response);
        var resultados = cuerpo.GetProperty("resultados");
        Assert.Equal(2, resultados.GetArrayLength());
        Assert.Equal("ACME ANDINA EIRL", resultados[0].GetProperty("nombre").GetString());
        Assert.Equal("P00A02", resultados[0].GetProperty("codigo").GetString());
        Assert.Equal("20100000002", resultados[0].GetProperty("ruc").GetString());
        Assert.False(cuerpo.GetProperty("hayMas").GetBoolean());
    }

    [Fact]
    public async Task Get_MatchesByRuc()
    {
        await SeedProveedorAsync("P00B01", "COMERCIAL DELTA", "20555555555");
        await SeedProveedorAsync("P00B02", "COMERCIAL GAMMA", "20111111111");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/catalogos/proveedores?q=20555555555");

        var cuerpo = await LeerCuerpoAsync(response);
        var resultados = cuerpo.GetProperty("resultados");
        Assert.Equal(1, resultados.GetArrayLength());
        Assert.Equal("P00B01", resultados[0].GetProperty("codigo").GetString());
    }

    [Fact]
    public async Task Get_PagesResults_SecondPage_AndPastEnd()
    {
        for (var i = 0; i < 23; i++)
        {
            await SeedProveedorAsync($"P0C{i:D3}", $"PAGINADO {i:D3}");
        }
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var primera = await LeerCuerpoAsync(await client.GetAsync("/api/catalogos/proveedores?q=PAGINADO&pagina=1"));
        Assert.Equal(20, primera.GetProperty("resultados").GetArrayLength());
        Assert.True(primera.GetProperty("hayMas").GetBoolean());

        var segunda = await LeerCuerpoAsync(await client.GetAsync("/api/catalogos/proveedores?q=PAGINADO&pagina=2"));
        Assert.Equal(3, segunda.GetProperty("resultados").GetArrayLength());
        Assert.False(segunda.GetProperty("hayMas").GetBoolean());

        var pasada = await LeerCuerpoAsync(await client.GetAsync("/api/catalogos/proveedores?q=PAGINADO&pagina=5"));
        Assert.Equal(0, pasada.GetProperty("resultados").GetArrayLength());
        Assert.False(pasada.GetProperty("hayMas").GetBoolean());
    }

    [Theory]
    [InlineData("/api/catalogos/proveedores")]
    [InlineData("/api/catalogos/proveedores?q=")]
    [InlineData("/api/catalogos/proveedores?q=a")]
    public async Task Get_MissingOrShortQuery_Returns200_Empty(string url)
    {
        await SeedProveedorAsync("P00D01", "ALGUN PROVEEDOR");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await LeerCuerpoAsync(response);
        Assert.Equal(0, cuerpo.GetProperty("resultados").GetArrayLength());
        Assert.False(cuerpo.GetProperty("hayMas").GetBoolean());
    }

    [Fact]
    public async Task Get_NoMatch_Returns200_Empty()
    {
        await SeedProveedorAsync("P00E01", "PROVEEDOR REAL");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var cuerpo = await LeerCuerpoAsync(await client.GetAsync("/api/catalogos/proveedores?q=ZZZNOEXISTE"));

        Assert.Equal(0, cuerpo.GetProperty("resultados").GetArrayLength());
        Assert.False(cuerpo.GetProperty("hayMas").GetBoolean());
    }

    [Fact]
    public async Task Get_ExcludesP00000_EvenWhenItMatchesTextually()
    {
        await SeedProveedorAsync("P00000", "VARIOS");
        await SeedProveedorAsync("P00F01", "VARIOS HERMANOS SAC", "20222222222");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var cuerpo = await LeerCuerpoAsync(await client.GetAsync("/api/catalogos/proveedores?q=VARIOS"));

        var codigos = cuerpo.GetProperty("resultados").EnumerateArray()
            .Select(r => r.GetProperty("codigo").GetString())
            .ToArray();
        Assert.Equal(new[] { "P00F01" }, codigos);
    }

    [Fact]
    public async Task Get_WithoutACookie_Returns401()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/catalogos/proveedores?q=ACME");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
