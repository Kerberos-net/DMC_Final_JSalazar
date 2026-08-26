using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartNet.Db.TestBootstrap;

namespace SmartNet.Api.Tests;

/// <summary>
/// tasks.md Phase 3 (PR 3) -- <c>DocumentoEndpoints</c> (design D1/D2, spec.md
/// documento-contenido-api / documentos-lista-unificada-api) against the real database via
/// <see cref="SmartNetApiFactory"/>: threat-matrix RED cases (3.1-3.4) plus the unified-list merge
/// (3.6).
/// </summary>
public sealed class DocumentoEndpointsTests : SesionEndpointsTestBase
{
    private async Task<HttpClient> AuthenticatedClientAsync(SmartNetApiFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var loginResponse = await client.PostAsJsonAsync("/api/sesion", new LoginRequest(NombreUsuario, ClavePlanaCorrecta));
        var cookie = ExtractSessionCookie(loginResponse)!;
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    // --- Threat matrix (design.md) ---

    [Fact]
    public async Task Contenido_ForAManualAdjunto_WithATraversalRutaRelativa_Returns404()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        // The endpoint never trusts a DB-supplied RutaRelativa (design D2) -- insert one that would
        // escape the configured storage root if resolved naively via Path.Combine.
        await client.PostAsJsonAsync(
            $"/api/facturas/{facturaId}/adjuntos",
            new RegistrarAdjuntoRequest("evil.pdf", "../../../../windows/win.ini", "application/pdf", 10));
        var adjuntoId = await Db.ExecuteScalarAsync<long>(
            $"SELECT MAX(AdjuntoManualId) FROM fact.AdjuntoManual WHERE FacturaId = {facturaId};");

        var response = await client.GetAsync($"/api/documentos/manual-{adjuntoId}/contenido");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Contenido_ForANonAllowListedMime_ServesOctetStream_WithNosniff()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        const string rutaRelativa = "adjuntos/tarjeta.html";
        Directory.CreateDirectory(Path.Combine(factory.StorageRoot, "adjuntos"));
        await File.WriteAllTextAsync(Path.Combine(factory.StorageRoot, rutaRelativa), "<script>evil()</script>");

        await client.PostAsJsonAsync(
            $"/api/facturas/{facturaId}/adjuntos",
            new RegistrarAdjuntoRequest("tarjeta.html", rutaRelativa, "text/html", 100));
        var adjuntoId = await Db.ExecuteScalarAsync<long>(
            $"SELECT MAX(AdjuntoManualId) FROM fact.AdjuntoManual WHERE FacturaId = {facturaId};");

        var response = await client.GetAsync($"/api/documentos/manual-{adjuntoId}/contenido");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var nosniff));
        Assert.Contains("nosniff", nosniff!);
    }

    [Fact]
    public async Task Contenido_WithoutACookie_Returns401_AndNoBytes()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/documentos/manual-1/contenido");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Empty(body);
    }

    [Fact]
    public async Task Contenido_ForAnOrphanRow_WithNoFileOnDisk_Returns404_AndNeverEchoesThePath()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        const string rutaRelativa = "adjuntos/nunca-escrito.pdf";
        await client.PostAsJsonAsync(
            $"/api/facturas/{facturaId}/adjuntos",
            new RegistrarAdjuntoRequest("nunca-escrito.pdf", rutaRelativa, "application/pdf", 10));
        var adjuntoId = await Db.ExecuteScalarAsync<long>(
            $"SELECT MAX(AdjuntoManualId) FROM fact.AdjuntoManual WHERE FacturaId = {facturaId};");

        var response = await client.GetAsync($"/api/documentos/manual-{adjuntoId}/contenido");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(rutaRelativa, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Contenido_ForAnUnknownId_Returns404()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync("/api/documentos/manual-999999/contenido");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Contenido_ForAnAllowListedManualAdjunto_Returns200_WithTheRealBytes_AndInlineDisposition()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        const string rutaRelativa = "adjuntos/factura-real.pdf";
        Directory.CreateDirectory(Path.Combine(factory.StorageRoot, "adjuntos"));
        var bytesEsperados = "%PDF-1.4 contenido de prueba"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(factory.StorageRoot, rutaRelativa), bytesEsperados);

        await client.PostAsJsonAsync(
            $"/api/facturas/{facturaId}/adjuntos",
            new RegistrarAdjuntoRequest("factura-real.pdf", rutaRelativa, "application/pdf", bytesEsperados.Length));
        var adjuntoId = await Db.ExecuteScalarAsync<long>(
            $"SELECT MAX(AdjuntoManualId) FROM fact.AdjuntoManual WHERE FacturaId = {facturaId};");

        var response = await client.GetAsync($"/api/documentos/manual-{adjuntoId}/contenido");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        var bytesRecibidos = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(bytesEsperados, bytesRecibidos);
        Assert.Equal("inline", response.Content.Headers.ContentDisposition?.DispositionType);
    }

    [Fact]
    public async Task Contenido_ForAnIngestaDocumento_Returns200_WithTheRealBytes()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var procesamientoId = await Db.InsertarProcesamientoAsync(gmailMessageId: "msg-doc-1");
        var documentoRecibidoId = await Db.ExecuteScalarAsync<long>(
            "SELECT MAX(DocumentoRecibidoId) FROM fact.DocumentoRecibido;");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        const string rutaRelativa = "ingesta/factura-ingesta.pdf";
        Directory.CreateDirectory(Path.Combine(factory.StorageRoot, "ingesta"));
        var bytesEsperados = "%PDF-1.4 desde ingesta"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(factory.StorageRoot, rutaRelativa), bytesEsperados);

        var documentoFacturaId = await Db.InsertarDocumentoFacturaAsync(
            facturaId, documentoRecibidoId, rutaRelativa: rutaRelativa);

        var response = await client.GetAsync($"/api/documentos/ingesta-{documentoFacturaId}/contenido");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytesRecibidos = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(bytesEsperados, bytesRecibidos);
    }

    // --- Unified list (spec.md documentos-lista-unificada-api) ---

    [Fact]
    public async Task Documentos_ForAFacturaWithNoDocuments_ReturnsAnEmptyList_NotAnError()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);

        var response = await client.GetAsync($"/api/facturas/{facturaId}/documentos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await response.Content.ReadFromJsonAsync<DocumentoRespuesta[]>();
        Assert.Empty(cuerpo!);
    }

    [Fact]
    public async Task Documentos_MergesIngestaAndManual_TaggedByOrigin_NoDuplicates()
    {
        var facturaId = await Db.InsertarFacturaAsync();
        var procesamientoId = await Db.InsertarProcesamientoAsync(gmailMessageId: "msg-doc-2");
        var documentoRecibidoId = await Db.ExecuteScalarAsync<long>(
            "SELECT MAX(DocumentoRecibidoId) FROM fact.DocumentoRecibido;");
        await Db.InsertarDocumentoFacturaAsync(facturaId, documentoRecibidoId);

        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);
        await client.PostAsJsonAsync(
            $"/api/facturas/{facturaId}/adjuntos",
            new RegistrarAdjuntoRequest("manual.pdf", "adjuntos/manual.pdf", "application/pdf", 10));

        var response = await client.GetAsync($"/api/facturas/{facturaId}/documentos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await response.Content.ReadFromJsonAsync<DocumentoRespuesta[]>();
        Assert.Equal(2, cuerpo!.Length);
        Assert.Contains(cuerpo, d => d.Origen == "INGESTA");
        Assert.Contains(cuerpo, d => d.Origen == "MANUAL");
        Assert.Equal(cuerpo.Select(d => d.Id).Distinct().Count(), cuerpo.Length);
    }

    [Fact]
    public async Task Documentos_APreSchema016IngestedDocument_DegradesToManualOnly_NotAnError()
    {
        // A DocumentoRecibido row with no fact.DocumentoFactura projection (schema 016 never ran
        // for it) must not surface as an error -- the list simply has no ingesta-origin entry for it.
        var facturaId = await Db.InsertarFacturaAsync();
        await Db.InsertarProcesamientoAsync(gmailMessageId: "msg-doc-pre-016");
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = await AuthenticatedClientAsync(factory);
        await client.PostAsJsonAsync(
            $"/api/facturas/{facturaId}/adjuntos",
            new RegistrarAdjuntoRequest("manual-only.pdf", "adjuntos/manual-only.pdf", "application/pdf", 10));

        var response = await client.GetAsync($"/api/facturas/{facturaId}/documentos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cuerpo = await response.Content.ReadFromJsonAsync<DocumentoRespuesta[]>();
        Assert.Single(cuerpo!);
        Assert.Equal("MANUAL", cuerpo![0].Origen);
    }

    [Fact]
    public async Task Documentos_WithoutACookie_Returns401()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/facturas/1/documentos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
