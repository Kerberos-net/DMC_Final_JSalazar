using SmartNet.Contable.Core;
using SmartNet.Db.TestBootstrap;
using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Infrastructure.Tests;

/// <summary>
/// tasks.md Phase 3 (PR 3) — el contrato de líneas por <c>LineaId</c> de <see cref="IUnidadDeTrabajo"/>
/// (spec.md api-asientos: "never position") contra una base real migrada: CAS de encabezado
/// (<c>fact.AsientoContable.Version</c> se toca aunque la fila cambiada sea de
/// <c>fact.AsientoContableDetalle</c>), y que el <c>LineaId</c> sobrevive reorder/delete.
/// </summary>
public sealed class SqlUnidadDeTrabajoAsientoLineasTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;

    public async Task InitializeAsync() => _db = await FacturacionTestDatabaseFixtureHelper.MigratedDatabaseAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static readonly LineaAsiento LineaValida = new(
        Orden: 1, Bloque: Bloque.Principal, Tipo: TipoLinea.D, Debe: 100m, Haber: 0m,
        CuentaCodigo: "639915", CuentaDescripcion: null, CtaReflejaCodigo: null, CtaPuenteCodigo: null);

    [Fact]
    public async Task AgregarLineaAsync_WithAMatchingVersion_InsertsTheRow_AndBumpsTheHeaderVersion()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        var versionOriginal = await _db.ObtenerVersionAsync(asientoId);
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using var uow = await store.AbrirAsync(CancellationToken.None);
        var resultado = await uow.AgregarLineaAsync(asientoId, versionOriginal, LineaValida, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);

        Assert.Equal(ResultadoEscritura.Aplicado, resultado.Resultado);
        Assert.NotNull(resultado.LineaId);
        var cantidad = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.AsientoContableDetalle WHERE LineaId = {resultado.LineaId};");
        Assert.Equal(1, cantidad);
        var versionNueva = await _db.ObtenerVersionAsync(asientoId);
        Assert.NotEqual(Convert.ToBase64String(versionOriginal), Convert.ToBase64String(versionNueva));
    }

    [Fact]
    public async Task AgregarLineaAsync_WithAStaleVersion_ReturnsVersionEnConflicto_AndInsertsNothing()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using var uow = await store.AbrirAsync(CancellationToken.None);
        var resultado = await uow.AgregarLineaAsync(
            asientoId, new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }, LineaValida, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);

        Assert.Equal(ResultadoEscritura.VersionEnConflicto, resultado.Resultado);
        Assert.Null(resultado.LineaId);
        var cantidad = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.AsientoContableDetalle WHERE AsientoContableId = {asientoId};");
        Assert.Equal(0, cantidad);
    }

    [Fact]
    public async Task LineaId_SurvivesReorderAndDelete_UnlikeOrden()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        var store = new SqlFacturacionStore(_db.ConnectionString);

        long primeraLineaId, segundaLineaId;
        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            var version = await _db.ObtenerVersionAsync(asientoId);
            var primero = await uow.AgregarLineaAsync(asientoId, version, LineaValida, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            primeraLineaId = primero.LineaId!.Value;
        }

        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            var version = await _db.ObtenerVersionAsync(asientoId);
            var segundo = await uow.AgregarLineaAsync(
                asientoId, version, LineaValida with { Orden = 2, CuentaCodigo = "401111" }, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            segundaLineaId = segundo.LineaId!.Value;
        }

        // Delete the FIRST línea; the second's LineaId must stay valid and addressable.
        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            var version = await _db.ObtenerVersionAsync(asientoId);
            var eliminacion = await uow.EliminarLineaAsync(primeraLineaId, asientoId, version, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            Assert.Equal(ResultadoEscritura.Aplicado, eliminacion);
        }

        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            var version = await _db.ObtenerVersionAsync(asientoId);
            var actualizacion = await uow.ActualizarLineaAsync(
                segundaLineaId, asientoId, version, LineaValida with { CuentaCodigo = "421001" }, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            Assert.Equal(ResultadoEscritura.Aplicado, actualizacion);
        }

        var cuentaFinal = await _db.ExecuteScalarAsync<string>(
            $"SELECT CuentaCodigo FROM fact.AsientoContableDetalle WHERE LineaId = {segundaLineaId};");
        Assert.Equal("421001", cuentaFinal!.TrimEnd());
    }

    [Fact]
    public async Task ActualizarLineaAsync_WhenLineaDoesNotExist_ReturnsNoEncontrado()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        var version = await _db.ObtenerVersionAsync(asientoId);
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using var uow = await store.AbrirAsync(CancellationToken.None);
        var resultado = await uow.ActualizarLineaAsync(999_999, asientoId, version, LineaValida, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);

        Assert.Equal(ResultadoEscritura.NoEncontrado, resultado);
    }

    [Fact]
    public async Task CargarLineasPersistidasAsync_ReturnsEachRowWithItsStableLineaId()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        var version = await _db.ObtenerVersionAsync(asientoId);
        var store = new SqlFacturacionStore(_db.ConnectionString);

        long lineaId;
        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            var resultado = await uow.AgregarLineaAsync(asientoId, version, LineaValida, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            lineaId = resultado.LineaId!.Value;
        }

        await using var segundoUow = await store.AbrirAsync(CancellationToken.None);
        var lineas = await segundoUow.CargarLineasPersistidasAsync(asientoId, CancellationToken.None);

        var linea = Assert.Single(lineas);
        Assert.Equal(lineaId, linea.LineaId);
        Assert.Equal("639915", linea.Linea.CuentaCodigo);
    }
}
