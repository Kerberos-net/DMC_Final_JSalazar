using SmartNet.Db.TestBootstrap;
using SmartNet.Inbox.Core;

namespace SmartNet.Inbox.Infrastructure.Tests;

/// <summary>
/// Tasks 3.5/3.6 -- <see cref="SqlPromocionRepository"/> against a real, migrated database. Design
/// D2: <c>PromoverAsync</c> INSERTs first and only catches a real <c>UQ_Factura_Procesamiento</c>
/// violation (2601/2627) -- never a SELECT-before-INSERT pre-check.
/// </summary>
public sealed class SqlPromocionRepositoryTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;

    public async Task InitializeAsync() => _db = await InboxTestDatabaseFixtureHelper.MigratedDatabaseAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static FacturaPromovida MuestraFactura(string estado = "PENDIENTE_VALIDACION") =>
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
                AfectacionMixta: false,
                CamposNoExtraidos: new[] { "igv", "fechaEmision" }),
            Extracciones: new[] { new FacturaExtraccionPromovida("total", "1180.00", "XML") },
            Estado: estado);

    /// <summary>BACKLOG #12 task 2.1 -- built purely from in-memory values (never a SELECT against
    /// <c>fact.DocumentoRecibido</c>, design D1/task 2.3); <paramref name="documentoRecibidoId"/>
    /// only needs to match the FK chain <see cref="InboxTestDatabaseFixtureHelper.InsertarProcesamientoAsync"/>
    /// already inserted so a real ingested document's provenance is exercised.</summary>
    private static DocumentoPromovido MuestraDocumento(long documentoRecibidoId) =>
        new(
            DocumentoRecibidoId: documentoRecibidoId,
            NombreArchivo: "f.pdf",
            MimeType: "application/pdf",
            RutaRelativa: "/f.pdf",
            TamanoBytes: 10);

    private Task<long> DocumentoRecibidoIdDeAsync(long procesamientoId) =>
        _db.ExecuteScalarAsync<long>($"SELECT DocumentoRecibidoId FROM fact.Procesamiento WHERE ProcesamientoId = {procesamientoId};")!;

    /// <summary>BACKLOG (pdf-asociado-en-documento-factura), Phase 2 -- a payload whose
    /// <c>documento.documentoRecibidoId</c> is <paramref name="documentoRecibidoId"/>, the exact
    /// JSON path <c>ResolverParAsync</c>'s Query B reads (design.md Decision 2). The stored payload
    /// content never drives <c>PromoverAsync</c>/<c>DescartarAsync</c> behavior (they take
    /// already-parsed arguments) -- only Query B ever reads it back.</summary>
    private static string PayloadConDocumentoRecibidoId(long documentoRecibidoId) =>
        $$"""
        {"version": 1, "estadoProcesamiento": "COMPLETADO",
         "documento": {"documentoRecibidoId": {{documentoRecibidoId}}, "tipoDocumento": "XML", "documentoAsociadoId": null,
                       "nombreArchivo": "factura.xml", "mimeType": "application/xml",
                       "rutaRelativa": "2026/08/factura.xml", "tamanoBytes": 2048},
         "comprobante": null, "evidencia": [], "afectacionMixta": null, "camposNoExtraidos": [], "advertenciasAsociacion": []}
        """;

    [Fact]
    public async Task PromoverAsync_InsertsFacturaAndFacturaExtraccion_AndMarksInboxEventPromovido()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var inboxEventId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        var documentoRecibidoId = await DocumentoRecibidoIdDeAsync(procesamientoId);
        var sut = new SqlPromocionRepository(_db.ConnectionString);

        var resultado = await sut.PromoverAsync(
            inboxEventId, procesamientoId, MuestraFactura(), MuestraDocumento(documentoRecibidoId), CancellationToken.None);

        Assert.False(resultado.YaExistia);
        var estadoFactura = await _db.ExecuteScalarAsync<string>(
            $"SELECT Estado FROM fact.Factura WHERE FacturaId = {resultado.FacturaId};");
        Assert.Equal("PENDIENTE_VALIDACION", estadoFactura!.TrimEnd());

        var extraccionCount = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.FacturaExtraccion WHERE FacturaId = {resultado.FacturaId};");
        Assert.Equal(1, extraccionCount);

        var estadoConsumo = await _db.ExecuteScalarAsync<string>(
            $"SELECT EstadoConsumo FROM fact.InboxEvent WHERE InboxEventId = {inboxEventId};");
        Assert.Equal("PROMOVIDO", estadoConsumo!.TrimEnd());
        var facturaIdEnEvento = await _db.ExecuteScalarAsync<long>(
            $"SELECT FacturaId FROM fact.InboxEvent WHERE InboxEventId = {inboxEventId};");
        Assert.Equal(resultado.FacturaId, facturaIdEnEvento);
    }

    /// <summary>BACKLOG #19 Phase 2 (task 2.3) -- promotion persists the worker's per-field
    /// camposNoExtraidos list verbatim into <c>fact.Factura.CamposNoExtraidos</c> (D8: an immutable
    /// extraction fact, no API-side derivation). A non-empty list alongside real extraction
    /// evidence is valid, not a contradiction.</summary>
    [Fact]
    public async Task PromoverAsync_PersistsCamposNoExtraidos_FromIndicadores()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var inboxEventId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        var documentoRecibidoId = await DocumentoRecibidoIdDeAsync(procesamientoId);
        var sut = new SqlPromocionRepository(_db.ConnectionString);

        var resultado = await sut.PromoverAsync(
            inboxEventId, procesamientoId, MuestraFactura(), MuestraDocumento(documentoRecibidoId), CancellationToken.None);

        var camposNoExtraidos = await _db.ExecuteScalarAsync<string>(
            $"SELECT CamposNoExtraidos FROM fact.Factura WHERE FacturaId = {resultado.FacturaId};");
        Assert.Equal("igv,fechaEmision", camposNoExtraidos);
    }

    /// <summary>BACKLOG #19 Phase 2 (task 2.3) -- an all-fields-extracted factura leaves the column
    /// NULL (the SPA reads NULL as "pre-021, fall back to the coarse badge").</summary>
    [Fact]
    public async Task PromoverAsync_LeavesCamposNoExtraidosNull_WhenListIsEmpty()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var inboxEventId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        var documentoRecibidoId = await DocumentoRecibidoIdDeAsync(procesamientoId);
        var sut = new SqlPromocionRepository(_db.ConnectionString);

        var factura = MuestraFactura() with
        {
            Indicadores = MuestraFactura().Indicadores with
            {
                TieneCamposNoExtraidos = false,
                CamposNoExtraidos = Array.Empty<string>(),
            },
        };

        var resultado = await sut.PromoverAsync(
            inboxEventId, procesamientoId, factura, MuestraDocumento(documentoRecibidoId), CancellationToken.None);

        var camposNoExtraidos = await _db.ExecuteScalarAsync<string>(
            $"SELECT CamposNoExtraidos FROM fact.Factura WHERE FacturaId = {resultado.FacturaId};");
        Assert.Null(camposNoExtraidos);
    }

    /// <summary>BACKLOG #12 task 2.1 -- proves the projection row lands with the metadata mapped
    /// verbatim from the payload-derived <see cref="DocumentoPromovido"/>, in the same transaction
    /// as the <c>Factura</c> row it references.</summary>
    [Fact]
    public async Task PromoverAsync_InsertsDocumentoFactura_WithMappedMetadata()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var inboxEventId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        var documentoRecibidoId = await DocumentoRecibidoIdDeAsync(procesamientoId);
        var sut = new SqlPromocionRepository(_db.ConnectionString);

        var resultado = await sut.PromoverAsync(
            inboxEventId, procesamientoId, MuestraFactura(), MuestraDocumento(documentoRecibidoId), CancellationToken.None);

        var facturaIdProyectada = await _db.ExecuteScalarAsync<long>(
            $"SELECT FacturaId FROM fact.DocumentoFactura WHERE DocumentoRecibidoId = {documentoRecibidoId};");
        Assert.Equal(resultado.FacturaId, facturaIdProyectada);

        var nombreArchivo = await _db.ExecuteScalarAsync<string>(
            $"SELECT NombreArchivo FROM fact.DocumentoFactura WHERE DocumentoRecibidoId = {documentoRecibidoId};");
        Assert.Equal("f.pdf", nombreArchivo!.TrimEnd());
        var mimeType = await _db.ExecuteScalarAsync<string>(
            $"SELECT MimeType FROM fact.DocumentoFactura WHERE DocumentoRecibidoId = {documentoRecibidoId};");
        Assert.Equal("application/pdf", mimeType!.TrimEnd());
        var rutaRelativa = await _db.ExecuteScalarAsync<string>(
            $"SELECT RutaRelativa FROM fact.DocumentoFactura WHERE DocumentoRecibidoId = {documentoRecibidoId};");
        Assert.Equal("/f.pdf", rutaRelativa!.TrimEnd());
        var tamanoBytes = await _db.ExecuteScalarAsync<long>(
            $"SELECT TamanoBytes FROM fact.DocumentoFactura WHERE DocumentoRecibidoId = {documentoRecibidoId};");
        Assert.Equal(10, tamanoBytes);
    }

    /// <summary>BACKLOG #12 task 2.1 -- a re-processed <c>InboxEvent</c> for the same
    /// <c>DocumentoRecibidoId</c> (e.g. a duplicate promoción attempt) projects at most one
    /// <c>fact.DocumentoFactura</c> row, same anti-duplicate discipline as <c>fact.Factura</c>.
    /// </summary>
    [Fact]
    public async Task PromoverAsync_DoesNotDuplicateDocumentoFactura_WhenDocumentoRecibidoIdRepeats()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var documentoRecibidoId = await DocumentoRecibidoIdDeAsync(procesamientoId);
        var primerEventoId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        var sut = new SqlPromocionRepository(_db.ConnectionString);
        await sut.PromoverAsync(
            primerEventoId, procesamientoId, MuestraFactura(), MuestraDocumento(documentoRecibidoId), CancellationToken.None);

        var segundoEventoId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        await sut.PromoverAsync(
            segundoEventoId, procesamientoId, MuestraFactura(), MuestraDocumento(documentoRecibidoId), CancellationToken.None);

        var totalProyectado = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.DocumentoFactura WHERE DocumentoRecibidoId = {documentoRecibidoId};");
        Assert.Equal(1, totalProyectado);
    }

    [Fact]
    public async Task PromoverAsync_ReusesExistingFactura_WhenProcesamientoIdAlreadyHasOne()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var documentoRecibidoId = await DocumentoRecibidoIdDeAsync(procesamientoId);
        var primerEventoId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        var sut = new SqlPromocionRepository(_db.ConnectionString);
        var primero = await sut.PromoverAsync(
            primerEventoId, procesamientoId, MuestraFactura(), MuestraDocumento(documentoRecibidoId), CancellationToken.None);

        var segundoEventoId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        var segundo = await sut.PromoverAsync(
            segundoEventoId, procesamientoId, MuestraFactura(), MuestraDocumento(documentoRecibidoId), CancellationToken.None);

        Assert.True(segundo.YaExistia);
        Assert.Equal(primero.FacturaId, segundo.FacturaId);
        var totalFacturas = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.Factura WHERE ProcesamientoId = {procesamientoId};");
        Assert.Equal(1, totalFacturas);
        var estadoConsumoSegundo = await _db.ExecuteScalarAsync<string>(
            $"SELECT EstadoConsumo FROM fact.InboxEvent WHERE InboxEventId = {segundoEventoId};");
        Assert.Equal("PROMOVIDO", estadoConsumoSegundo!.TrimEnd());
    }

    [Fact]
    public async Task DescartarAsync_MarksInboxEventDescartado_AndCreatesNoFacturaRow()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var inboxEventId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        var sut = new SqlPromocionRepository(_db.ConnectionString);

        await sut.DescartarAsync(inboxEventId, "Faltan campos requeridos: monto", CancellationToken.None);

        var estadoConsumo = await _db.ExecuteScalarAsync<string>(
            $"SELECT EstadoConsumo FROM fact.InboxEvent WHERE InboxEventId = {inboxEventId};");
        Assert.Equal("DESCARTADO", estadoConsumo!.TrimEnd());
        var motivo = await _db.ExecuteScalarAsync<string>(
            $"SELECT MotivoDescarte FROM fact.InboxEvent WHERE InboxEventId = {inboxEventId};");
        Assert.Equal("Faltan campos requeridos: monto", motivo);
        var facturaCount = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.Factura WHERE ProcesamientoId = {procesamientoId};");
        Assert.Equal(0, facturaCount);
    }

    [Fact]
    public async Task ResolverProveedorAsync_ReturnsExisteTrue_WhenTheRucMatchesADboProveedorRow()
    {
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO dbo.Proveedor (codpro, proveedor, rucpro) VALUES ('P00123', 'Acme SAC', '20100000001');");
        var sut = new SqlPromocionRepository(_db.ConnectionString);

        var resultado = await sut.ResolverProveedorAsync("20100000001", CancellationToken.None);

        Assert.True(resultado.Existe);
        Assert.Equal("P00123", resultado.Codigo);
    }

    [Fact]
    public async Task ResolverProveedorAsync_ReturnsGenericCode_WhenNoRucMatches()
    {
        var sut = new SqlPromocionRepository(_db.ConnectionString);

        var resultado = await sut.ResolverProveedorAsync("99999999999", CancellationToken.None);

        Assert.False(resultado.Existe);
        Assert.Equal("P00000", resultado.Codigo);
    }

    [Fact]
    public async Task ExisteIdentidadPreviaAsync_ReturnsTrue_WhenAMatchingFacturaAlreadyExists()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var inboxEventId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        var documentoRecibidoId = await DocumentoRecibidoIdDeAsync(procesamientoId);
        var sut = new SqlPromocionRepository(_db.ConnectionString);
        await sut.PromoverAsync(
            inboxEventId, procesamientoId, MuestraFactura(), MuestraDocumento(documentoRecibidoId), CancellationToken.None);

        var existe = await sut.ExisteIdentidadPreviaAsync("20100000001", "01", "F001-123", CancellationToken.None);

        Assert.True(existe);
    }

    [Fact]
    public async Task ExisteIdentidadPreviaAsync_ReturnsFalse_WhenNoFacturaMatches()
    {
        var sut = new SqlPromocionRepository(_db.ConnectionString);

        var existe = await sut.ExisteIdentidadPreviaAsync("20100000001", "01", "F001-999", CancellationToken.None);

        Assert.False(existe);
    }

    /// <summary>design.md Decision 2, Query A -- a non-<c>DESCARTADA</c> partner <c>Factura</c>
    /// already projects a <c>DocumentoFactura</c> row on the associated document's id.</summary>
    [Fact]
    public async Task ResolverParAsync_ReturnsFusionable_WhenQueryAHitsANonDiscardedPartnerFactura()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var documentoRecibidoId = await DocumentoRecibidoIdDeAsync(procesamientoId);
        var inboxEventId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        var sut = new SqlPromocionRepository(_db.ConnectionString);
        var promovido = await sut.PromoverAsync(
            inboxEventId, procesamientoId, MuestraFactura(), MuestraDocumento(documentoRecibidoId), CancellationToken.None);

        var resolucion = await sut.ResolverParAsync(documentoRecibidoId, CancellationToken.None);

        var fusionable = Assert.IsType<ResolucionPar.Fusionable>(resolucion);
        Assert.Equal(promovido.FacturaId, fusionable.FacturaId);
    }

    /// <summary>design.md ordering proof table -- partner <c>Factura</c> later <c>DESCARTADA</c> by
    /// a human (Query A empty, Estado filter) while the event still reads <c>PROMOVIDO</c> (Query
    /// B) -- terminates the pair, never an infinite defer.</summary>
    [Fact]
    public async Task ResolverParAsync_ReturnsParNoPromovible_WhenPartnerFacturaWasDiscardedAfterPromotion()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var documentoRecibidoId = await DocumentoRecibidoIdDeAsync(procesamientoId);
        var inboxEventId = await _db.InsertarInboxEventAsync(
            procesamientoId, PayloadConDocumentoRecibidoId(documentoRecibidoId));
        var sut = new SqlPromocionRepository(_db.ConnectionString);
        var promovido = await sut.PromoverAsync(
            inboxEventId, procesamientoId, MuestraFactura(), MuestraDocumento(documentoRecibidoId), CancellationToken.None);
        await _db.ExecuteNonQueryAsync($"UPDATE fact.Factura SET Estado = 'DESCARTADA' WHERE FacturaId = {promovido.FacturaId};");

        var resolucion = await sut.ResolverParAsync(documentoRecibidoId, CancellationToken.None);

        Assert.IsType<ResolucionPar.ParNoPromovible>(resolucion);
    }

    [Fact]
    public async Task ResolverParAsync_ReturnsParNoPromovible_WhenPartnerEventWasDescartado()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var documentoRecibidoId = await DocumentoRecibidoIdDeAsync(procesamientoId);
        var inboxEventId = await _db.InsertarInboxEventAsync(
            procesamientoId, PayloadConDocumentoRecibidoId(documentoRecibidoId));
        var sut = new SqlPromocionRepository(_db.ConnectionString);
        await sut.DescartarAsync(inboxEventId, "Faltan campos requeridos: monto", CancellationToken.None);

        var resolucion = await sut.ResolverParAsync(documentoRecibidoId, CancellationToken.None);

        Assert.IsType<ResolucionPar.ParNoPromovible>(resolucion);
    }

    [Fact]
    public async Task ResolverParAsync_ReturnsNoDisponible_WhenPartnerEventIsStillPendiente()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var documentoRecibidoId = await DocumentoRecibidoIdDeAsync(procesamientoId);
        await _db.InsertarInboxEventAsync(procesamientoId, PayloadConDocumentoRecibidoId(documentoRecibidoId));
        var sut = new SqlPromocionRepository(_db.ConnectionString);

        var resolucion = await sut.ResolverParAsync(documentoRecibidoId, CancellationToken.None);

        Assert.IsType<ResolucionPar.NoDisponible>(resolucion);
    }

    [Fact]
    public async Task ResolverParAsync_ReturnsNoDisponible_WhenPartnerEventIsAbsent()
    {
        var sut = new SqlPromocionRepository(_db.ConnectionString);

        var resolucion = await sut.ResolverParAsync(documentoAsociadoId: 999_999, CancellationToken.None);

        Assert.IsType<ResolucionPar.NoDisponible>(resolucion);
    }

    /// <summary>design.md Decision 4 -- projects onto the given <c>FacturaId</c>, creates NO
    /// <c>fact.Factura</c> row, and marks the source event <c>PROMOVIDO</c>.</summary>
    [Fact]
    public async Task FusionarDocumentoAsync_InsertsOneDocumentoFacturaRow_AndMarksEventPromovido_CreatingNoFactura()
    {
        var procesamientoIdOriginal = await _db.InsertarProcesamientoAsync();
        var documentoRecibidoIdOriginal = await DocumentoRecibidoIdDeAsync(procesamientoIdOriginal);
        var inboxEventIdOriginal = await _db.InsertarInboxEventAsync(procesamientoIdOriginal, "{}");
        var sut = new SqlPromocionRepository(_db.ConnectionString);
        var original = await sut.PromoverAsync(
            inboxEventIdOriginal, procesamientoIdOriginal, MuestraFactura(), MuestraDocumento(documentoRecibidoIdOriginal),
            CancellationToken.None);

        var procesamientoIdPdf = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-inbox-pdf");
        var documentoRecibidoIdPdf = await DocumentoRecibidoIdDeAsync(procesamientoIdPdf);
        var inboxEventIdPdf = await _db.InsertarInboxEventAsync(procesamientoIdPdf, "{}");
        var facturaCountAntes = await _db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fact.Factura;");

        await sut.FusionarDocumentoAsync(
            inboxEventIdPdf, original.FacturaId, MuestraDocumento(documentoRecibidoIdPdf), CancellationToken.None);

        var facturaCountDespues = await _db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fact.Factura;");
        Assert.Equal(facturaCountAntes, facturaCountDespues);

        var documentoCount = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.DocumentoFactura WHERE DocumentoRecibidoId = {documentoRecibidoIdPdf} AND FacturaId = {original.FacturaId};");
        Assert.Equal(1, documentoCount);

        var estadoConsumo = await _db.ExecuteScalarAsync<string>(
            $"SELECT EstadoConsumo FROM fact.InboxEvent WHERE InboxEventId = {inboxEventIdPdf};");
        Assert.Equal("PROMOVIDO", estadoConsumo!.TrimEnd());
        var facturaIdEnEvento = await _db.ExecuteScalarAsync<long>(
            $"SELECT FacturaId FROM fact.InboxEvent WHERE InboxEventId = {inboxEventIdPdf};");
        Assert.Equal(original.FacturaId, facturaIdEnEvento);
    }

    /// <summary>design.md ordering proof table -- a re-emitted (reprocesar) associated event hits
    /// <c>UQ_DocumentoFactura_DocumentoRecibidoId</c>, the same 2601/2627 catch as
    /// <c>PromoverAsync</c>'s own idempotency path; <c>MarcarPromovidoAsync</c> still runs.</summary>
    [Fact]
    public async Task FusionarDocumentoAsync_IsAnIdempotentNoOp_WhenDocumentoRecibidoIdRepeats()
    {
        var procesamientoIdOriginal = await _db.InsertarProcesamientoAsync();
        var documentoRecibidoIdOriginal = await DocumentoRecibidoIdDeAsync(procesamientoIdOriginal);
        var inboxEventIdOriginal = await _db.InsertarInboxEventAsync(procesamientoIdOriginal, "{}");
        var sut = new SqlPromocionRepository(_db.ConnectionString);
        var original = await sut.PromoverAsync(
            inboxEventIdOriginal, procesamientoIdOriginal, MuestraFactura(), MuestraDocumento(documentoRecibidoIdOriginal),
            CancellationToken.None);

        var procesamientoIdPdf = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-inbox-pdf-2");
        var documentoRecibidoIdPdf = await DocumentoRecibidoIdDeAsync(procesamientoIdPdf);
        var primerEventoPdfId = await _db.InsertarInboxEventAsync(procesamientoIdPdf, "{}");
        await sut.FusionarDocumentoAsync(
            primerEventoPdfId, original.FacturaId, MuestraDocumento(documentoRecibidoIdPdf), CancellationToken.None);

        var segundoEventoPdfId = await _db.InsertarInboxEventAsync(procesamientoIdPdf, "{}");
        await sut.FusionarDocumentoAsync(
            segundoEventoPdfId, original.FacturaId, MuestraDocumento(documentoRecibidoIdPdf), CancellationToken.None);

        var totalProyectado = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.DocumentoFactura WHERE DocumentoRecibidoId = {documentoRecibidoIdPdf};");
        Assert.Equal(1, totalProyectado);
        var estadoConsumoSegundo = await _db.ExecuteScalarAsync<string>(
            $"SELECT EstadoConsumo FROM fact.InboxEvent WHERE InboxEventId = {segundoEventoPdfId};");
        Assert.Equal("PROMOVIDO", estadoConsumoSegundo!.TrimEnd());
    }
}
