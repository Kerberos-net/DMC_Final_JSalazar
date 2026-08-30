using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartNet.Db.TestBootstrap;
using SmartNet.Inbox.Core;
using SmartNet.Inbox.Infrastructure;

namespace SmartNet.Api.Tests;

/// <summary>
/// A local copy of the FK-chain fixture helper WU3's
/// <c>SmartNet.Inbox.Infrastructure.Tests.InboxTestDatabaseFixtureHelper</c> already proved
/// (<c>internal</c> to that assembly, so it cannot be reused here) -- inserts the minimal
/// <c>Email -&gt; DocumentoRecibido -&gt; Procesamiento -&gt; InboxEvent</c> chain a real
/// <c>fact.InboxEvent</c> row needs.
/// </summary>
internal static class BandejaTestDataHelper
{
    public static async Task<long> InsertarProcesamientoAsync(
        this TestDatabaseFixture db, string estado = "COMPLETADO", string gmailMessageId = "msg-bandeja-1")
    {
        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.Email (GmailMessageId, Remitente, Asunto, FechaRecepcion, FechaDeteccion, Estado)
             VALUES ('{gmailMessageId}', 'a@b.com', 'Factura', SYSUTCDATETIME(), SYSUTCDATETIME(), 'CANDIDATO');
             """);
        var emailId = await db.ExecuteScalarAsync<long>(
            $"SELECT MAX(EmailId) FROM fact.Email WHERE GmailMessageId = '{gmailMessageId}';");

        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.DocumentoRecibido
                 (EmailId, GmailMessageId, NombreArchivo, Extension, MimeType, TamanoBytes, HashContenido, RutaRelativa, Estado)
             VALUES
                 ({emailId}, '{gmailMessageId}', 'f.pdf', 'pdf', 'application/pdf', 10, REPLICATE('a', 64), '/f.pdf', 'PROCESADO');
             """);
        var documentoId = await db.ExecuteScalarAsync<long>(
            $"SELECT MAX(DocumentoRecibidoId) FROM fact.DocumentoRecibido WHERE EmailId = {emailId};");

