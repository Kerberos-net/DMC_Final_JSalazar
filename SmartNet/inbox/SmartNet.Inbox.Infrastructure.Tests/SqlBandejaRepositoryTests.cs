using SmartNet.Db.TestBootstrap;

namespace SmartNet.Inbox.Infrastructure.Tests;

/// <summary>
/// Task 3.7 -- <see cref="SqlBandejaRepository"/> backing <c>GET /api/bandeja?estado=&amp;orden=</c>
/// (design D6).
/// </summary>
public sealed class SqlBandejaRepositoryTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;

    public async Task InitializeAsync() => _db = await InboxTestDatabaseFixtureHelper.MigratedDatabaseAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task ListarAsync_FiltersByEstadoConsumo()
    {
        var procesamientoId1 = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-a");
        var pendienteId = await _db.InsertarInboxEventAsync(procesamientoId1, "{}");
        var procesamientoId2 = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-b");
        var descartadoId = await _db.InsertarInboxEventAsync(procesamientoId2, "{}");
        await _db.ExecuteNonQueryAsync(
            $"UPDATE fact.InboxEvent SET EstadoConsumo = 'DESCARTADO', MotivoDescarte = 'sin monto' WHERE InboxEventId = {descartadoId};");

        var sut = new SqlBandejaRepository(_db.ConnectionString);

        var soloDescartados = await sut.ListarAsync("DESCARTADO", "asc", CancellationToken.None);
        var item = Assert.Single(soloDescartados);
        Assert.Equal(descartadoId, item.InboxEventId);
        Assert.Equal("sin monto", item.MotivoDescarte);

        var todos = await sut.ListarAsync(null, "asc", CancellationToken.None);
        Assert.Equal(2, todos.Count);
        Assert.Contains(todos, i => i.InboxEventId == pendienteId);
    }

    [Fact]
    public async Task ListarAsync_IncludesIndicadores_WhenTheEventWasPromoted()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var inboxEventId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        var promocionRepo = new SqlPromocionRepository(_db.ConnectionString);
        var factura = new Core.FacturaPromovida(
            ProveedorCodigo: "P00000", TipoComprobante: "01", Numero: "F001-1", RucProveedor: "20100000001",
            TotalOrig: 100m, Moneda: "PEN", FechaEmision: new DateOnly(2026, 8, 9),
            Indicadores: new Core.IndicadoresFactura(true, false, false, false, false),
            Extracciones: Array.Empty<Core.FacturaExtraccionPromovida>(), Estado: "PENDIENTE_VALIDACION");
        var documento = new Core.DocumentoPromovido(
            DocumentoRecibidoId: 1, NombreArchivo: "f.pdf", MimeType: "application/pdf", RutaRelativa: "/f.pdf", TamanoBytes: 10);
        await promocionRepo.PromoverAsync(inboxEventId, procesamientoId, factura, documento, CancellationToken.None);

        var sut = new SqlBandejaRepository(_db.ConnectionString);
        var resultado = await sut.ListarAsync("PROMOVIDO", "asc", CancellationToken.None);

        var item = Assert.Single(resultado);
        Assert.NotNull(item.FacturaId);
        Assert.NotNull(item.Indicadores);
        Assert.True(item.Indicadores!.EsProveedorGenerico);
    }

    [Fact]
    public async Task ListarAsync_OrdersByFecha_Descending()
    {
        var procesamientoId1 = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-c");
        var primeroId = await _db.InsertarInboxEventAsync(procesamientoId1, "{}");
        var procesamientoId2 = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-d");
        var segundoId = await _db.InsertarInboxEventAsync(procesamientoId2, "{}");
        var sut = new SqlBandejaRepository(_db.ConnectionString);

        var resultado = await sut.ListarAsync(null, "desc", CancellationToken.None);

        Assert.Equal(segundoId, resultado[0].InboxEventId);
        Assert.Equal(primeroId, resultado[1].InboxEventId);
    }
}
