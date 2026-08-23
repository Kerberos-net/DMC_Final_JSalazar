using Microsoft.Extensions.Time.Testing;
using SmartNet.Db.TestBootstrap;

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

    private PromocionBackgroundService BuildSut() => new(
        new SqlEventoInboxRepository(_db.ConnectionString),
        new SqlPromocionRepository(_db.ConnectionString),
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
}
