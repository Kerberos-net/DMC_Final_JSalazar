using Microsoft.Extensions.Time.Testing;
using SmartNet.Db.TestBootstrap;
using SmartNet.Inbox.Core;

namespace SmartNet.Inbox.Infrastructure.Tests;

/// <summary>
/// Task 3.8/3.9 -- <see cref="PromocionBackgroundService"/> end-to-end over the real SQL adapters
/// (never mocks the ports here, since the whole point is proving the wiring). Drives exactly one
/// cycle via the internal <c>ProcesarPendientesAsync</c> instead of racing the 1-minute
/// <see cref="PeriodicTimer"/> (design D7).
/// </summary>
public sealed class PromocionBackgroundServiceTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;
    private readonly FakeSembradorDeAsiento _sembrador = new();

    /// <summary>BACKLOG #24 (design C2) — el puerto <see cref="ISembradorDeAsiento"/> se falsea aquí:
    /// estas pruebas prueban el <em>cableado</em> (una siembra por factura promovida, cero en las
    /// ramas de fusión/descarte), no la composición real del asiento (eso vive en el E2E de
    /// <c>SmartNet.Api.Tests</c>).</summary>
    private sealed class FakeSembradorDeAsiento : ISembradorDeAsiento
    {
        public List<long> FacturasSembradas { get; } = new();

        public Task SembrarAsync(long facturaId, CancellationToken ct)
        {
            FacturasSembradas.Add(facturaId);
            return Task.CompletedTask;
        }
    }

    public async Task InitializeAsync() => _db = await InboxTestDatabaseFixtureHelper.MigratedDatabaseAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private const string PayloadCompleto =
        """
        {"version": 1, "estadoProcesamiento": "COMPLETADO",
         "documento": {"documentoRecibidoId": 1, "tipoDocumento": "XML", "documentoAsociadoId": 2,
                       "nombreArchivo": "factura.xml", "mimeType": "application/xml",
                       "rutaRelativa": "2026/08/factura.xml", "tamanoBytes": 2048},
         "comprobante": {"tipoComprobante": "01", "numero": "F001-1", "rucProveedor": "20100000001",
                         "nombreProveedor": "Acme SAC", "monto": "100.00", "moneda": "PEN", "fechaEmision": "2026-08-09"},
         "evidencia": [{"campo": "total", "valor": "100.00", "fuente": "XML"}],
         "afectacionMixta": false, "camposNoExtraidos": [], "advertenciasAsociacion": []}
        """;

    private const string PayloadInsuficiente =
        """
        {"version": 1, "estadoProcesamiento": "COMPLETADO",
         "documento": {"documentoRecibidoId": 3, "tipoDocumento": "PDF", "documentoAsociadoId": null,
                       "nombreArchivo": "factura.pdf", "mimeType": "application/pdf",
                       "rutaRelativa": "2026/08/factura.pdf", "tamanoBytes": 4096},
         "comprobante": {"tipoComprobante": "01", "numero": null, "rucProveedor": null,
                         "nombreProveedor": null, "monto": null, "moneda": "PEN", "fechaEmision": "2026-08-09"},
         "evidencia": [], "afectacionMixta": null, "camposNoExtraidos": [], "advertenciasAsociacion": ["SIN_PAREJA"]}
        """;

    /// <summary>BACKLOG (pdf-asociado-en-documento-factura) Phase 3 -- the paired PDF's own
    /// comprobante is structurally incomplete: proves the merge branch never runs
    /// <c>PoliticaDePromocion</c> for it (it would discard on that path).</summary>
    private const string PayloadPdfAsociadoIncompleto =
        """
        {"version": 1, "estadoProcesamiento": "COMPLETADO",
         "documento": {"documentoRecibidoId": 2, "tipoDocumento": "PDF", "documentoAsociadoId": 1,
                       "nombreArchivo": "factura.pdf", "mimeType": "application/pdf",
                       "rutaRelativa": "2026/08/factura.pdf", "tamanoBytes": 4096},
         "comprobante": {"tipoComprobante": null, "numero": null, "rucProveedor": null,
                         "nombreProveedor": null, "monto": null, "moneda": null, "fechaEmision": null},
         "evidencia": [], "afectacionMixta": null, "camposNoExtraidos": [], "advertenciasAsociacion": []}
        """;

    private const string PayloadXmlInsuficienteAsociado =
        """
        {"version": 1, "estadoProcesamiento": "COMPLETADO",
         "documento": {"documentoRecibidoId": 1, "tipoDocumento": "XML", "documentoAsociadoId": 2,
                       "nombreArchivo": "factura.xml", "mimeType": "application/xml",
                       "rutaRelativa": "2026/08/factura.xml", "tamanoBytes": 2048},
         "comprobante": {"tipoComprobante": null, "numero": null, "rucProveedor": null,
                         "nombreProveedor": null, "monto": null, "moneda": null, "fechaEmision": null},
         "evidencia": [], "afectacionMixta": null, "camposNoExtraidos": [], "advertenciasAsociacion": []}
        """;

    private PromocionBackgroundService BuildSut() => new(
        new SqlEventoInboxRepository(_db.ConnectionString),
        new SqlPromocionRepository(_db.ConnectionString),
        _sembrador,
        new FakeTimeProvider());

    [Fact]
    public async Task ProcesarPendientesAsync_PromotesASufficientPayload_ToAPendienteValidacionFactura()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var inboxEventId = await _db.InsertarInboxEventAsync(procesamientoId, PayloadCompleto);
        var sut = BuildSut();

        await sut.ProcesarPendientesAsync(CancellationToken.None);

        var estadoConsumo = await _db.ExecuteScalarAsync<string>(
            $"SELECT EstadoConsumo FROM fact.InboxEvent WHERE InboxEventId = {inboxEventId};");
        Assert.Equal("PROMOVIDO", estadoConsumo!.TrimEnd());
        var facturaCount = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.Factura WHERE ProcesamientoId = {procesamientoId} AND Estado = 'PENDIENTE_VALIDACION';");
        Assert.Equal(1, facturaCount);

        // BACKLOG #12 task 2.2 -- end-to-end proof the wiring projects fact.DocumentoFactura too,
        // not just the repository-level test (documentoRecibidoId comes from PayloadCompleto's own
        // literal `documento` object, never a SELECT against fact.DocumentoRecibido).
        var documentoCount = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.DocumentoFactura WHERE DocumentoRecibidoId = 1 AND NombreArchivo = 'factura.xml';");
        Assert.Equal(1, documentoCount);

        // design.md Decision 1 regression guard (task 3.2): this fixture is XML+asociado -- the
        // one the proposal's broken predicate (DocumentoAsociadoId != null alone, no TipoDocumento
        // check) would have deferred forever instead of promoting. Proves it stays on the
        // unchanged sufficiency path: exactly one Factura total, never a merge/defer branch.
        var facturaCountTotal = await _db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fact.Factura;");
        Assert.Equal(1, facturaCountTotal);
    }

    [Fact]
    public async Task ProcesarPendientesAsync_DiscardsAnInsufficientPayload_CreatingNoFacturaRow()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var inboxEventId = await _db.InsertarInboxEventAsync(procesamientoId, PayloadInsuficiente);
        var sut = BuildSut();

        await sut.ProcesarPendientesAsync(CancellationToken.None);

        var estadoConsumo = await _db.ExecuteScalarAsync<string>(
            $"SELECT EstadoConsumo FROM fact.InboxEvent WHERE InboxEventId = {inboxEventId};");
        Assert.Equal("DESCARTADO", estadoConsumo!.TrimEnd());
        var motivo = await _db.ExecuteScalarAsync<string>(
            $"SELECT MotivoDescarte FROM fact.InboxEvent WHERE InboxEventId = {inboxEventId};");
        Assert.NotNull(motivo);
        var facturaCount = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.Factura WHERE ProcesamientoId = {procesamientoId};");
        Assert.Equal(0, facturaCount);
    }

    [Fact]
    public async Task ProcesarPendientesAsync_ReprocessingTheSamePromotedEvent_IsAnIdempotentNoOp()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var inboxEventId = await _db.InsertarInboxEventAsync(procesamientoId, PayloadCompleto);
        var sut = BuildSut();
        await sut.ProcesarPendientesAsync(CancellationToken.None);

        // Simulate a second, independent InboxEvent for the same Procesamiento (e.g. a rare racing
        // duplicate publish, design D3) and run a second cycle.
        var segundoEventoId = await _db.InsertarInboxEventAsync(procesamientoId, PayloadCompleto);
        await sut.ProcesarPendientesAsync(CancellationToken.None);

        var facturaCount = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.Factura WHERE ProcesamientoId = {procesamientoId};");
        Assert.Equal(1, facturaCount);
        var estadoConsumoSegundo = await _db.ExecuteScalarAsync<string>(
            $"SELECT EstadoConsumo FROM fact.InboxEvent WHERE InboxEventId = {segundoEventoId};");
        Assert.Equal("PROMOVIDO", estadoConsumoSegundo!.TrimEnd());
    }

    /// <summary>design.md ordering proof table -- XML event first promotes normally; the paired
    /// PDF event, processed in a later cycle, hits Query A and merges onto the same Factura
    /// instead of creating a second one.</summary>
    [Fact]
    public async Task ProcesarPendientesAsync_XmlFirstThenPdf_MergesOntoOneFactura_BothEventsPromovido()
    {
        var procesamientoXmlId = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-par-xml-1");
        var inboxEventXmlId = await _db.InsertarInboxEventAsync(procesamientoXmlId, PayloadCompleto);
        var sut = BuildSut();
        await sut.ProcesarPendientesAsync(CancellationToken.None);

        var procesamientoPdfId = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-par-pdf-1");
        var inboxEventPdfId = await _db.InsertarInboxEventAsync(procesamientoPdfId, PayloadPdfAsociadoIncompleto);
        await sut.ProcesarPendientesAsync(CancellationToken.None);

        var facturaCount = await _db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fact.Factura;");
        Assert.Equal(1, facturaCount);
        var documentoCount = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.DocumentoFactura WHERE DocumentoRecibidoId IN (1, 2);");
        Assert.Equal(2, documentoCount);
        var estadoXml = await _db.ExecuteScalarAsync<string>(
            $"SELECT EstadoConsumo FROM fact.InboxEvent WHERE InboxEventId = {inboxEventXmlId};");
        Assert.Equal("PROMOVIDO", estadoXml!.TrimEnd());
        var estadoPdf = await _db.ExecuteScalarAsync<string>(
            $"SELECT EstadoConsumo FROM fact.InboxEvent WHERE InboxEventId = {inboxEventPdfId};");
        Assert.Equal("PROMOVIDO", estadoPdf!.TrimEnd());
    }

    /// <summary>design.md ordering proof table -- the PDF arrives before its XML partner exists at
    /// all. Query A and Query B both come up empty -&gt; defer is a pure no-op (design D3): the
    /// event stays PENDIENTE, no discard, no Factura created.</summary>
    [Fact]
    public async Task ProcesarPendientesAsync_PdfFirst_SingleCycle_StaysPendiente_NoDiscards()
    {
        var procesamientoPdfId = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-par-pdf-2");
        var inboxEventPdfId = await _db.InsertarInboxEventAsync(procesamientoPdfId, PayloadPdfAsociadoIncompleto);
        var sut = BuildSut();

        await sut.ProcesarPendientesAsync(CancellationToken.None);

        var estadoPdf = await _db.ExecuteScalarAsync<string>(
            $"SELECT EstadoConsumo FROM fact.InboxEvent WHERE InboxEventId = {inboxEventPdfId};");
        Assert.Equal("PENDIENTE", estadoPdf!.TrimEnd());
        var descartes = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.InboxEvent WHERE EstadoConsumo = 'DESCARTADO';");
        Assert.Equal(0, descartes);
        var facturaCount = await _db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fact.Factura;");
        Assert.Equal(0, facturaCount);
    }

    /// <summary>design.md ordering proof table -- the XML partner discards on its own (structurally
    /// insufficient, owner decision 3: the PDF never self-promotes). Once the discard is committed
    /// (cycle 1), the PDF's own next cycle (cycle 2) sees Query B = 'DESCARTADO' -&gt;
    /// <c>ParNoPromovible</c> -&gt; discards too. Two separate cycles avoids any same-cycle
    /// ordering assumption over <c>ListarPendientesAsync</c>'s unordered result.</summary>
    [Fact]
    public async Task ProcesarPendientesAsync_XmlDescarta_ThenPdfDescartaAfterTwoCycles()
    {
        var procesamientoXmlId = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-par-xml-2");
        var inboxEventXmlId = await _db.InsertarInboxEventAsync(procesamientoXmlId, PayloadXmlInsuficienteAsociado);
        var sut = BuildSut();
        await sut.ProcesarPendientesAsync(CancellationToken.None);

        var estadoXml = await _db.ExecuteScalarAsync<string>(
            $"SELECT EstadoConsumo FROM fact.InboxEvent WHERE InboxEventId = {inboxEventXmlId};");
        Assert.Equal("DESCARTADO", estadoXml!.TrimEnd());

        var procesamientoPdfId = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-par-pdf-3");
        var inboxEventPdfId = await _db.InsertarInboxEventAsync(procesamientoPdfId, PayloadPdfAsociadoIncompleto);
        await sut.ProcesarPendientesAsync(CancellationToken.None);

        var estadoPdf = await _db.ExecuteScalarAsync<string>(
            $"SELECT EstadoConsumo FROM fact.InboxEvent WHERE InboxEventId = {inboxEventPdfId};");
        Assert.Equal("DESCARTADO", estadoPdf!.TrimEnd());

        // Must discard via the merge-branch route (ResolverParAsync -> ParNoPromovible ->
        // Descarta), NOT the normal PoliticaDePromocion insufficient-fields path -- otherwise this
        // assertion would pass trivially even without the new routing (the PDF's own comprobante
        // is also structurally incomplete). The motive text is what proves which path ran.
        var motivoPdf = await _db.ExecuteScalarAsync<string>(
            $"SELECT MotivoDescarte FROM fact.InboxEvent WHERE InboxEventId = {inboxEventPdfId};");
        Assert.Equal("El evento asociado fue descartado", motivoPdf);

        var facturaCount = await _db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fact.Factura;");
        Assert.Equal(0, facturaCount);
    }

    // --- BACKLOG #24 (design C2/C3): promotion auto-seed via ISembradorDeAsiento ---

    [Fact]
    public async Task ProcesarPendientesAsync_AfterPromotingAFactura_SeedsItsAsientoExactlyOnce()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        await _db.InsertarInboxEventAsync(procesamientoId, PayloadCompleto);
        var sut = BuildSut();

        await sut.ProcesarPendientesAsync(CancellationToken.None);

        var facturaId = await _db.ExecuteScalarAsync<long>(
            $"SELECT FacturaId FROM fact.Factura WHERE ProcesamientoId = {procesamientoId};");
        Assert.Equal(new[] { facturaId }, _sembrador.FacturasSembradas);
    }

    [Fact]
    public async Task ProcesarPendientesAsync_WhenTheEventIsDiscarded_NeverSeedsAnAsiento()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        await _db.InsertarInboxEventAsync(procesamientoId, PayloadInsuficiente);
        var sut = BuildSut();

        await sut.ProcesarPendientesAsync(CancellationToken.None);

        Assert.Empty(_sembrador.FacturasSembradas);
    }

    /// <summary>#25/#26 non-disturbance: the associated-PDF merge branch
    /// (<c>ProcesarDocumentoAsociadoAsync</c>) creates zero <c>fact.Factura</c> rows and MUST never
    /// call <see cref="ISembradorDeAsiento.SembrarAsync"/>. Only the XML's own promotion seeds — and
    /// exactly once, even after the later merge cycle.</summary>
    [Fact]
    public async Task ProcesarPendientesAsync_XmlThenAssociatedPdfMerge_SeedsOnlyForTheXmlPromotion()
    {
        var procesamientoXmlId = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-seed-xml-1");
        await _db.InsertarInboxEventAsync(procesamientoXmlId, PayloadCompleto);
        var sut = BuildSut();
        await sut.ProcesarPendientesAsync(CancellationToken.None);

        var procesamientoPdfId = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-seed-pdf-1");
        await _db.InsertarInboxEventAsync(procesamientoPdfId, PayloadPdfAsociadoIncompleto);
        await sut.ProcesarPendientesAsync(CancellationToken.None);

        var facturaId = await _db.ExecuteScalarAsync<long>("SELECT FacturaId FROM fact.Factura;");
        Assert.Equal(new[] { facturaId }, _sembrador.FacturasSembradas);
    }

    /// <summary>The PDF arrives before its XML partner — defer is a pure no-op, no Factura, no seed.</summary>
    [Fact]
    public async Task ProcesarPendientesAsync_PdfFirstDefer_NeverSeedsAnAsiento()
    {
        var procesamientoPdfId = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-seed-pdf-2");
        await _db.InsertarInboxEventAsync(procesamientoPdfId, PayloadPdfAsociadoIncompleto);
        var sut = BuildSut();

        await sut.ProcesarPendientesAsync(CancellationToken.None);

        Assert.Empty(_sembrador.FacturasSembradas);
    }
}
