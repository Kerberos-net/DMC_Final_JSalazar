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
                AfectacionMixta: false),
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
}
