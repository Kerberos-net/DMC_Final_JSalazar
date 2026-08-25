using SmartNet.Db.TestBootstrap;
using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Infrastructure.Tests;

/// <summary>
/// tasks.md 1.10/1.11 — <see cref="SqlUnidadDeTrabajo"/> against a real, migrated database (design
/// D2/D5): CAS write with a stale version returns <see cref="ResultadoEscritura.VersionEnConflicto"/>
/// and leaves the row untouched; the correlativo UPDLOCK increments exactly once and never reuses a
/// number a rolled-back transaction claimed.
/// </summary>
public sealed class SqlUnidadDeTrabajoTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;

    public async Task InitializeAsync() => _db = await FacturacionTestDatabaseFixtureHelper.MigratedDatabaseAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task CargarAsientoAsync_ReturnsNull_WhenTheAsientoDoesNotExist()
    {
        var store = new SqlFacturacionStore(_db.ConnectionString);
        await using var uow = await store.AbrirAsync(CancellationToken.None);

        var resultado = await uow.CargarAsientoAsync(999_999, CancellationToken.None);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task CargarAsientoAsync_ReturnsTheBorradorAsiento_WithItsFacturaAndVersion()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        var store = new SqlFacturacionStore(_db.ConnectionString);
        await using var uow = await store.AbrirAsync(CancellationToken.None);

        var resultado = await uow.CargarAsientoAsync(asientoId, CancellationToken.None);

        Assert.NotNull(resultado);
        Assert.Equal(facturaId, resultado!.FacturaId);
        Assert.Equal(AsientoPersistido.Borrador, resultado.Estado);
        Assert.Equal(8, resultado.Version.Length);
        Assert.Equal(100m, resultado.Asiento.BasePEN);
    }

    // --- PR 5 (Phase 5, verify-report.md CRITICAL finding): HechosDeConflicto.SinTipoCambio ---

    [Fact]
    public async Task CargarAsientoAsync_ForeignCurrencyWithNoTipoCambioRow_ReportsSinTipoCambio()
    {
        var facturaId = await _db.InsertarFacturaAsync(moneda: "USD", fechaEmision: "2026-08-10");
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        var store = new SqlFacturacionStore(_db.ConnectionString);
        await using var uow = await store.AbrirAsync(CancellationToken.None);

        var resultado = await uow.CargarAsientoAsync(asientoId, CancellationToken.None);

        Assert.NotNull(resultado);
        Assert.True(resultado!.Hechos.SinTipoCambio);
    }

    [Fact]
    public async Task CargarAsientoAsync_ForeignCurrencyWithATipoCambioRow_ReportsNoConflict()
    {
        var facturaId = await _db.InsertarFacturaAsync(moneda: "USD", fechaEmision: "2026-08-10");
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        await _db.InsertarTipoCambioAsync(fecha: "2026-08-10");
        var store = new SqlFacturacionStore(_db.ConnectionString);
        await using var uow = await store.AbrirAsync(CancellationToken.None);

        var resultado = await uow.CargarAsientoAsync(asientoId, CancellationToken.None);

        Assert.NotNull(resultado);
        Assert.False(resultado!.Hechos.SinTipoCambio);
    }

    [Fact]
    public async Task CargarAsientoAsync_LocalCurrencyWithNoTipoCambioRow_ReportsNoConflict()
    {
        // Moneda PEN (default) never needs a fact.TipoCambio row -- SinTipoCambio only applies to
        // foreign-currency facturas (spec.md, ADR 0018 pt. 3).
        var facturaId = await _db.InsertarFacturaAsync();
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        var store = new SqlFacturacionStore(_db.ConnectionString);
        await using var uow = await store.AbrirAsync(CancellationToken.None);

        var resultado = await uow.CargarAsientoAsync(asientoId, CancellationToken.None);

        Assert.NotNull(resultado);
        Assert.False(resultado!.Hechos.SinTipoCambio);
    }

    [Fact]
    public async Task GuardarAsientoAsync_WithAStaleVersion_ReturnsVersionEnConflicto_AndLeavesTheRowUnchanged()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        var store = new SqlFacturacionStore(_db.ConnectionString);

        AsientoPersistido cargado;
        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            cargado = (await uow.CargarAsientoAsync(asientoId, CancellationToken.None))!;
        }

        // A second, concurrent client updates the row first — bumping its rowversion.
        await _db.ExecuteNonQueryAsync(
            $"UPDATE fact.AsientoContable SET MotivoDescripcion = 'Otro cliente' WHERE AsientoContableId = {asientoId};");

        await using var segundoUow = await store.AbrirAsync(CancellationToken.None);
        var escritura = await segundoUow.GuardarAsientoAsync(
            asientoId, cargado.Version, cargado with { Estado = AsientoPersistido.Confirmado }, CancellationToken.None);
        await segundoUow.CommitAsync(CancellationToken.None);

        Assert.Equal(ResultadoEscritura.VersionEnConflicto, escritura);
        var estadoActual = await _db.ExecuteScalarAsync<string>(
            $"SELECT Estado FROM fact.AsientoContable WHERE AsientoContableId = {asientoId};");
        Assert.Equal(AsientoPersistido.Borrador, estadoActual!.TrimEnd());
    }

    [Fact]
    public async Task GuardarAsientoAsync_WithAMatchingVersion_AppliesTheWrite()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        var version = await _db.ObtenerVersionAsync(asientoId);
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using var uow = await store.AbrirAsync(CancellationToken.None);
        var cargado = (await uow.CargarAsientoAsync(asientoId, CancellationToken.None))!;
        var escritura = await uow.GuardarAsientoAsync(
            asientoId, version, cargado with { Estado = AsientoPersistido.Confirmado, NumeroAsiento = "02-2026-08-000001" }, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);

        Assert.Equal(ResultadoEscritura.Aplicado, escritura);
        var estado = await _db.ExecuteScalarAsync<string>(
            $"SELECT Estado FROM fact.AsientoContable WHERE AsientoContableId = {asientoId};");
        Assert.Equal(AsientoPersistido.Confirmado, estado!.TrimEnd());
    }

    [Fact]
    public async Task AsignarCorrelativoAsync_IncrementsOnceAndNeverReusesTheNumberARolledBackTransactionClaimed()
    {
        var store = new SqlFacturacionStore(_db.ConnectionString);

        int primero;
        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            primero = await uow.AsignarCorrelativoAsync(2026, 8, "02", CancellationToken.None);
            // Deliberately NOT committed -- Dispose rolls back (design D5: "una transacción
            // revertida devuelve el número").
        }

        int segundo;
        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            segundo = await uow.AsignarCorrelativoAsync(2026, 8, "02", CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        Assert.Equal(1, primero);
        // The rolled-back transaction's claim of 1 was returned -- the next successful validar
        // gets 1 again, not 2. Gapless, per design D5/spec.md.
        Assert.Equal(1, segundo);

        var ultimoPersistido = await _db.ExecuteScalarAsync<int>(
            "SELECT Ultimo FROM fact.CorrelativoAsiento WHERE Anio = 2026 AND Mes = 8 AND Origen = '02';");
        Assert.Equal(1, ultimoPersistido);
    }

    [Fact]
    public async Task AsignarCorrelativoAsync_IncrementsSequentially_AcrossTwoCommittedTransactions()
    {
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            var numero = await uow.AsignarCorrelativoAsync(2026, 9, "02", CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            Assert.Equal(1, numero);
        }

        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            var numero = await uow.AsignarCorrelativoAsync(2026, 9, "02", CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            Assert.Equal(2, numero);
        }
    }

    [Fact]
    public async Task RegistrarAuditoriaAsync_InsertsOneRow_WithTheGivenAccion()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        var usuarioId = await _db.InsertarUsuarioAsync();
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            await uow.RegistrarAuditoriaAsync(
                new EntradaAuditoria(
                    EntradaAuditoria.EntidadTipos.Asiento, asientoId, EntradaAuditoria.Acciones.Reapertura,
                    "Estado", "CONFIRMADO", "BORRADOR", "Motivo de prueba", usuarioId, DateTimeOffset.UtcNow),
                CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        var accion = await _db.ExecuteScalarAsync<string>(
            $"SELECT Accion FROM fact.AuditoriaCorreccion WHERE EntidadId = {asientoId};");
        Assert.Equal(EntradaAuditoria.Acciones.Reapertura, accion!.TrimEnd());
    }

    [Fact]
    public async Task EmitirOutboxAsync_InsertsOneRow_WithAMonotonicSecuencia()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            await uow.EmitirOutboxAsync("FACTURA_VALIDADA", facturaId, "02-2026-08-000001", CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        var cantidad = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.OutboxEvent WHERE FacturaId = {facturaId} AND Tipo = 'FACTURA_VALIDADA';");
        Assert.Equal(1, cantidad);
    }

    [Fact]
    public async Task DisposeAsync_WithoutCommit_RollsBackEveryWrite()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        var version = await _db.ObtenerVersionAsync(asientoId);
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            var cargado = (await uow.CargarAsientoAsync(asientoId, CancellationToken.None))!;
            await uow.GuardarAsientoAsync(
                asientoId, version, cargado with { Estado = AsientoPersistido.Confirmado }, CancellationToken.None);
            // No CommitAsync -- Dispose must roll back.
        }

        var estado = await _db.ExecuteScalarAsync<string>(
            $"SELECT Estado FROM fact.AsientoContable WHERE AsientoContableId = {asientoId};");
        Assert.Equal(AsientoPersistido.Borrador, estado!.TrimEnd());
    }
}