        await db.ExecuteNonQueryAsync(
            $"INSERT INTO fact.Procesamiento (DocumentoRecibidoId, Estado) VALUES ({documentoId}, '{estado}');");
        return await db.ExecuteScalarAsync<long>(
            $"SELECT MAX(ProcesamientoId) FROM fact.Procesamiento WHERE DocumentoRecibidoId = {documentoId};");
    }

    public static async Task<long> InsertarInboxEventAsync(
        this TestDatabaseFixture db, long procesamientoId, string payloadJson)
    {
        var payloadEscaped = payloadJson.Replace("'", "''");
        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.InboxEvent (Tipo, ProcesamientoId, Payload)
             VALUES ('PROCESAMIENTO_FINALIZADO', {procesamientoId}, N'{payloadEscaped}');
             """);
        return await db.ExecuteScalarAsync<long>(
            $"SELECT MAX(InboxEventId) FROM fact.InboxEvent WHERE ProcesamientoId = {procesamientoId};");
    }
}

/// <summary>
/// Task 4.7 -- E2E: <c>GET /api/bandeja</c> (design D6, <see cref="BandejaEndpoints"/>) is a thin,
/// cookie-authorized delegator over <see cref="IBandejaRepository"/> -- runs against the SAME real
/// database <see cref="SqlPromocionRepository"/> (WU3) writes to, proving the composition root
/// (Program.cs registrations, task 4.2) actually wires the concrete SQL adapters behind the ports.
/// </summary>
public sealed class BandejaEndpointsTests : SesionEndpointsTestBase
{
    // Results.Ok(...) serializes with ASP.NET Core's web defaults (camelCase); the bare HttpClient
    // JSON reader used here does not, so property-name-insensitive matching is required to
    // deserialize back into the PascalCase SmartNet.Inbox.Core.BandejaItem record.
    private static readonly JsonSerializerOptions ResponseJsonOptions =
        new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task GetBandeja_WithoutACookie_Returns401()
    {
        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.GetAsync("/api/bandeja");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetBandeja_WithAValidCookie_ReturnsEnvelopeWithPromotedRow()
    {
        var procesamientoIdPromovido = await Db.InsertarProcesamientoAsync(gmailMessageId: "msg-bandeja-promovido");
        var inboxEventIdPromovido = await Db.InsertarInboxEventAsync(procesamientoIdPromovido, "{}");
        var promocionRepository = new SqlPromocionRepository(Db.ConnectionString);
        await promocionRepository.PromoverAsync(
            inboxEventIdPromovido, procesamientoIdPromovido, MuestraFacturaPromovida(), MuestraDocumentoPromovido(), CancellationToken.None);

        var procesamientoIdDescartado = await Db.InsertarProcesamientoAsync(gmailMessageId: "msg-bandeja-descartado");
        var inboxEventIdDescartado = await Db.InsertarInboxEventAsync(procesamientoIdDescartado, "{}");
        await promocionRepository.DescartarAsync(
            inboxEventIdDescartado, "Faltan campos requeridos: monto", CancellationToken.None);

        using var client = await ObtenerClienteAutenticadoAsync();

        var response = await client.GetAsync("/api/bandeja?estado=PROMOVIDO");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pagina = await response.Content.ReadFromJsonAsync<PaginaBandeja<BandejaItem>>(ResponseJsonOptions);
        Assert.NotNull(pagina);
        Assert.Equal(1, pagina!.Pagina);
        Assert.Equal(20, pagina.TamanioPagina);
        Assert.Equal(1, pagina.TotalPaginas);
        var promovido = pagina.Items.Single(i => i.InboxEventId == inboxEventIdPromovido);
        Assert.Equal("PROMOVIDO", promovido.EstadoConsumo);
        Assert.Equal("FACTURA", promovido.Origen);
        Assert.NotNull(promovido.FacturaId);
        Assert.NotNull(promovido.Indicadores);
        Assert.DoesNotContain(pagina.Items, i => i.InboxEventId == inboxEventIdDescartado);
    }

    [Fact]
    public async Task GetBandeja_FilteredByEstado_ReturnsOnlyMatchingRows()
    {
        var procesamientoId = await Db.InsertarProcesamientoAsync(gmailMessageId: "msg-bandeja-filtro");
        var inboxEventId = await Db.InsertarInboxEventAsync(procesamientoId, "{}");
        var promocionRepository = new SqlPromocionRepository(Db.ConnectionString);
        await promocionRepository.DescartarAsync(inboxEventId, "Faltan campos requeridos: numero", CancellationToken.None);

        using var client = await ObtenerClienteAutenticadoAsync();

        var response = await client.GetAsync("/api/bandeja?estado=DESCARTADO&orden=desc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pagina = await response.Content.ReadFromJsonAsync<PaginaBandeja<BandejaItem>>(ResponseJsonOptions);
        Assert.NotNull(pagina);
        Assert.All(pagina!.Items, i => Assert.Equal("DESCARTADO", i.EstadoConsumo));
        Assert.Contains(pagina.Items, i => i.InboxEventId == inboxEventId);
    }

    // Task 4.4 -- default view (no `estado`) excludes terminal rows. Deliberately does NOT set up a
    // still-PENDIENTE row here: `PromocionBackgroundService` (BACKLOG #6/#7, unrelated to #13) runs
    // its first poll tick the instant a NEW `WebApplicationFactory` boots and eagerly
    // promotes/discards every `PENDIENTE` `fact.InboxEvent` row it finds via
    // `PayloadInboxParser.Parse` -- a stub `Payload: "{}"` (this file's fixture shape) throws
    // `KeyNotFoundException` there and crashes/stops the whole test host (`HostOptions.
    // BackgroundServiceExceptionBehavior = StopHost`), well before this test's own HTTP call runs.
    // The PENDIENTE-inclusion half of the default-view rule is proven without that host at
    // `SmartNet.Inbox.Infrastructure.Tests.SqlBandejaRepositoryTests.
    // ListarAsync_DefaultView_ExcludesTerminalRows_WhenEstadoIsOmitted` (design.md Testing
    // Strategy already assigns default-view coverage to Core/Infra, not API). This test only needs
    // rows already resolved (`PROMOVIDO`/`DESCARTADO`) before the factory boots, which is safe.
    [Fact]
    public async Task GetBandeja_DefaultView_ExcludesPromotedAndDiscardedRows()
    {
        var procesamientoIdPromovido = await Db.InsertarProcesamientoAsync(gmailMessageId: "msg-bandeja-promovido-terminal");
        var inboxEventIdPromovido = await Db.InsertarInboxEventAsync(procesamientoIdPromovido, "{}");
        var promocionRepository = new SqlPromocionRepository(Db.ConnectionString);
        await promocionRepository.PromoverAsync(
            inboxEventIdPromovido, procesamientoIdPromovido, MuestraFacturaPromovida(), MuestraDocumentoPromovido(), CancellationToken.None);

        var procesamientoIdDescartado = await Db.InsertarProcesamientoAsync(gmailMessageId: "msg-bandeja-descartado-terminal");
        var inboxEventIdDescartado = await Db.InsertarInboxEventAsync(procesamientoIdDescartado, "{}");
        await promocionRepository.DescartarAsync(
            inboxEventIdDescartado, "Faltan campos requeridos: monto", CancellationToken.None);

        using var client = await ObtenerClienteAutenticadoAsync();

        var response = await client.GetAsync("/api/bandeja");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pagina = await response.Content.ReadFromJsonAsync<PaginaBandeja<BandejaItem>>(ResponseJsonOptions);
        Assert.NotNull(pagina);
        Assert.DoesNotContain(pagina!.Items, i => i.InboxEventId == inboxEventIdPromovido);
        Assert.DoesNotContain(pagina.Items, i => i.InboxEventId == inboxEventIdDescartado);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("no-numerico")]
    public async Task GetBandeja_InvalidPagina_Returns400ProblemDetails(string pagina)
    {
        using var client = await ObtenerClienteAutenticadoAsync();

        var response = await client.GetAsync($"/api/bandeja?pagina={pagina}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problema = await response.Content.ReadFromJsonAsync<ProblemDetails>(ResponseJsonOptions);
        Assert.NotNull(problema);
        Assert.Equal(400, problema!.Status);
    }

    [Fact]
    public async Task GetBandeja_DesdeAfterHasta_Returns400ProblemDetails()
    {
        using var client = await ObtenerClienteAutenticadoAsync();

        var response = await client.GetAsync("/api/bandeja?desde=2026-02-01&hasta=2026-01-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problema = await response.Content.ReadFromJsonAsync<ProblemDetails>(ResponseJsonOptions);
        Assert.NotNull(problema);
        Assert.Equal(400, problema!.Status);
    }

    // --- BACKLOG #21 task 2.5: envelope carries the enriched fields + the global resumen --------

    [Fact]
    public async Task GetBandeja_CarriesEnrichedComprobanteFields_AndAGlobalResumen()
    {
        var procesamientoId = await Db.InsertarProcesamientoAsync(gmailMessageId: "msg-bandeja-21-enriquecido");
        var inboxEventId = await Db.InsertarInboxEventAsync(procesamientoId, "{}");
        var promocionRepository = new SqlPromocionRepository(Db.ConnectionString);
        await promocionRepository.PromoverAsync(
            inboxEventId, procesamientoId, MuestraFacturaPromovida(), MuestraDocumentoPromovido(), CancellationToken.None);

        using var client = await ObtenerClienteAutenticadoAsync();

        var sinFiltro = await client.GetAsync("/api/bandeja");
        var conFiltro = await client.GetAsync("/api/bandeja?estado=PROMOVIDO&pagina=1");
        Assert.Equal(HttpStatusCode.OK, sinFiltro.StatusCode);
        Assert.Equal(HttpStatusCode.OK, conFiltro.StatusCode);

        var paginaSinFiltro = await sinFiltro.Content.ReadFromJsonAsync<PaginaBandeja<BandejaItem>>(ResponseJsonOptions);
        var paginaConFiltro = await conFiltro.Content.ReadFromJsonAsync<PaginaBandeja<BandejaItem>>(ResponseJsonOptions);

        Assert.NotNull(paginaSinFiltro!.Resumen);
        Assert.Equal(paginaSinFiltro.Resumen.Total,
            paginaSinFiltro.Resumen.Pendientes + paginaSinFiltro.Resumen.Validadas + paginaSinFiltro.Resumen.ConError
            + paginaSinFiltro.Resumen.Alertas + paginaSinFiltro.Resumen.Descartadas);
        // The generic-proveedor promoted row lands in Alertas; the aggregate sees it even though the
        // default list view does not return it.
        Assert.Equal(1, paginaSinFiltro.Resumen.Alertas);
        Assert.Equal(paginaSinFiltro.Resumen, paginaConFiltro!.Resumen);

        var promovido = paginaConFiltro.Items.Single(i => i.InboxEventId == inboxEventId);
        Assert.Equal("01", promovido.TipoComprobante);
        Assert.Equal("F001-123", promovido.Numero);
        Assert.Equal(1180.00m, promovido.TotalOrig);
        Assert.Equal("PEN", promovido.Moneda);
        Assert.Equal(new DateOnly(2026, 8, 10), promovido.FechaEmision);
    }

    // Tracked so DisposeAsync can dispose every factory created via ObtenerClienteAutenticadoAsync --
    // a factory with no other live reference is eligible for GC mid-test, which disposes its
    // TestServer and turns in-flight requests into ObjectDisposedException.
    private readonly List<SmartNetApiFactory> _factoriesCreadas = new();

    public override async Task DisposeAsync()
    {
        foreach (var factory in _factoriesCreadas)
        {
            await factory.DisposeAsync();
        }

        await base.DisposeAsync();
    }

    private async Task<HttpClient> ObtenerClienteAutenticadoAsync()
    {
        var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        _factoriesCreadas.Add(factory);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var loginResponse = await client.PostAsJsonAsync(
            "/api/sesion", new LoginRequest(NombreUsuario, ClavePlanaCorrecta));
        var cookie = ExtractSessionCookie(loginResponse)!;
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    private static DocumentoPromovido MuestraDocumentoPromovido() =>
        new(DocumentoRecibidoId: 1, NombreArchivo: "f.pdf", MimeType: "application/pdf", RutaRelativa: "/f.pdf", TamanoBytes: 10);

    private static FacturaPromovida MuestraFacturaPromovida() =>
        new(
            ProveedorCodigo: "P00000",
            TipoComprobante: "01",
            Numero: "F001-123",
            RucProveedor: "20100000001",
            TotalOrig: 1180.00m,
            Moneda: "PEN",
            FechaEmision: new DateOnly(2026, 8, 10),
            Indicadores: new IndicadoresFactura(
                EsProveedorGenerico: true,
                PosibleDuplicado: false,
                TieneCamposNoExtraidos: true,
                FechaEnDomingo: false,
                AfectacionMixta: false),
            Extracciones: new[] { new FacturaExtraccionPromovida("total", "1180.00", "XML") },
            Estado: "PENDIENTE_VALIDACION");
}
