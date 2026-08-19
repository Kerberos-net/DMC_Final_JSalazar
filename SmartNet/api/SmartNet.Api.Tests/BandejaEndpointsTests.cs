using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    public async Task GetBandeja_WithAValidCookie_ReturnsPromotedAndDiscardedRows()
    {
        var procesamientoIdPromovido = await Db.InsertarProcesamientoAsync(gmailMessageId: "msg-bandeja-promovido");
        var inboxEventIdPromovido = await Db.InsertarInboxEventAsync(procesamientoIdPromovido, "{}");
        var promocionRepository = new SqlPromocionRepository(Db.ConnectionString);
        await promocionRepository.PromoverAsync(
            inboxEventIdPromovido, procesamientoIdPromovido, MuestraFacturaPromovida(), CancellationToken.None);

        var procesamientoIdDescartado = await Db.InsertarProcesamientoAsync(gmailMessageId: "msg-bandeja-descartado");
        var inboxEventIdDescartado = await Db.InsertarInboxEventAsync(procesamientoIdDescartado, "{}");
        await promocionRepository.DescartarAsync(
            inboxEventIdDescartado, "Faltan campos requeridos: monto", CancellationToken.None);

        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var loginResponse = await client.PostAsJsonAsync(
            "/api/sesion", new LoginRequest(NombreUsuario, ClavePlanaCorrecta));
        var cookie = ExtractSessionCookie(loginResponse)!;
        client.DefaultRequestHeaders.Add("Cookie", cookie);

        var response = await client.GetAsync("/api/bandeja");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<BandejaItem>>(ResponseJsonOptions);
        Assert.NotNull(items);
        var promovido = items!.Single(i => i.InboxEventId == inboxEventIdPromovido);
        Assert.Equal("PROMOVIDO", promovido.EstadoConsumo);
        Assert.NotNull(promovido.FacturaId);
        Assert.NotNull(promovido.Indicadores);

        var descartado = items.Single(i => i.InboxEventId == inboxEventIdDescartado);
        Assert.Equal("DESCARTADO", descartado.EstadoConsumo);
        Assert.Equal("Faltan campos requeridos: monto", descartado.MotivoDescarte);
        Assert.Null(descartado.FacturaId);
    }

    [Fact]
    public async Task GetBandeja_FilteredByEstado_ReturnsOnlyMatchingRows()
    {
        var procesamientoId = await Db.InsertarProcesamientoAsync(gmailMessageId: "msg-bandeja-filtro");
        var inboxEventId = await Db.InsertarInboxEventAsync(procesamientoId, "{}");
        var promocionRepository = new SqlPromocionRepository(Db.ConnectionString);
        await promocionRepository.DescartarAsync(inboxEventId, "Faltan campos requeridos: numero", CancellationToken.None);

        await using var factory = new SmartNetApiFactory(Db.ConnectionString, KeyRingPath);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var loginResponse = await client.PostAsJsonAsync(
            "/api/sesion", new LoginRequest(NombreUsuario, ClavePlanaCorrecta));
        var cookie = ExtractSessionCookie(loginResponse)!;
        client.DefaultRequestHeaders.Add("Cookie", cookie);

        var response = await client.GetAsync("/api/bandeja?estado=DESCARTADO&orden=desc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<BandejaItem>>(ResponseJsonOptions);
        Assert.NotNull(items);
        Assert.All(items!, i => Assert.Equal("DESCARTADO", i.EstadoConsumo));
        Assert.Contains(items!, i => i.InboxEventId == inboxEventId);
    }

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
