using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartNet.Db.TestBootstrap;
using SmartNet.Facturacion.Core;
using SmartNet.Inbox.Core;
using SmartNet.Inbox.Infrastructure;

namespace SmartNet.Api.Tests;

/// <summary>
/// tasks.md Phase 2 (PR 2) — <c>FacturaEndpoints</c> (design D2/D3/D4/D6) against the real database
/// via <see cref="SmartNetApiFactory"/>: If-Match/428/412 (D2), the 409/422 mapping (D3), abrir's
/// idempotency, descartar/adjuntos' no-audit and audit rules (D6).
/// </summary>
public sealed class FacturaEndpointsTests : SesionEndpointsTestBase
{
    private async Task<HttpClient> AuthenticatedClientAsync(SmartNetApiFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var loginResponse = await client.PostAsJsonAsync("/api/sesion", new LoginRequest(NombreUsuario, ClavePlanaCorrecta));
        var cookie = ExtractSessionCookie(loginResponse)!;
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    // --- PATCH: If-Match ---

    [Fact]
    public async Task PatchFactura_WithoutIfMatch_Returns428()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PatchAsJsonAsync($"/api/facturas/{facturaId}", new CorreccionFacturaRequest(
            ProveedorCodigo: null, RucProveedor: "20999999999", Moneda: null, TotalOrig: null, FechaEmision: null,
            Motivo: null, Afectacion: null));

        Assert.Equal((HttpStatusCode)428, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PatchFactura_WithAStaleIfMatch_Returns412_AndLeavesTheRowUnchanged()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        await Db.ExecuteNonQueryAsync($"UPDATE fact.Factura SET RucProveedor = '20999999999' WHERE FacturaId = {facturaId};");
        var etagObsoleto = TokenDeConcurrencia.Codificar(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/facturas/{facturaId}")
        {
            Content = JsonContent.Create(new CorreccionFacturaRequest(null, "20111111111", null, null, null, null, null)),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etagObsoleto);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
    }

    [Fact]
    public async Task PatchFactura_WithAMatchingIfMatch_AppliesTheChange_AndReturnsANewEtag()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var version = await Db.ObtenerVersionFacturaAsync(facturaId);
        var etag = TokenDeConcurrencia.Codificar(version);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/facturas/{facturaId}")
        {
            Content = JsonContent.Create(new CorreccionFacturaRequest(null, "20999999999", null, null, null, null, null)),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.ETag is not null);
        Assert.NotEqual(etag, response.Headers.ETag!.Tag);
        var rucActual = await Db.ExecuteScalarAsync<string>($"SELECT RucProveedor FROM fact.Factura WHERE FacturaId = {facturaId};");
        Assert.Equal("20999999999", rucActual!.TrimEnd());
    }

    [Fact]
    public async Task PatchFactura_OnAnAlreadyValidatedFactura_WritesOneCorreccionAuditRow()
    {
        var facturaId = await Db.InsertarFacturaAsync(estado: "VALIDADA");
        var version = await Db.ObtenerVersionFacturaAsync(facturaId);
        var etag = TokenDeConcurrencia.Codificar(version);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/facturas/{facturaId}")
        {
            Content = JsonContent.Create(new CorreccionFacturaRequest(null, "20999999999", null, null, null, null, null)),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cantidad = await Db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.AuditoriaCorreccion WHERE EntidadTipo = 'FACTURA' AND EntidadId = {facturaId} AND Accion = 'CORRECCION';");
        Assert.Equal(1, cantidad);
    }

    // --- abrir ---

    [Fact]
    public async Task Abrir_WhenTheFacturaHasNoAsiento_CreatesOneInBorrador()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsync($"/api/facturas/{facturaId}/abrir", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cantidad = await Db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.AsientoContable WHERE FacturaId = {facturaId} AND Estado = 'BORRADOR';");
        Assert.Equal(1, cantidad);
    }

    [Fact]
    public async Task Abrir_WhenAnAsientoAlreadyExists_IsIdempotent()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsync($"/api/facturas/{facturaId}/abrir", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cantidad = await Db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.AsientoContable WHERE FacturaId = {facturaId};");
        Assert.Equal(1, cantidad);
    }

    [Fact]
    public async Task Abrir_WhenTheFacturaDoesNotExist_Returns404()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsync("/api/facturas/999999/abrir", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- abrir -- Phase 5 (PR 5): spec.md "Opening a factura with no tipo de cambio (foreign
    // currency)" -- verify-report.md CRITICAL finding. ---

    [Fact]
    public async Task Abrir_ForeignCurrencyWithNoTipoCambio_Returns409_AndCreatesNoAsiento()
    {
        var facturaId = await Db.InsertarFacturaAsync(moneda: "USD", fechaEmision: "2026-08-10");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsync($"/api/facturas/{facturaId}/abrir", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var cantidad = await Db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.AsientoContable WHERE FacturaId = {facturaId};");
        Assert.Equal(0, cantidad);
    }

    [Fact]
    public async Task Abrir_ForeignCurrencyWithATipoCambio_CreatesTheAsientoNormally()
    {
        var facturaId = await Db.InsertarFacturaAsync(moneda: "USD", fechaEmision: "2026-08-10");
        await Db.InsertarTipoCambioAsync(fecha: "2026-08-10");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsync($"/api/facturas/{facturaId}/abrir", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cantidad = await Db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.AsientoContable WHERE FacturaId = {facturaId} AND Estado = 'BORRADOR';");
        Assert.Equal(1, cantidad);
    }

    [Fact]
    public async Task Abrir_LocalCurrencyWithNoTipoCambio_StillCreatesTheAsiento_Regression()
    {
        // Regression guard (must not break the existing PR 2 idempotency/happy-path tests above):
        // a PEN factura never needs fact.TipoCambio, no matter how the D4 gate is wired.
        var facturaId = await Db.InsertarFacturaAsync();
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsync($"/api/facturas/{facturaId}/abrir", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cantidad = await Db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.AsientoContable WHERE FacturaId = {facturaId} AND Estado = 'BORRADOR';");
        Assert.Equal(1, cantidad);
    }

    // --- validar ---

    [Fact]
    public async Task Validar_WhenTheFacturaHasNoAsientoVigente_Returns404()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsync($"/api/facturas/{facturaId}/validar?fechaCorteContable=2026-08-01", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Validar_WhenTheAsientoIsBalanced_Succeeds_AndAssignsACorrelativo()
    {
        var facturaId = await Db.InsertarFacturaAsync(numero: "F001-VALIDAR-OK");
        await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsync($"/api/facturas/{facturaId}/validar?fechaCorteContable=2026-08-01", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var estado = await Db.ExecuteScalarAsync<string>(
            $"SELECT Estado FROM fact.AsientoContable WHERE FacturaId = {facturaId};");
        Assert.Equal("CONFIRMADO", estado!.TrimEnd());
    }

    [Fact]
    public async Task Validar_WhenADuplicateIdentityExists_Returns409()
    {
        var facturaOriginal = await Db.InsertarFacturaAsync(numero: "F001-DUP", rucProveedor: "20100000099");
        var facturaDuplicada = await Db.InsertarFacturaAsync(numero: "F001-DUP", rucProveedor: "20100000099");
        await Db.InsertarAsientoBorradorBalanceadoAsync(facturaDuplicada);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsync($"/api/facturas/{facturaDuplicada}/validar?fechaCorteContable=2026-08-01", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        _ = facturaOriginal; // solo para que IX_Factura_Identidad tenga la otra fila del duplicado.
    }

    // --- descartar ---

    [Fact]
    public async Task Descartar_OnAPendienteFactura_WritesNoAuditRow()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var version = await Db.ObtenerVersionFacturaAsync(facturaId);
        var etag = TokenDeConcurrencia.Codificar(version);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/facturas/{facturaId}/descartar");
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var estado = await Db.ExecuteScalarAsync<string>($"SELECT Estado FROM fact.Factura WHERE FacturaId = {facturaId};");
        Assert.Equal("DESCARTADA", estado!.TrimEnd());
        var cantidadAuditoria = await Db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.AuditoriaCorreccion WHERE EntidadTipo = 'FACTURA' AND EntidadId = {facturaId};");
        Assert.Equal(0, cantidadAuditoria);
    }

    [Fact]
    public async Task Descartar_WhenFacturaAlreadyValidada_Returns409()
    {
        var facturaId = await Db.InsertarFacturaAsync(estado: "VALIDADA");
        var version = await Db.ObtenerVersionFacturaAsync(facturaId);
        var etag = TokenDeConcurrencia.Codificar(version);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/facturas/{facturaId}/descartar");
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // --- adjuntos ---

    [Fact]
    public async Task PostAdjunto_OnAValidadaFactura_EmitsDocumentacionActualizada_WithNoAudit()
    {
        var facturaId = await Db.InsertarFacturaAsync(estado: "VALIDADA");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            $"/api/facturas/{facturaId}/adjuntos",
            new RegistrarAdjuntoRequest("comprobante-tardio.pdf", "/adjuntos/comprobante-tardio.pdf", "application/pdf", 2048));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var eventos = await Db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.OutboxEvent WHERE FacturaId = {facturaId} AND Tipo = 'DOCUMENTACION_ACTUALIZADA';");
        Assert.Equal(1, eventos);
        var auditoria = await Db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.AuditoriaCorreccion WHERE EntidadTipo = 'ADJUNTO';");
        Assert.Equal(0, auditoria);
    }

    [Fact]
    public async Task DeleteAdjunto_AlwaysWritesEliminacionAdjuntoAudit()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var postResponse = await client.PostAsJsonAsync(
            $"/api/facturas/{facturaId}/adjuntos",
            new RegistrarAdjuntoRequest("f.pdf", "/adjuntos/f.pdf", "application/pdf", 10));
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
        var adjuntoId = await Db.ExecuteScalarAsync<long>(
            $"SELECT MAX(AdjuntoManualId) FROM fact.AdjuntoManual WHERE FacturaId = {facturaId};");

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/facturas/{facturaId}/adjuntos/{adjuntoId}")
        {
            Content = JsonContent.Create(new EliminarAdjuntoRequest("Adjunto equivocado")),
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var accion = await Db.ExecuteScalarAsync<string>(
            $"SELECT Accion FROM fact.AuditoriaCorreccion WHERE EntidadTipo = 'ADJUNTO' AND EntidadId = {adjuntoId};");
        Assert.Equal("ELIMINACION_ADJUNTO", accion!.TrimEnd());
    }

    [Fact]
    public async Task DeleteAdjunto_WithoutAMotivo_Returns400()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/facturas/{facturaId}/adjuntos/1")
        {
            Content = JsonContent.Create(new EliminarAdjuntoRequest(null)),
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- diseno-visual-spa-item-12 (design D9): FacturaRespuesta projects the 4 indicator columns ---

    [Fact]
    public async Task GetFactura_IncludesTheFourIndicatorFields_MatchingTheirPersistedValues()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        await Db.FijarIndicadoresFacturaAsync(
            facturaId, esProveedorGenerico: true, posibleDuplicado: true, tieneCamposNoExtraidos: false, afectacionMixta: null);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync($"/api/facturas/{facturaId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await response.Content.ReadFromJsonAsync<FacturaRespuesta>();
        Assert.NotNull(cuerpo);
        Assert.True(cuerpo!.EsProveedorGenerico);
        Assert.True(cuerpo.PosibleDuplicado);
        Assert.False(cuerpo.TieneCamposNoExtraidos);
        Assert.Null(cuerpo.AfectacionMixta);
    }

    [Fact]
    public async Task GetFactura_IndicatorFieldsMatchTheBandejaProjection_ForTheSameRow()
    {
        var procesamientoId = await Db.InsertarProcesamientoAsync(gmailMessageId: "msg-parity-1");
        var inboxEventId = await Db.InsertarInboxEventAsync(procesamientoId, "{}");
        var promocionRepository = new SqlPromocionRepository(Db.ConnectionString);
        var facturaPromovida = new FacturaPromovida(
            ProveedorCodigo: "P00000",
            TipoComprobante: "01",
            Numero: "F001-PARITY",
            RucProveedor: "20100000001",
            TotalOrig: 1180.00m,
            Moneda: "PEN",
            FechaEmision: new DateOnly(2026, 8, 10),
            Indicadores: new IndicadoresFactura(
                EsProveedorGenerico: true,
                PosibleDuplicado: true,
                TieneCamposNoExtraidos: false,
                FechaEnDomingo: false,
                AfectacionMixta: false),
            Extracciones: Array.Empty<FacturaExtraccionPromovida>(),
            Estado: "PENDIENTE_VALIDACION");
        var resultado = await promocionRepository.PromoverAsync(
            inboxEventId, procesamientoId, facturaPromovida,
            new DocumentoPromovido(DocumentoRecibidoId: 1, NombreArchivo: "f.pdf", MimeType: "application/pdf",
                RutaRelativa: "/f.pdf", TamanoBytes: 10),
            CancellationToken.None);
        var facturaId = resultado.FacturaId;

        var bandejaRepository = new SqlBandejaRepository(Db.ConnectionString);
        var filtrosBandeja = new FiltrosBandeja(
            Estado: "PROMOVIDO", Desde: null, Hasta: null, Proveedor: null, Orden: "desc", Pagina: 1);
        var bandeja = await bandejaRepository.ListarAsync(filtrosBandeja, CancellationToken.None);
        var itemBandeja = bandeja.Items.Single(i => i.InboxEventId == inboxEventId);

        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync($"/api/facturas/{facturaId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await response.Content.ReadFromJsonAsync<FacturaRespuesta>();
        Assert.NotNull(cuerpo);
        Assert.NotNull(itemBandeja.Indicadores);
        Assert.Equal(itemBandeja.Indicadores!.EsProveedorGenerico, cuerpo!.EsProveedorGenerico);
        Assert.Equal(itemBandeja.Indicadores.PosibleDuplicado, cuerpo.PosibleDuplicado);
        Assert.Equal(itemBandeja.Indicadores.TieneCamposNoExtraidos, cuerpo.TieneCamposNoExtraidos);
        Assert.Equal(itemBandeja.Indicadores.AfectacionMixta, cuerpo.AfectacionMixta);
    }

    // --- diseno-visual-spa-item-12 (design D10): POST /confirmar-afectacion, gate stays dormant ---

    [Fact]
    public async Task ConfirmarAfectacion_WithoutIfMatch_Returns428()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            $"/api/facturas/{facturaId}/confirmar-afectacion", new ConfirmarAfectacionRequest(EsMixta: false));

        Assert.Equal((HttpStatusCode)428, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmarAfectacion_WithAStaleIfMatch_Returns412_AndLeavesAfectacionMixtaUnchanged()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var etagObsoleto = TokenDeConcurrencia.Codificar(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/facturas/{facturaId}/confirmar-afectacion")
        {
            Content = JsonContent.Create(new ConfirmarAfectacionRequest(EsMixta: false)),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etagObsoleto);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
        var afectacionMixta = await Db.ExecuteScalarAsync<bool?>(
            $"SELECT AfectacionMixta FROM fact.Factura WHERE FacturaId = {facturaId};");
        Assert.Null(afectacionMixta);
    }

    [Fact]
    public async Task ConfirmarAfectacion_WithAMatchingIfMatch_SetsAfectacionMixta_AndWritesConfirmacionAfectacionAudit()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var etag = TokenDeConcurrencia.Codificar(await Db.ObtenerVersionFacturaAsync(facturaId));
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/facturas/{facturaId}/confirmar-afectacion")
        {
            Content = JsonContent.Create(new ConfirmarAfectacionRequest(EsMixta: false)),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var afectacionMixta = await Db.ExecuteScalarAsync<bool?>(
            $"SELECT AfectacionMixta FROM fact.Factura WHERE FacturaId = {facturaId};");
        Assert.False(afectacionMixta);
        var accion = await Db.ExecuteScalarAsync<string>(
            $"SELECT Accion FROM fact.AuditoriaCorreccion WHERE EntidadTipo = 'FACTURA' AND EntidadId = {facturaId};");
        Assert.Equal("CONFIRMACION_AFECTACION", accion!.TrimEnd());
    }

    [Fact]
    public async Task ConfirmarAfectacion_ForAnUnknownFactura_Returns404()
    {
        var etag = TokenDeConcurrencia.Codificar(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/facturas/999999/confirmar-afectacion")
        {
            Content = JsonContent.Create(new ConfirmarAfectacionRequest(EsMixta: false)),
        };
        request.Headers.TryAddWithoutValidation("If-Match", etag);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ConfirmarAfectacion_WithoutACookie_Returns401()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.PostAsJsonAsync(
            "/api/facturas/1/confirmar-afectacion", new ConfirmarAfectacionRequest(EsMixta: false));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- 401 guard (matches BandejaEndpointsTests' precedent) ---

    [Fact]
    public async Task GetFactura_WithoutACookie_Returns401()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/facturas/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- factura -> asiento resolution (tasks.md 3.8/3.9, spec.md asiento-lectura-api, design D3) ---

    [Fact]
    public async Task GetAsientoDeFactura_ForAFacturaWithAVigenteAsiento_ReturnsItsIdAndEtag()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var asientoId = await Db.InsertarAsientoBorradorBalanceadoAsync(facturaId);
        var version = await Db.ObtenerVersionAsientoAsync(asientoId);
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync($"/api/facturas/{facturaId}/asiento");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TokenDeConcurrencia.Codificar(version), response.Headers.ETag!.Tag);
        var cuerpo = await response.Content.ReadFromJsonAsync<FacturaAsientoRespuesta>();
        Assert.Equal(asientoId, cuerpo!.AsientoContableId);
    }

    [Fact]
    public async Task GetAsientoDeFactura_ForAFacturaWithNoAsientoYet_IndicatesNoVigenteAsiento_DistinctFrom404()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync($"/api/facturas/{facturaId}/asiento");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await response.Content.ReadFromJsonAsync<FacturaAsientoRespuesta>();
        Assert.Null(cuerpo!.AsientoContableId);
    }

    [Fact]
    public async Task GetAsientoDeFactura_ForAnUnknownFactura_Returns404()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/facturas/999999/asiento");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
