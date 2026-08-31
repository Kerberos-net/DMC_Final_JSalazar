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

    // --- diseno-visual-spa-item-12 (design D9): las 4 columnas indicadoras, ya persistidas por
    // fact.Factura/#13, ahora proyectadas por CargarFacturaAsync. ---

    [Fact]
    public async Task CargarFacturaAsync_RoundTripsTheFourIndicatorColumns()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        await _db.ExecuteNonQueryAsync(
            $"""
             UPDATE fact.Factura
             SET EsProveedorGenerico = 1, PosibleDuplicado = 1, TieneCamposNoExtraidos = 1, AfectacionMixta = NULL
             WHERE FacturaId = {facturaId};
             """);
        var store = new SqlFacturacionStore(_db.ConnectionString);
        await using var uow = await store.AbrirAsync(CancellationToken.None);

        var cargada = await uow.CargarFacturaAsync(facturaId, CancellationToken.None);

        Assert.NotNull(cargada);
        Assert.True(cargada!.EsProveedorGenerico);
        Assert.True(cargada.PosibleDuplicado);
        Assert.True(cargada.TieneCamposNoExtraidos);
        Assert.Null(cargada.AfectacionMixta);
    }

    [Fact]
    public async Task CargarFacturaAsync_RoundTripsAfectacionMixta_WhenVerifiedFalse()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        await _db.ExecuteNonQueryAsync(
            $"UPDATE fact.Factura SET AfectacionMixta = 0 WHERE FacturaId = {facturaId};");
        var store = new SqlFacturacionStore(_db.ConnectionString);
        await using var uow = await store.AbrirAsync(CancellationToken.None);

        var cargada = await uow.CargarFacturaAsync(facturaId, CancellationToken.None);

        Assert.NotNull(cargada);
        Assert.False(cargada!.EsProveedorGenerico);
        Assert.False(cargada.PosibleDuplicado);
        Assert.False(cargada.TieneCamposNoExtraidos);
        Assert.False(cargada.AfectacionMixta);
    }

    [Fact]
    public async Task GuardarFacturaAsync_ViaPatch_DoesNotClobberTheFourIndicatorColumns()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        await _db.ExecuteNonQueryAsync(
            $"""
             UPDATE fact.Factura
             SET EsProveedorGenerico = 1, PosibleDuplicado = 1, TieneCamposNoExtraidos = 1, AfectacionMixta = 1
             WHERE FacturaId = {facturaId};
             """);
        var version = await _db.ObtenerVersionFacturaAsync(facturaId);
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            var cargada = (await uow.CargarFacturaAsync(facturaId, CancellationToken.None))!;
            var escritura = await uow.GuardarFacturaAsync(
                facturaId, version, cargada with { RucProveedor = "20999999999" }, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            Assert.Equal(ResultadoEscritura.Aplicado, escritura);
        }

        await using var segundoUow = await store.AbrirAsync(CancellationToken.None);
        var releida = await segundoUow.CargarFacturaAsync(facturaId, CancellationToken.None);

        Assert.NotNull(releida);
        Assert.True(releida!.EsProveedorGenerico);
        Assert.True(releida.PosibleDuplicado);
        Assert.True(releida.TieneCamposNoExtraidos);
        Assert.True(releida.AfectacionMixta);
        Assert.Equal("20999999999", releida.RucProveedor!.TrimEnd());
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

    // --- BACKLOG #19 (design D1/D4/D6/D8, tasks 3.11) — columnas contables + puertos nuevos ---

    [Fact]
    public async Task CargarFacturaAsync_RoundTripsIgvOrigGlosaAndCamposNoExtraidos()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        await _db.ExecuteNonQueryAsync(
            $"""
             UPDATE fact.Factura
             SET IgvOrig = 18.00, Glosa = 'Compra de utiles', CamposNoExtraidos = 'numero,igv'
             WHERE FacturaId = {facturaId};
             """);
        var store = new SqlFacturacionStore(_db.ConnectionString);
        await using var uow = await store.AbrirAsync(CancellationToken.None);

        var cargada = await uow.CargarFacturaAsync(facturaId, CancellationToken.None);

        Assert.NotNull(cargada);
        Assert.Equal(18.00m, cargada!.IgvOrig);
        Assert.Equal("Compra de utiles", cargada.Glosa);
        Assert.Equal(new[] { "numero", "igv" }, cargada.CamposNoExtraidos);
    }

    [Fact]
    public async Task CargarFacturaAsync_LeavesCamposNoExtraidosNull_ForAPre021Factura()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var store = new SqlFacturacionStore(_db.ConnectionString);
        await using var uow = await store.AbrirAsync(CancellationToken.None);

        var cargada = await uow.CargarFacturaAsync(facturaId, CancellationToken.None);

        Assert.NotNull(cargada);
        Assert.Null(cargada!.IgvOrig);
        Assert.Null(cargada.Glosa);
        Assert.Null(cargada.CamposNoExtraidos);
    }

    [Fact]
    public async Task GuardarFacturaAsync_PersistsIgvOrigAndGlosa_ButNeverMutatesCamposNoExtraidos()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        await _db.ExecuteNonQueryAsync(
            $"UPDATE fact.Factura SET CamposNoExtraidos = 'numero' WHERE FacturaId = {facturaId};");
        var version = await _db.ObtenerVersionFacturaAsync(facturaId);
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            var cargada = (await uow.CargarFacturaAsync(facturaId, CancellationToken.None))!;
            var escritura = await uow.GuardarFacturaAsync(
                facturaId, version, cargada with { IgvOrig = 20.00m, Glosa = "glosa nueva", TotalOrig = 131.00m },
                CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            Assert.Equal(ResultadoEscritura.Aplicado, escritura);
        }

        var igvOrig = await _db.ExecuteScalarAsync<decimal?>($"SELECT IgvOrig FROM fact.Factura WHERE FacturaId = {facturaId};");
        var glosa = await _db.ExecuteScalarAsync<string>($"SELECT Glosa FROM fact.Factura WHERE FacturaId = {facturaId};");
        var campos = await _db.ExecuteScalarAsync<string>($"SELECT CamposNoExtraidos FROM fact.Factura WHERE FacturaId = {facturaId};");
        Assert.Equal(20.00m, igvOrig);
        Assert.Equal("glosa nueva", glosa!.TrimEnd());
        Assert.Equal("numero", campos!.TrimEnd());
    }

    [Fact]
    public async Task ExisteIdentidadPreviaAsync_IsTrue_WhenAnotherNonDescartadaFacturaSharesTheTriple()
    {
        var original = await _db.InsertarFacturaAsync(numero: "F001-1", rucProveedor: "20100000001");
        var otra = await _db.InsertarFacturaAsync(numero: "F001-1", rucProveedor: "20100000001");
        var store = new SqlFacturacionStore(_db.ConnectionString);
        await using var uow = await store.AbrirAsync(CancellationToken.None);

        Assert.True(await uow.ExisteIdentidadPreviaAsync(original, "20100000001", "01", "F001-1", CancellationToken.None));
        Assert.False(await uow.ExisteIdentidadPreviaAsync(original, "20100000001", "01", "F001-DISTINTO", CancellationToken.None));

        await _db.ExecuteNonQueryAsync($"UPDATE fact.Factura SET Estado = 'DESCARTADA' WHERE FacturaId = {otra};");
        Assert.False(await uow.ExisteIdentidadPreviaAsync(original, "20100000001", "01", "F001-1", CancellationToken.None));
    }

    [Fact]
    public async Task ActualizarPosibleDuplicadoAsync_WritesTheIndicator_WithoutTouchingVersionSemantics()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            await uow.ActualizarPosibleDuplicadoAsync(facturaId, true, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }

        var valor = await _db.ExecuteScalarAsync<bool>($"SELECT PosibleDuplicado FROM fact.Factura WHERE FacturaId = {facturaId};");
        Assert.True(valor);
    }

    [Fact]
    public async Task ActualizarProyeccionEscalarAsync_WritesTheThreeScalars_OntoABorradorAsiento()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using (var uow = await store.AbrirAsync(CancellationToken.None))
        {
            var escritura = await uow.ActualizarProyeccionEscalarAsync(asientoId, 1000m, 180m, 1180m, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            Assert.Equal(ResultadoEscritura.Aplicado, escritura);
        }

        var (basePen, igvPen, netoPen) = (
            await _db.ExecuteScalarAsync<decimal>($"SELECT BasePEN FROM fact.AsientoContable WHERE AsientoContableId = {asientoId};"),
            await _db.ExecuteScalarAsync<decimal>($"SELECT IgvPEN FROM fact.AsientoContable WHERE AsientoContableId = {asientoId};"),
            await _db.ExecuteScalarAsync<decimal>($"SELECT NetoPEN FROM fact.AsientoContable WHERE AsientoContableId = {asientoId};"));
        Assert.Equal(1000m, basePen);
        Assert.Equal(180m, igvPen);
        Assert.Equal(1180m, netoPen);
    }

    [Fact]
    public async Task ActualizarProyeccionEscalarAsync_ReturnsNoEncontrado_WhenTheAsientoIsNoLongerBorrador()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        await _db.ExecuteNonQueryAsync(
            $"UPDATE fact.AsientoContable SET Estado = 'CONFIRMADO', NumeroAsiento = '02-2026-08-000001' WHERE AsientoContableId = {asientoId};");
        var store = new SqlFacturacionStore(_db.ConnectionString);

        await using var uow = await store.AbrirAsync(CancellationToken.None);
        var escritura = await uow.ActualizarProyeccionEscalarAsync(asientoId, 1m, 0m, 1m, CancellationToken.None);

        Assert.Equal(ResultadoEscritura.NoEncontrado, escritura);
    }

    // --- BACKLOG #19 (design correction 2 / REGLAS.md §6, tasks 3.12/3.13) — narrowing de SinTipoCambio ---

    [Fact]
    public async Task CargarAsientoAsync_FlagsSinTipoCambio_ForAForeignNonNcFacturaWithNoRate()
    {
        var facturaId = await _db.InsertarFacturaAsync(moneda: "USD");
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        var store = new SqlFacturacionStore(_db.ConnectionString);
        await using var uow = await store.AbrirAsync(CancellationToken.None);

        var asiento = await uow.CargarAsientoAsync(asientoId, CancellationToken.None);

        Assert.True(asiento!.Hechos.SinTipoCambio);
    }

    [Fact]
    public async Task CargarAsientoAsync_DoesNotFlagSinTipoCambio_ForANc07WithAnInternalReference()
    {
        var referida = await _db.InsertarFacturaAsync(numero: "F001-ORIG");
        var ncId = await _db.InsertarFacturaAsync(tipoComprobante: "07", numero: "FC01-1", moneda: "USD");
        await _db.ExecuteNonQueryAsync(
            $"UPDATE fact.Factura SET EsReferenciaExterna = 0, FacturaReferenciaId = {referida} WHERE FacturaId = {ncId};");
        var asientoId = await _db.InsertarAsientoBorradorAsync(ncId);
        var store = new SqlFacturacionStore(_db.ConnectionString);
        await using var uow = await store.AbrirAsync(CancellationToken.None);

        var asiento = await uow.CargarAsientoAsync(asientoId, CancellationToken.None);

        Assert.False(asiento!.Hechos.SinTipoCambio);
    }

    [Fact]
    public async Task CargarAsientoAsync_StillFlagsSinTipoCambio_ForANc07WithAnExternalReference()
    {
        var ncId = await _db.InsertarFacturaAsync(tipoComprobante: "07", numero: "FC01-2", moneda: "USD");
        await _db.ExecuteNonQueryAsync(
            $"UPDATE fact.Factura SET EsReferenciaExterna = 1, FacturaReferenciaId = NULL WHERE FacturaId = {ncId};");
        var asientoId = await _db.InsertarAsientoBorradorAsync(ncId);
        var store = new SqlFacturacionStore(_db.ConnectionString);
        await using var uow = await store.AbrirAsync(CancellationToken.None);

        var asiento = await uow.CargarAsientoAsync(asientoId, CancellationToken.None);

        Assert.True(asiento!.Hechos.SinTipoCambio);
    }

    [Fact]
    public async Task CargarAsientoAsync_NeverFlagsSinTipoCambio_ForAPenFactura()
    {
        var facturaId = await _db.InsertarFacturaAsync(moneda: "PEN");
        var asientoId = await _db.InsertarAsientoBorradorAsync(facturaId);
        var store = new SqlFacturacionStore(_db.ConnectionString);
        await using var uow = await store.AbrirAsync(CancellationToken.None);

        var asiento = await uow.CargarAsientoAsync(asientoId, CancellationToken.None);

        Assert.False(asiento!.Hechos.SinTipoCambio);
    }

    // --- BACKLOG #19 (design D4 §7 CONSEQUENCE / owner ACCEPTED, task 3.15) ---
    // Poblar fact.AsientoContable.BasePEN via el PATCH contable DES-VACIA la invariante §7 "los
    // cargos 6x/1x suman la base imponible": una factura cuyo asiento tenia lineas hand-built que
    // cuadraban con la base ORIGINAL puede EMPEZAR a fallar `validar` tras editar la base. Correcto
    // por REGLAS.md, pero es un cambio de comportamiento en vivo, no un no-op.

    [Fact]
    public async Task PatchThenValidar_PopulatingBasePen_MakesValidarRejectAnInvoiceWhoseHandBuiltLineasNoLongerMatch()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var usuarioId = await _db.InsertarUsuarioAsync();
        await _db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.AsientoContable
                 (FacturaId, OrigenLibro, ProveedorCodigo, FechaContable, BasePEN, IgvPEN, NetoPEN, Estado)
             VALUES ({facturaId}, '02', 'P00123', '2026-08-10', 100.00, 18.00, 118.00, 'BORRADOR');
             """);
        var asientoId = await _db.ExecuteScalarAsync<long>("SELECT MAX(AsientoContableId) FROM fact.AsientoContable;");
        await _db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.AsientoContableDetalle (AsientoContableId, Orden, Bloque, Tipo, Debe, Haber, CuentaCodigo)
             VALUES ({asientoId}, 1, 'PRINCIPAL', 'D', 100.00, 0, '639915'),
                    ({asientoId}, 2, 'PRINCIPAL', 'D', 18.00, 0, '401111'),
                    ({asientoId}, 3, 'PRINCIPAL', 'H', 0, 118.00, '421001');
             """);
        var version = await _db.ObtenerVersionFacturaAsync(facturaId);
        var servicio = new ServicioDeFacturas(new SqlFacturacionStore(_db.ConnectionString));
        var corte = new DateOnly(2026, 8, 1);
        var ahora = new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

        // ANTES del PATCH: `validar` aprueba (BasePEN 100 == cargos 6x 100).
        var antes = await servicio.ValidarPorFacturaAsync(facturaId, corte, ahora, usuarioId, CancellationToken.None);
        Assert.IsType<ResultadoComando.Aplicado>(antes);

        // Reabrimos el asiento a BORRADOR para poder re-editar la base (el PATCH exige
        // PENDIENTE_VALIDACION en la factura; la revalidacion posterior es la que expone §7).
        await _db.ExecuteNonQueryAsync(
            $"UPDATE fact.AsientoContable SET Estado = 'BORRADOR' WHERE AsientoContableId = {asientoId};");
        await _db.ExecuteNonQueryAsync(
            $"UPDATE fact.Factura SET Estado = 'PENDIENTE_VALIDACION' WHERE FacturaId = {facturaId};");
        version = await _db.ObtenerVersionFacturaAsync(facturaId);

        var patch = await servicio.PatchAsync(
            facturaId, version, new CorreccionFactura(BaseImponible: 1000m, Igv: 180m), usuarioId, ahora, CancellationToken.None);
        Assert.IsType<ResultadoComando.Aplicado>(patch);

        var basePenTrasPatch = await _db.ExecuteScalarAsync<decimal>(
            $"SELECT BasePEN FROM fact.AsientoContable WHERE AsientoContableId = {asientoId};");
        Assert.Equal(1000m, basePenTrasPatch);

        // DESPUES del PATCH: `validar` RECHAZA — las lineas hand-built (cargos 6x = 100) ya no
        // cuadran con la base editada (1000). Invariante §7 ahora NO es vacia.
        var despues = await servicio.ValidarPorFacturaAsync(facturaId, corte, ahora, usuarioId, CancellationToken.None);
        Assert.IsType<ResultadoComando.InvariantesIncumplidas>(despues);
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
