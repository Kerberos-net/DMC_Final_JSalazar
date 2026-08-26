using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartNet.Db.TestBootstrap;
using SmartNet.Facturacion.Core;

namespace SmartNet.Api.Tests;

/// <summary>
/// tasks.md 1.6 (RED first)/1.7 (GREEN) — <c>GET /api/facturas/{id}/historial</c> (design D7):
/// unauthenticated -&gt; 401; unknown factura id -&gt; <c>200 []</c> (design D7 -- no extra
/// existence query); known id -&gt; entries newest-first.
/// </summary>
public sealed class AuditoriaEndpointsTests : SesionEndpointsTestBase
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
    public async Task GetHistorial_WithoutACookie_Returns401()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/facturas/1/historial");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetHistorial_ForAnUnknownFacturaId_Returns200_WithAnEmptyArray()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/facturas/999999/historial");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await response.Content.ReadFromJsonAsync<EntradaAuditoriaRespuesta[]>();
        Assert.NotNull(cuerpo);
        Assert.Empty(cuerpo!);
    }

    [Fact]
    public async Task GetHistorial_ForAFacturaWithNoCorrections_Returns200_WithAnEmptyArray()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync($"/api/facturas/{facturaId}/historial");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await response.Content.ReadFromJsonAsync<EntradaAuditoriaRespuesta[]>();
        Assert.NotNull(cuerpo);
        Assert.Empty(cuerpo!);
    }

    [Fact]
    public async Task GetHistorial_ForAFacturaWithCorrections_ReturnsThemNewestFirst()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        await Db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.AuditoriaCorreccion
                 (EntidadTipo, EntidadId, Accion, Campo, ValorOriginal, ValorNuevo, Motivo, UsuarioId, OcurridoEn)
             VALUES
                 ('FACTURA', {facturaId}, 'CORRECCION', 'RucProveedor', '20111111111', '20999999999', NULL, {UsuarioId}, '2026-01-01T10:00:00'),
                 ('FACTURA', {facturaId}, 'CORRECCION', 'Moneda', 'PEN', 'USD', NULL, {UsuarioId}, '2026-01-02T10:00:00');
             """);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync($"/api/facturas/{facturaId}/historial");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await response.Content.ReadFromJsonAsync<EntradaAuditoriaRespuesta[]>();
        Assert.NotNull(cuerpo);
        Assert.Equal(2, cuerpo!.Length);
        Assert.Equal("Moneda", cuerpo[0].Campo);
        Assert.Equal("RucProveedor", cuerpo[1].Campo);
    }
}
