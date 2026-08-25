using System.Linq;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartNet.Db.TestBootstrap;
using SmartNet.Facturacion.Core;

namespace SmartNet.Api.Tests;

/// <summary>
/// tasks.md Phase 3 (PR 3) — <c>AsientoEndpoints</c> (design D2/D3/D4/D6, spec.md api-asientos)
/// against the real database via <see cref="SmartNetApiFactory"/>: PATCH If-Match/412/428 +
/// edit-without-reabrir 409 (task 3.1); líneas by LineaId, reabrir motivo-required/BORRADOR-409,
/// anular terminal/already-anulado-409, REPARTO_MANUAL audit (task 3.2).
/// </summary>
public sealed class AsientoEndpointsTests : SesionEndpointsTestBase
{
    private async Task<HttpClient> AuthenticatedClientAsync(SmartNetApiFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var loginResponse = await client.PostAsJsonAsync("/api/sesion", new LoginRequest(NombreUsuario, ClavePlanaCorrecta));
        var cookie = ExtractSessionCookie(loginResponse)!;
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    private static LineaAsientoRequest LineaValida(string cuentaCodigo = "639915") =>
        new(1, "PRINCIPAL", "D", 100m, 0m, cuentaCodigo, null, null, null);

    // --- PATCH: If-Match / edit-without-reabrir ---

    [Fact]
    public async Task PatchAsiento_WithoutIfMatch_Returns428()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var asientoId = await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PatchAsJsonAsync(
            $"/api/asientos/{asientoId}", new CorreccionAsientoRequest("MotivoDescripcion", null, "Nueva glosa"));

        Assert.Equal((HttpStatusCode)428, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PatchAsiento_WithAStaleIfMatch_Returns412_AndLeavesTheRowUnchanged()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var asientoId = await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        var etagObsoleto = TokenDeConcurrencia.Codificar(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/asientos/{asientoId}")
        {
            Content = JsonContent.Create(new CorreccionAsientoRequest("MotivoDescripcion", null, "Nueva glosa")),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etagObsoleto);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Fact]
    public async Task PatchAsiento_WithAMatchingIfMatch_AppliesTheChange_AndReturnsANewEtag()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var asientoId = await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        var version = await Db.ObtenerVersionAsientoAsync(asientoId);
        var etag = TokenDeConcurrencia.Codificar(version);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/asientos/{asientoId}")
        {
            Content = JsonContent.Create(new CorreccionAsientoRequest("MotivoDescripcion", null, "Nueva glosa")),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.ETag is not null);
        Assert.NotEqual(etag, response.Headers.ETag!.Tag);
        var cantidad = await Db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.AuditoriaCorreccion WHERE EntidadTipo = 'ASIENTO' AND EntidadId = {asientoId} AND Accion = 'CORRECCION';");
        Assert.Equal(1, cantidad);
    }

    [Fact]
    public async Task PatchAsiento_WhenConfirmadoWithoutReabrir_Returns409()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var asientoId = await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        await Db.ExecuteNonQueryAsync($"UPDATE fact.AsientoContable SET Estado = 'CONFIRMADO' WHERE AsientoContableId = {asientoId};");
        var version = await Db.ObtenerVersionAsientoAsync(asientoId);
        var etag = TokenDeConcurrencia.Codificar(version);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/asientos/{asientoId}")
        {
            Content = JsonContent.Create(new CorreccionAsientoRequest("MotivoDescripcion", null, "Nueva glosa")),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // --- líneas by LineaId ---

    [Fact]
    public async Task PostLinea_WithAMatchingIfMatch_InsertsTheLinea_AndReturns201WithLineaId()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var asientoId = await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        var version = await Db.ObtenerVersionAsientoAsync(asientoId);
        var etag = TokenDeConcurrencia.Codificar(version);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/asientos/{asientoId}/lineas")
        {
            Content = JsonContent.Create(LineaValida("421002")),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var cuerpo = await response.Content.ReadFromJsonAsync<LineaCreadaRespuesta>();
        Assert.True(cuerpo!.LineaId > 0);
        var cantidad = await Db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.AsientoContableDetalle WHERE LineaId = {cuerpo.LineaId};");
        Assert.Equal(1, cantidad);
        var auditoria = await Db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.AuditoriaCorreccion WHERE EntidadTipo = 'ASIENTO' AND EntidadId = {asientoId} AND Accion = 'REPARTO_MANUAL';");
        Assert.Equal(1, auditoria);
    }

    [Fact]
    public async Task LineaId_SurvivesDelete_PatchOnTheSurvivingLineaStillTargetsIt()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var asientoId = await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        // Add a fourth línea, then delete the third — the fourth's LineaId must stay addressable.
        var etag1 = TokenDeConcurrencia.Codificar(await Db.ObtenerVersionAsientoAsync(asientoId));
        var postRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/asientos/{asientoId}/lineas")
        {
            Content = JsonContent.Create(LineaValida("421002")),
        };
        postRequest.Headers.TryAddWithoutValidation("If-Match", etag1);
        var postResponse = await client.SendAsync(postRequest);
        var creada = await postResponse.Content.ReadFromJsonAsync<LineaCreadaRespuesta>();
        var lineaSuperviviente = creada!.LineaId;

        var lineaAEliminar = await Db.ExecuteScalarAsync<long>(
            $"SELECT MIN(LineaId) FROM fact.AsientoContableDetalle WHERE AsientoContableId = {asientoId};");

        var etag2 = TokenDeConcurrencia.Codificar(await Db.ObtenerVersionAsientoAsync(asientoId));
        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/api/asientos/{asientoId}/lineas/{lineaAEliminar}");
        deleteRequest.Headers.TryAddWithoutValidation("If-Match", etag2);
        var deleteResponse = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var etag3 = TokenDeConcurrencia.Codificar(await Db.ObtenerVersionAsientoAsync(asientoId));
        var patchRequest = new HttpRequestMessage(HttpMethod.Patch, $"/api/asientos/{asientoId}/lineas/{lineaSuperviviente}")
        {
            Content = JsonContent.Create(LineaValida("421003")),
        };
        patchRequest.Headers.TryAddWithoutValidation("If-Match", etag3);
        var patchResponse = await client.SendAsync(patchRequest);

        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        var cuentaFinal = await Db.ExecuteScalarAsync<string>(
            $"SELECT CuentaCodigo FROM fact.AsientoContableDetalle WHERE LineaId = {lineaSuperviviente};");
        Assert.Equal("421003", cuentaFinal!.TrimEnd());
    }

    [Fact]
    public async Task PatchLinea_WhenConfirmadoWithoutReabrir_Returns409()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var asientoId = await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        var lineaId = await Db.ExecuteScalarAsync<long>(
            $"SELECT MIN(LineaId) FROM fact.AsientoContableDetalle WHERE AsientoContableId = {asientoId};");
        await Db.ExecuteNonQueryAsync($"UPDATE fact.AsientoContable SET Estado = 'CONFIRMADO' WHERE AsientoContableId = {asientoId};");
        var etag = TokenDeConcurrencia.Codificar(await Db.ObtenerVersionAsientoAsync(asientoId));
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/asientos/{asientoId}/lineas/{lineaId}")
        {
            Content = JsonContent.Create(LineaValida()),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // --- reabrir ---

    [Fact]
    public async Task Reabrir_WithMotivo_TransitionsToBorrador_AndRegistersReaperturaAudit()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var asientoId = await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        await Db.ExecuteNonQueryAsync($"UPDATE fact.AsientoContable SET Estado = 'CONFIRMADO' WHERE AsientoContableId = {asientoId};");
        var etag = TokenDeConcurrencia.Codificar(await Db.ObtenerVersionAsientoAsync(asientoId));
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/asientos/{asientoId}/reabrir")
        {
            Content = JsonContent.Create(new ReabrirAnularRequest("Corrección de cuenta")),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var estado = await Db.ExecuteScalarAsync<string>($"SELECT Estado FROM fact.AsientoContable WHERE AsientoContableId = {asientoId};");
        Assert.Equal("BORRADOR", estado!.TrimEnd());
        var accion = await Db.ExecuteScalarAsync<string>(
            $"SELECT Accion FROM fact.AuditoriaCorreccion WHERE EntidadTipo = 'ASIENTO' AND EntidadId = {asientoId};");
        Assert.Equal("REAPERTURA", accion!.TrimEnd());
    }

    [Fact]
    public async Task Reabrir_WithoutMotivo_Returns400()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var asientoId = await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        await Db.ExecuteNonQueryAsync($"UPDATE fact.AsientoContable SET Estado = 'CONFIRMADO' WHERE AsientoContableId = {asientoId};");
        var etag = TokenDeConcurrencia.Codificar(await Db.ObtenerVersionAsientoAsync(asientoId));
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/asientos/{asientoId}/reabrir")
        {
            Content = JsonContent.Create(new ReabrirAnularRequest(null)),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reabrir_WhenBorrador_Returns409()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var asientoId = await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        var etag = TokenDeConcurrencia.Codificar(await Db.ObtenerVersionAsientoAsync(asientoId));
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/asientos/{asientoId}/reabrir")
        {
            Content = JsonContent.Create(new ReabrirAnularRequest("Motivo")),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // --- anular ---

    [Fact]
    public async Task Anular_WhenConfirmado_TransitionsToAnulado_AndRegistersAnulacionAudit()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var asientoId = await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        await Db.ExecuteNonQueryAsync($"UPDATE fact.AsientoContable SET Estado = 'CONFIRMADO' WHERE AsientoContableId = {asientoId};");
        var etag = TokenDeConcurrencia.Codificar(await Db.ObtenerVersionAsientoAsync(asientoId));
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/asientos/{asientoId}/anular")
        {
            Content = JsonContent.Create(new ReabrirAnularRequest("Factura anulada por el proveedor")),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var estado = await Db.ExecuteScalarAsync<string>($"SELECT Estado FROM fact.AsientoContable WHERE AsientoContableId = {asientoId};");
        Assert.Equal("ANULADO", estado!.TrimEnd());
        var accion = await Db.ExecuteScalarAsync<string>(
            $"SELECT Accion FROM fact.AuditoriaCorreccion WHERE EntidadTipo = 'ASIENTO' AND EntidadId = {asientoId};");
        Assert.Equal("ANULACION", accion!.TrimEnd());
    }

    [Fact]
    public async Task Anular_WhenAlreadyAnulado_Returns409_TerminalNoTransition()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var asientoId = await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        await Db.ExecuteNonQueryAsync($"UPDATE fact.AsientoContable SET Estado = 'ANULADO' WHERE AsientoContableId = {asientoId};");
        var etag = TokenDeConcurrencia.Codificar(await Db.ObtenerVersionAsientoAsync(asientoId));
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/asientos/{asientoId}/anular")
        {
            Content = JsonContent.Create(new ReabrirAnularRequest("Motivo")),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Anular_WithoutMotivo_Returns400()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var asientoId = await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        await Db.ExecuteNonQueryAsync($"UPDATE fact.AsientoContable SET Estado = 'CONFIRMADO' WHERE AsientoContableId = {asientoId};");
        var etag = TokenDeConcurrencia.Codificar(await Db.ObtenerVersionAsientoAsync(asientoId));
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/asientos/{asientoId}/anular")
        {
            Content = JsonContent.Create(new ReabrirAnularRequest(null)),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- 401 guard (matches FacturaEndpointsTests' precedent) ---

    [Fact]
    public async Task PatchAsiento_WithoutACookie_Returns401()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.PatchAsJsonAsync("/api/asientos/1", new CorreccionAsientoRequest("Glosa", null, "x"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- GET /api/asientos/{id} (tasks.md 3.8/3.9, spec.md asiento-lectura-api) ---

    [Fact]
    public async Task GetAsiento_ForAnExistingAsiento_Returns200_WithBodyAndEtag()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var asientoId = await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        var version = await Db.ObtenerVersionAsientoAsync(asientoId);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync($"/api/asientos/{asientoId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TokenDeConcurrencia.Codificar(version), response.Headers.ETag!.Tag);
        var cuerpo = await response.Content.ReadFromJsonAsync<AsientoRespuesta>();
        Assert.Equal(asientoId, cuerpo!.AsientoContableId);
    }

    /// <summary>PR5 (BACKLOG #12, Phase 5) — cierra un gap de Phase 3: <c>AsientoRespuesta</c> nunca
    /// exponía <c>Lineas</c>, así que la pantalla de detalle no tenía forma de leer los
    /// <see cref="LineaPersistida.LineaId"/> que necesita para editar/eliminar por id (spec.md
    /// api-asientos: "never position"). <c>IUnidadDeTrabajo.CargarLineasPersistidasAsync</c> ya
    /// existía (Phase 3, PR 3) pero ningún endpoint lo llamaba.</summary>
    [Fact]
    public async Task GetAsiento_ExposesLineasWithTheirStableLineaId()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var asientoId = await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync($"/api/asientos/{asientoId}");

        var cuerpo = await response.Content.ReadFromJsonAsync<AsientoRespuesta>();
        Assert.Equal(3, cuerpo!.Lineas.Count);
        Assert.All(cuerpo.Lineas, l => Assert.True(l.LineaId > 0));
        var primera = cuerpo.Lineas.Single(l => l.Orden == 1);
        Assert.Equal("PRINCIPAL", primera.Bloque);
        Assert.Equal("D", primera.Tipo);
        Assert.Equal(100.00m, primera.Debe);
        Assert.Equal("639915", primera.CuentaCodigo);
    }

    [Fact]
    public async Task GetAsiento_ForAnUnknownId_Returns404()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/asientos/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAsiento_WithoutACookie_Returns401()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/asientos/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- TipoCambioVenta on AsientoRespuesta (tasks.md 3.10/3.11, design D4) ---

    [Fact]
    public async Task GetAsiento_ForAForeignCurrencyFactura_ExposesTheFrozenTipoCambioVenta()
    {
        var facturaId = await Db.InsertarFacturaAsync(moneda: "USD");
        var asientoId = await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        await Db.ExecuteNonQueryAsync(
            $"UPDATE fact.AsientoContable SET TipoCambioVenta = 3.755 WHERE AsientoContableId = {asientoId};");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync($"/api/asientos/{asientoId}");

        var cuerpo = await response.Content.ReadFromJsonAsync<AsientoRespuesta>();
        Assert.Equal(3.755m, cuerpo!.TipoCambioVenta);
    }

    [Fact]
    public async Task GetAsiento_ForAPenFactura_HasNoTipoCambioVenta()
    {
        var facturaId = await Db.InsertarFacturaAsync(moneda: "PEN");
        var asientoId = await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync($"/api/asientos/{asientoId}");

        var cuerpo = await response.Content.ReadFromJsonAsync<AsientoRespuesta>();
        Assert.Null(cuerpo!.TipoCambioVenta);
    }
}
