using SmartNet.Contable.Core;
using SmartNet.Db.TestBootstrap;
using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Infrastructure.Tests;

/// <summary>
/// BACKLOG #24 (design A1/A3/B1/B2) — <see cref="SqlUnidadDeTrabajo"/> contra una base real migrada:
/// <see cref="IUnidadDeTrabajo.ResolverHechosDeComposicionAsync"/> lee
/// <c>fact.ProveedorAtributo</c> / <c>dbo.Motivo</c> / el TC venta vigente;
/// <see cref="IUnidadDeTrabajo.CrearAsientoBorradorAsync"/> persiste encabezado + N líneas;
/// <see cref="IUnidadDeTrabajo.ReemplazarLineasAsync"/> borra + reinserta bajo el CAS de encabezado.
/// </summary>
public sealed class SqlUnidadDeTrabajoSembradoTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;

    public async Task InitializeAsync() => _db = await FacturacionTestDatabaseFixtureHelper.MigratedDatabaseAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static AsientoContable Asiento(
        string? motivo = "Movilidad",
        decimal? tipoCambioVenta = null,
        decimal basePen = 100m,
        decimal igvPen = 18m,
        params LineaAsiento[] lineas) =>
        new(
            ProveedorCodigo: "P00123",
            FechaContable: new DateOnly(2026, 8, 10),
            MotivoDescripcion: motivo,
            TipoCambioVenta: tipoCambioVenta,
            BasePEN: basePen,
            IgvPEN: igvPen,
            NetoPEN: basePen + igvPen,
            AfectacionCongelada: Afectacion.Gravada,
            Comprobante: TipoComprobante.Factura,
            Lineas: lineas);

    private static LineaAsiento Cargo(short orden, decimal debe, string? cuenta) =>
        new(orden, Bloque.Principal, TipoLinea.D, debe, 0m, cuenta, null, null, null);

    private static LineaAsiento Abono(short orden, decimal haber, string? cuenta) =>
        new(orden, Bloque.Principal, TipoLinea.H, 0m, haber, cuenta, null, null, null);

    [Fact]
    public async Task CrearAsientoBorradorAsync_PersistsHeaderScalars_AndEveryLine()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var store = new SqlFacturacionStore(_db.ConnectionString);

        var asiento = Asiento(
            tipoCambioVenta: 3.789500m, basePen: 1000m, igvPen: 180m,
            lineas: new[] { Cargo(1, 1000m, "631123"), Cargo(2, 180m, "40111"), Abono(3, 1180m, null) });

        long asientoId;
        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            asientoId = await uow.CrearAsientoBorradorAsync(facturaId, asiento, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        string Col(string c) => $"SELECT {c} FROM fact.AsientoContable WHERE AsientoContableId = {asientoId};";

        Assert.Equal("Movilidad", await _db.ExecuteScalarAsync<string>(Col("MotivoDescripcion")));
        Assert.Equal(3.789500m, await _db.ExecuteScalarAsync<decimal>(Col("TipoCambioVenta")));
        Assert.Equal(1000m, await _db.ExecuteScalarAsync<decimal>(Col("BasePEN")));
        Assert.Equal(180m, await _db.ExecuteScalarAsync<decimal>(Col("IgvPEN")));
        Assert.Equal(1180m, await _db.ExecuteScalarAsync<decimal>(Col("NetoPEN")));
        Assert.Equal("BORRADOR", (await _db.ExecuteScalarAsync<string>(Col("Estado")))!.TrimEnd());

        var lineas = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.AsientoContableDetalle WHERE AsientoContableId = {asientoId};");
        Assert.Equal(3, lineas);

        var sinCuenta = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.AsientoContableDetalle WHERE AsientoContableId = {asientoId} AND SinCuenta = 1;");
        Assert.Equal(1, sinCuenta);
    }

    [Fact]
    public async Task ReemplazarLineasAsync_UnderAMatchingVersion_DeletesOldLines_ReinsertsNew_AndReDerivesHeader()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        await _db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.AsientoContableDetalle (AsientoContableId, Orden, Bloque, Tipo, Debe, Haber, SinCuenta)
             VALUES ({asientoId}, 1, 'PRINCIPAL', 'D', 100, 0, 1);
             """);
        var store = new SqlFacturacionStore(_db.ConnectionString);

        var nuevo = Asiento(
            motivo: "Peaje", basePen: 250m, igvPen: 45m,
            lineas: new[] { Cargo(1, 250m, "639915"), Cargo(2, 45m, "40111"), Abono(3, 295m, "42101") });

        ResultadoEscritura resultado;
        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            var version = await _db.ObtenerVersionAsync(asientoId);
            resultado = await uow.ReemplazarLineasAsync(asientoId, version, nuevo, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        Assert.Equal(ResultadoEscritura.Aplicado, resultado);

        string Col(string c) => $"SELECT {c} FROM fact.AsientoContable WHERE AsientoContableId = {asientoId};";
        Assert.Equal("Peaje", await _db.ExecuteScalarAsync<string>(Col("MotivoDescripcion")));
        Assert.Equal(250m, await _db.ExecuteScalarAsync<decimal>(Col("BasePEN")));
        Assert.Equal("BORRADOR", (await _db.ExecuteScalarAsync<string>(Col("Estado")))!.TrimEnd());

        var cuentas = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.AsientoContableDetalle WHERE AsientoContableId = {asientoId} AND CuentaCodigo = '639915';");
        Assert.Equal(1, cuentas);
        var total = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.AsientoContableDetalle WHERE AsientoContableId = {asientoId};");
        Assert.Equal(3, total);
    }

    [Fact]
    public async Task ReemplazarLineasAsync_WithAStaleVersion_ReturnsVersionEnConflicto_AndChangesNothing()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        var store = new SqlFacturacionStore(_db.ConnectionString);

        var nuevo = Asiento(lineas: new[] { Cargo(1, 100m, "639915") });

        await using var uow = await store.AbrirAsync(CancellationToken.None);
        var resultado = await uow.ReemplazarLineasAsync(
            asientoId, new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }, nuevo, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);

        Assert.Equal(ResultadoEscritura.VersionEnConflicto, resultado);
        var lineas = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.AsientoContableDetalle WHERE AsientoContableId = {asientoId};");
        Assert.Equal(0, lineas);
    }

    [Fact]
    public async Task ResolverHechosDeComposicionAsync_ReadsEsRelacionada_AndMotivoDescripcion()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        await _db.ExecuteNonQueryAsync(
            "INSERT INTO fact.ProveedorAtributo (ProveedorCodigo, EsRelacionada) VALUES ('P00123', 1);");
        await _db.ExecuteNonQueryAsync($"UPDATE fact.Factura SET Motivo = 13 WHERE FacturaId = {facturaId};");
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using var uow = await store.AbrirAsync(CancellationToken.None);
        var hechos = await uow.ResolverHechosDeComposicionAsync(facturaId, CancellationToken.None);

        Assert.True(hechos.EsRelacionada);
        Assert.Equal("Movilidad", hechos.MotivoDescripcion);
        Assert.Null(hechos.TipoCambio);
        Assert.Null(hechos.CuentaSugerida);
    }

    [Fact]
    public async Task ResolverHechosDeComposicionAsync_DefaultsEsRelacionadaFalse_AndNullMotivo_WhenAbsent()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using var uow = await store.AbrirAsync(CancellationToken.None);
        var hechos = await uow.ResolverHechosDeComposicionAsync(facturaId, CancellationToken.None);

        Assert.False(hechos.EsRelacionada);
        Assert.Null(hechos.MotivoDescripcion);
    }

    [Fact]
    public async Task ObtenerCuentaContableAsync_ReadsCuentaWithItsReflejoAndPuente_OrNullWhenAbsent()
    {
        await _db.ExecuteNonQueryAsync(
            """
            INSERT INTO dbo.CuentaContable (cuenta, descripcion, nivel, ctarefleja, ctapuente)
            VALUES ('631111', N'FLETE TRASLADO DE MERCADERIA', NULL, '946311', '791111');
            """);
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using var uow = await store.AbrirAsync(CancellationToken.None);
        var cuenta = await uow.ObtenerCuentaContableAsync("631111", CancellationToken.None);
        var ausente = await uow.ObtenerCuentaContableAsync("999999", CancellationToken.None);

        Assert.NotNull(cuenta);
        Assert.Equal("946311", cuenta!.CtaReflejaCodigo);
        Assert.Equal("791111", cuenta.CtaPuenteCodigo);
        Assert.Null(cuenta.Nivel);
        Assert.Null(ausente);
    }

    [Fact]
    public async Task ResolverHechosDeComposicionAsync_ForeignCurrency_FreezesTheVentaRate()
    {
        var facturaId = await _db.InsertarFacturaAsync(moneda: "USD");
        await _db.InsertarTipoCambioAsync(fecha: "2026-08-10", compra: 3.70m, venta: 3.75m);
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using var uow = await store.AbrirAsync(CancellationToken.None);
        var hechos = await uow.ResolverHechosDeComposicionAsync(facturaId, CancellationToken.None);

        Assert.NotNull(hechos.TipoCambio);
        Assert.Equal(3.75m, hechos.TipoCambio!.Venta);
    }
}
