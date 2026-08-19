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

    [Fact]
    public async Task PromoverAsync_InsertsFacturaAndFacturaExtraccion_AndMarksInboxEventPromovido()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var inboxEventId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        var sut = new SqlPromocionRepository(_db.ConnectionString);

        var resultado = await sut.PromoverAsync(inboxEventId, procesamientoId, MuestraFactura(), CancellationToken.None);

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

    [Fact]
    public async Task PromoverAsync_ReusesExistingFactura_WhenProcesamientoIdAlreadyHasOne()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var primerEventoId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        var sut = new SqlPromocionRepository(_db.ConnectionString);
        var primero = await sut.PromoverAsync(primerEventoId, procesamientoId, MuestraFactura(), CancellationToken.None);

        var segundoEventoId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        var segundo = await sut.PromoverAsync(segundoEventoId, procesamientoId, MuestraFactura(), CancellationToken.None);

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
        var sut = new SqlPromocionRepository(_db.ConnectionString);
        await sut.PromoverAsync(inboxEventId, procesamientoId, MuestraFactura(), CancellationToken.None);

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
