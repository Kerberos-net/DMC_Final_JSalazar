using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartNet.Db.TestBootstrap;
using SmartNet.Facturacion.Core;

namespace SmartNet.Api.Tests;

/// <summary>
/// tasks.md Phase 4 (PR 4), task 4.5 — design.md Testing Strategy: "two clients hold the same
/// ETag; the second PATCH must return 412 and leave the row untouched." Distinct from
/// <c>FacturaEndpointsTests</c>/<c>AsientoEndpointsTests</c>' single-client stale-ETag tests
/// (those construct an arbitrary obsolete rowversion): here BOTH requests start from the exact
/// same real ETag read once, simulating two browser tabs open on the same resource.
/// </summary>
public sealed class ConcurrenciaDosClientesTests : SesionEndpointsTestBase
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
    public async Task TwoClientsHoldingTheSameEtag_TheSecondPatchOnFactura_Returns412_AndLeavesTheRowUnchanged()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var etagCompartido = TokenDeConcurrencia.Codificar(await Db.ObtenerVersionFacturaAsync(facturaId));
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var clienteA = await AuthenticatedClientAsync(factory);
        using var clienteB = await AuthenticatedClientAsync(factory);

        var requestA = new HttpRequestMessage(HttpMethod.Patch, $"/api/facturas/{facturaId}")
        {
            Content = JsonContent.Create(new CorreccionFacturaRequest(
                ProveedorCodigo: null, RucProveedor: "20111111111", Moneda: null, TotalOrig: null,
                FechaEmision: null, Motivo: null, Afectacion: null)),
        };
        requestA.Headers.TryAddWithoutValidation("If-Match", etagCompartido);
        var responseA = await clienteA.SendAsync(requestA);
        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);

        var requestB = new HttpRequestMessage(HttpMethod.Patch, $"/api/facturas/{facturaId}")
        {
            Content = JsonContent.Create(new CorreccionFacturaRequest(
                ProveedorCodigo: null, RucProveedor: "20222222222", Moneda: null, TotalOrig: null,
                FechaEmision: null, Motivo: null, Afectacion: null)),
        };
        requestB.Headers.TryAddWithoutValidation("If-Match", etagCompartido);
        var responseB = await clienteB.SendAsync(requestB);

        Assert.Equal(HttpStatusCode.PreconditionFailed, responseB.StatusCode);
        var rucFinal = await Db.ExecuteScalarAsync<string>($"SELECT RucProveedor FROM fact.Factura WHERE FacturaId = {facturaId};");
        Assert.Equal("20111111111", rucFinal!.TrimEnd());
    }

    [Fact]
    public async Task TwoClientsHoldingTheSameEtag_TheSecondPatchOnAsiento_Returns412_AndLeavesTheRowUnchanged()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var asientoId = await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        var etagCompartido = TokenDeConcurrencia.Codificar(await Db.ObtenerVersionAsientoAsync(asientoId));
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var clienteA = await AuthenticatedClientAsync(factory);
        using var clienteB = await AuthenticatedClientAsync(factory);

        var requestA = new HttpRequestMessage(HttpMethod.Patch, $"/api/asientos/{asientoId}")
        {
            Content = JsonContent.Create(new CorreccionAsientoRequest("MotivoDescripcion", null, "Glosa A")),
        };
        requestA.Headers.TryAddWithoutValidation("If-Match", etagCompartido);
        var responseA = await clienteA.SendAsync(requestA);
        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);

        var requestB = new HttpRequestMessage(HttpMethod.Patch, $"/api/asientos/{asientoId}")
        {
            Content = JsonContent.Create(new CorreccionAsientoRequest("MotivoDescripcion", null, "Glosa B")),
        };
        requestB.Headers.TryAddWithoutValidation("If-Match", etagCompartido);
        var responseB = await clienteB.SendAsync(requestB);

        Assert.Equal(HttpStatusCode.PreconditionFailed, responseB.StatusCode);
        var cantidadCorrecciones = await Db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.AuditoriaCorreccion WHERE EntidadTipo = 'ASIENTO' AND EntidadId = {asientoId} AND Accion = 'CORRECCION';");
        Assert.Equal(1, cantidadCorrecciones);
    }
}
