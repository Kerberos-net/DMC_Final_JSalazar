using SmartNet.Db.TestBootstrap;
using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Infrastructure.Tests;

/// <summary>
/// outbox-mensajeria (BACKLOG #14), tasks.md Phase 3 — el resto de la superficie de
/// <see cref="IUnidadDeTrabajo"/> que Phase 1/2 no pudieron cerrar contra esquema real: el CAS de
/// estado de <see cref="SqlUnidadDeTrabajo.MarcarFacturaValidadaAsync"/> (D10, task 3.1), el fan-out
/// hacia <c>fact.OutboxEventIntegracion</c> por el mapa de aplicabilidad de
/// <see cref="SqlUnidadDeTrabajo.EmitirOutboxAsync"/> (D3, task 3.2), la guarda por-transacción de
/// <c>(Tipo, FacturaId)</c> (D8, deviation 2 de apply-progress batch 2) y la consecuencia de ETag de
/// D10 sobre <c>fact.Factura.Version</c> (task 3.3).
/// </summary>
public sealed class SqlUnidadDeTrabajoOutboxTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;

    public async Task InitializeAsync() => _db = await FacturacionTestDatabaseFixtureHelper.MigratedDatabaseAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    // --- task 3.1: state-CAS real, dedicado (el GREEN ya existía desde apply batch 1) ---

    [Fact]
    public async Task MarcarFacturaValidadaAsync_OnAPendienteValidacionRow_AppliesTheTransition_AndReturnsAplicada()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            var transicion = await uow.MarcarFacturaValidadaAsync(facturaId, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            Assert.Equal(TransicionEstadoFactura.Aplicada, transicion);
        }

        var estado = await _db.ExecuteScalarAsync<string>(
            $"SELECT Estado FROM fact.Factura WHERE FacturaId = {facturaId};");
        Assert.Equal(FacturaPersistida.Validada, estado!.TrimEnd());
    }

    [Fact]
    public async Task MarcarFacturaValidadaAsync_OnAnAlreadyValidadaRow_ReturnsYaValidada_AndWritesNothing()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        await _db.ExecuteNonQueryAsync($"UPDATE fact.Factura SET Estado = 'VALIDADA' WHERE FacturaId = {facturaId};");
        var versionAntes = await _db.ObtenerVersionFacturaAsync(facturaId);
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            var transicion = await uow.MarcarFacturaValidadaAsync(facturaId, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            Assert.Equal(TransicionEstadoFactura.YaValidada, transicion);
        }

        // @@ROWCOUNT = 0 en el UPDATE (design D10) -- la fila (y su Version rowversion) no se toca.
        var versionDespues = await _db.ObtenerVersionFacturaAsync(facturaId);
        Assert.Equal(versionAntes, versionDespues);
    }

    [Fact]
    public async Task MarcarFacturaValidadaAsync_OnADescartadaRow_ReturnsNoTransicionable()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        await _db.ExecuteNonQueryAsync($"UPDATE fact.Factura SET Estado = 'DESCARTADA' WHERE FacturaId = {facturaId};");
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using var uow = await store.AbrirAsync(CancellationToken.None);
        var transicion = await uow.MarcarFacturaValidadaAsync(facturaId, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);

        Assert.Equal(TransicionEstadoFactura.NoTransicionable, transicion);
        var estado = await _db.ExecuteScalarAsync<string>(
            $"SELECT Estado FROM fact.Factura WHERE FacturaId = {facturaId};");
        Assert.Equal("DESCARTADA", estado!.TrimEnd());
    }

    // --- task 3.2: fan-out INSERT hacia fact.OutboxEventIntegracion por el mapa D3 ---

    [Fact]
    public async Task EmitirOutboxAsync_ForFacturaValidada_FansOutToDriveAndSheets()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            await uow.EmitirOutboxAsync("FACTURA_VALIDADA", facturaId, "{}", CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        var destinos = await _db.ExecuteScalarAsync<string>(
            $"""
             SELECT STRING_AGG(oei.Integracion, ',') WITHIN GROUP (ORDER BY oei.Integracion)
             FROM fact.OutboxEventIntegracion oei
             JOIN fact.OutboxEvent oe ON oe.OutboxEventId = oei.OutboxEventId
             WHERE oe.FacturaId = {facturaId} AND oe.Tipo = 'FACTURA_VALIDADA';
             """);
        Assert.Equal("DRIVE,SHEETS", destinos);
    }

    // ADR 0004:57-60 -- "Solo sincroniza Drive: los adjuntos no son un dato del dashboard."
    [Fact]
    public async Task EmitirOutboxAsync_ForDocumentacionActualizada_FansOutToDriveOnly_NotSheets()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            await uow.EmitirOutboxAsync("DOCUMENTACION_ACTUALIZADA", facturaId, "{}", CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        var destinos = await _db.ExecuteScalarAsync<string>(
            $"""
             SELECT STRING_AGG(oei.Integracion, ',') WITHIN GROUP (ORDER BY oei.Integracion)
             FROM fact.OutboxEventIntegracion oei
             JOIN fact.OutboxEvent oe ON oe.OutboxEventId = oei.OutboxEventId
             WHERE oe.FacturaId = {facturaId} AND oe.Tipo = 'DOCUMENTACION_ACTUALIZADA';
             """);
        Assert.Equal("DRIVE", destinos);
    }

    [Fact]
    public async Task EmitirOutboxAsync_FanOutRows_StartPendienteWithNoIntentos()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            await uow.EmitirOutboxAsync("ASIENTO_ANULADO", facturaId, "{}", CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        var pendientesConIntentosCero = await _db.ExecuteScalarAsync<int>(
            $"""
             SELECT COUNT(*)
             FROM fact.OutboxEventIntegracion oei
             JOIN fact.OutboxEvent oe ON oe.OutboxEventId = oei.OutboxEventId
             WHERE oe.FacturaId = {facturaId} AND oe.Tipo = 'ASIENTO_ANULADO'
               AND oei.Estado = 'PENDIENTE' AND oei.Intentos = 0 AND oei.ProximoIntentoEn IS NULL;
             """);
        Assert.Equal(2, pendientesConIntentosCero);
    }

    // --- deviation 2 (apply-progress batch 2): guarda por-transacción de SqlUnidadDeTrabajo, D8 ---

    [Fact]
    public async Task EmitirOutboxAsync_WithARepeatedTipoAndFacturaIdInTheSameTransaction_Throws()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using var uow = await store.AbrirAsync(CancellationToken.None);
        await uow.EmitirOutboxAsync("FACTURA_VALIDADA", facturaId, "{}", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => uow.EmitirOutboxAsync("FACTURA_VALIDADA", facturaId, "{}", CancellationToken.None));
    }

    [Fact]
    public async Task EmitirOutboxAsync_WithDifferentTipos_ForTheSameFactura_InTheSameTransaction_DoesNotThrow()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using var uow = await store.AbrirAsync(CancellationToken.None);
        await uow.EmitirOutboxAsync("FACTURA_VALIDADA", facturaId, "{}", CancellationToken.None);
        await uow.EmitirOutboxAsync("DOCUMENTACION_ACTUALIZADA", facturaId, "{}", CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);

        var total = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.OutboxEvent WHERE FacturaId = {facturaId};");
        Assert.Equal(2, total);
    }

    // --- task 3.3: consecuencia de D10 sobre el ETag del asiento/factura -- 412 tras validar ---

    [Fact]
    public async Task GuardarFacturaAsync_WithThePreValidationVersion_AfterMarcarFacturaValidadaAsyncCommitted_ReturnsVersionEnConflicto()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var versionPrevaidacion = await _db.ObtenerVersionFacturaAsync(facturaId);
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            await uow.MarcarFacturaValidadaAsync(facturaId, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        await using var segundoUow = await store.AbrirAsync(CancellationToken.None);
        var cargada = (await segundoUow.CargarFacturaAsync(facturaId, CancellationToken.None))!;
        var escritura = await segundoUow.GuardarFacturaAsync(
            facturaId, versionPrevaidacion, cargada with { Motivo = 5 }, CancellationToken.None);
        await segundoUow.CommitAsync(CancellationToken.None);

        Assert.Equal(ResultadoEscritura.VersionEnConflicto, escritura);
    }
}
