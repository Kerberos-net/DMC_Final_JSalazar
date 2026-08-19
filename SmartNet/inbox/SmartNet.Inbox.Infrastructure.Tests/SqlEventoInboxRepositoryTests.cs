using SmartNet.Db.TestBootstrap;

namespace SmartNet.Inbox.Infrastructure.Tests;

/// <summary>
/// Tasks 3.3/3.4 -- <see cref="SqlEventoInboxRepository.ListarPendientesAsync"/> against a real,
/// migrated <c>fact_test_&lt;id&gt;</c> database. Never queries <c>fact.Procesamiento</c> (ADR 0003) --
/// see <c>NoWriteToDboStructuralTests</c>' sibling checks for the structural proof.
/// </summary>
public sealed class SqlEventoInboxRepositoryTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;

    public async Task InitializeAsync() => _db = await InboxTestDatabaseFixtureHelper.MigratedDatabaseAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private const string PayloadJson =
        """{"version": 1, "estadoProcesamiento": "COMPLETADO", "documento": {"documentoRecibidoId": 1, "tipoDocumento": "XML", "documentoAsociadoId": null}, "comprobante": null, "evidencia": [], "afectacionMixta": null, "camposNoExtraidos": [], "advertenciasAsociacion": []}""";

    [Fact]
    public async Task ListarPendientesAsync_ReturnsOnlyPendienteRows()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var pendienteId = await _db.InsertarInboxEventAsync(procesamientoId, PayloadJson);
        var otroProcesamientoId = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-inbox-2");
        var promovidoId = await _db.InsertarInboxEventAsync(otroProcesamientoId, PayloadJson);
        await _db.ExecuteNonQueryAsync($"UPDATE fact.InboxEvent SET EstadoConsumo = 'PROMOVIDO' WHERE InboxEventId = {promovidoId};");

        var sut = new SqlEventoInboxRepository(_db.ConnectionString);
        var pendientes = await sut.ListarPendientesAsync(CancellationToken.None);

        var soloId = Assert.Single(pendientes);
        Assert.Equal(pendienteId, soloId.InboxEventId);
        Assert.Equal(procesamientoId, soloId.ProcesamientoId);
        Assert.Equal(PayloadJson, soloId.PayloadJson);
    }

    [Fact]
    public async Task ListarPendientesAsync_ReturnsEmpty_WhenNoRowIsPendiente()
    {
        var sut = new SqlEventoInboxRepository(_db.ConnectionString);

        var pendientes = await sut.ListarPendientesAsync(CancellationToken.None);

        Assert.Empty(pendientes);
    }
}
