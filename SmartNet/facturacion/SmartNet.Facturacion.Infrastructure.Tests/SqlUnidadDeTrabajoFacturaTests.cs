using SmartNet.Db.TestBootstrap;
using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Infrastructure.Tests;

/// <summary>
/// tasks.md Phase 2 (PR 2) — el contrato factura-shaped de <see cref="IUnidadDeTrabajo"/> (PATCH,
/// abrir, descartar, adjuntos) contra una base real migrada: CAS sobre <c>fact.Factura.Version</c>,
/// <c>UQ_Asiento_Vigente</c>, y el borrado lógico de <c>fact.AdjuntoManual</c>.
/// </summary>
public sealed class SqlUnidadDeTrabajoFacturaTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;

    public async Task InitializeAsync() => _db = await FacturacionTestDatabaseFixtureHelper.MigratedDatabaseAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task CargarFacturaAsync_ReturnsNull_WhenTheFacturaDoesNotExist()
    {
        var store = new SqlFacturacionStore(_db.ConnectionString);
        await using var uow = await store.AbrirAsync(CancellationToken.None);

        var resultado = await uow.CargarFacturaAsync(999_999, CancellationToken.None);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task CargarFacturaAsync_ReturnsTheRow_WithItsVersion()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var store = new SqlFacturacionStore(_db.ConnectionString);
        await using var uow = await store.AbrirAsync(CancellationToken.None);

        var resultado = await uow.CargarFacturaAsync(facturaId, CancellationToken.None);

        Assert.NotNull(resultado);
        Assert.Equal(FacturaPersistida.PendienteValidacion, resultado!.Estado);
        Assert.Equal(8, resultado.Version.Length);
    }

    [Fact]
    public async Task GuardarFacturaAsync_WithAStaleVersion_ReturnsVersionEnConflicto_AndLeavesTheRowUnchanged()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var store = new SqlFacturacionStore(_db.ConnectionString);

        FacturaPersistida cargada;
        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            cargada = (await uow.CargarFacturaAsync(facturaId, CancellationToken.None))!;
        }

        await _db.ExecuteNonQueryAsync(
            $"UPDATE fact.Factura SET RucProveedor = '20999999999' WHERE FacturaId = {facturaId};");

        await using var segundoUow = await store.AbrirAsync(CancellationToken.None);
        var escritura = await segundoUow.GuardarFacturaAsync(
            facturaId, cargada.Version, cargada with { Estado = FacturaPersistida.Descartada }, CancellationToken.None);
        await segundoUow.CommitAsync(CancellationToken.None);

        Assert.Equal(ResultadoEscritura.VersionEnConflicto, escritura);
        var estado = await _db.ExecuteScalarAsync<string>(
            $"SELECT Estado FROM fact.Factura WHERE FacturaId = {facturaId};");
        Assert.Equal(FacturaPersistida.PendienteValidacion, estado!.TrimEnd());
    }

    [Fact]
    public async Task GuardarFacturaAsync_WithAMatchingVersion_AppliesTheWrite()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var version = await _db.ObtenerVersionFacturaAsync(facturaId);
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using var uow = await store.AbrirAsync(CancellationToken.None);
        var cargada = (await uow.CargarFacturaAsync(facturaId, CancellationToken.None))!;
        var escritura = await uow.GuardarFacturaAsync(
            facturaId, version, cargada with { Estado = FacturaPersistida.Descartada }, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);

        Assert.Equal(ResultadoEscritura.Aplicado, escritura);
        var estado = await _db.ExecuteScalarAsync<string>(
            $"SELECT Estado FROM fact.Factura WHERE FacturaId = {facturaId};");
        Assert.Equal(FacturaPersistida.Descartada, estado!.TrimEnd());
    }

    [Fact]
    public async Task ObtenerAsientoVigenteIdAsync_ReturnsNull_WhenTheFacturaHasNoAsiento()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var store = new SqlFacturacionStore(_db.ConnectionString);
        await using var uow = await store.AbrirAsync(CancellationToken.None);

        var resultado = await uow.ObtenerAsientoVigenteIdAsync(facturaId, CancellationToken.None);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task ObtenerAsientoVigenteIdAsync_ReturnsTheBorradorAsiento_WhenOneExists()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        var store = new SqlFacturacionStore(_db.ConnectionString);
        await using var uow = await store.AbrirAsync(CancellationToken.None);

        var resultado = await uow.ObtenerAsientoVigenteIdAsync(facturaId, CancellationToken.None);

        Assert.Equal(asientoId, resultado);
    }

    [Fact]
    public async Task ObtenerAsientoVigenteIdAsync_IgnoresAnAnuladoAsiento()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        await _db.ExecuteNonQueryAsync(
            $"UPDATE fact.AsientoContable SET Estado = 'ANULADO' WHERE AsientoContableId = {asientoId};");
        var store = new SqlFacturacionStore(_db.ConnectionString);
        await using var uow = await store.AbrirAsync(CancellationToken.None);

        var resultado = await uow.ObtenerAsientoVigenteIdAsync(facturaId, CancellationToken.None);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task CrearAsientoBorradorAsync_InsertsAHeaderRow_InBorrador()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var store = new SqlFacturacionStore(_db.ConnectionString);

        long asientoId;
        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            asientoId = await uow.CrearAsientoBorradorAsync(facturaId, "P00123", new DateOnly(2026, 8, 10), CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        var estado = await _db.ExecuteScalarAsync<string>(
            $"SELECT Estado FROM fact.AsientoContable WHERE AsientoContableId = {asientoId};");
        Assert.Equal("BORRADOR", estado!.TrimEnd());
    }

    [Fact]
    public async Task RegistrarAdjuntoAsync_ThenEliminarAdjuntoAsync_SoftDeletesTheRow()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var usuarioId = await _db.InsertarUsuarioAsync();
        var store = new SqlFacturacionStore(_db.ConnectionString);

        long adjuntoId;
        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            adjuntoId = await uow.RegistrarAdjuntoAsync(
                new AdjuntoManual(0, facturaId, "f.pdf", "/adjuntos/f.pdf", "application/pdf", 10, usuarioId, DateTimeOffset.UtcNow, null),
                CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            var escritura = await uow.EliminarAdjuntoAsync(
                adjuntoId, facturaId, DateTimeOffset.UtcNow, usuarioId, "Adjuntado por error", CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            Assert.Equal(ResultadoEscritura.Aplicado, escritura);
        }

        var eliminadoEn = await _db.ExecuteScalarAsync<DateTime?>(
            $"SELECT EliminadoEn FROM fact.AdjuntoManual WHERE AdjuntoManualId = {adjuntoId};");
        Assert.NotNull(eliminadoEn);
    }

    [Fact]
    public async Task EliminarAdjuntoAsync_WhenAlreadyDeleted_ReturnsNoEncontrado_AndDoesNotDeleteTwice()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var usuarioId = await _db.InsertarUsuarioAsync();
        var store = new SqlFacturacionStore(_db.ConnectionString);

        long adjuntoId;
        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            adjuntoId = await uow.RegistrarAdjuntoAsync(
                new AdjuntoManual(0, facturaId, "f.pdf", "/adjuntos/f.pdf", "application/pdf", 10, usuarioId, DateTimeOffset.UtcNow, null),
                CancellationToken.None);
            await uow.EliminarAdjuntoAsync(adjuntoId, facturaId, DateTimeOffset.UtcNow, usuarioId, "Motivo 1", CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        await using var segundoUow = await store.AbrirAsync(CancellationToken.None);
        var segundaEscritura = await segundoUow.EliminarAdjuntoAsync(
            adjuntoId, facturaId, DateTimeOffset.UtcNow, usuarioId, "Motivo 2", CancellationToken.None);
        await segundoUow.CommitAsync(CancellationToken.None);

        Assert.Equal(ResultadoEscritura.NoEncontrado, segundaEscritura);
    }
}
