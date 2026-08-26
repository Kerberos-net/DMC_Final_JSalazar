using SmartNet.Db.TestBootstrap;
using SmartNet.TiposCambio.Core;

namespace SmartNet.TiposCambio.Infrastructure.Tests;

/// <summary>
/// Tasks 2.2/2.3 -- <see cref="SqlTipoCambioRepository.ObtenerVigenteAsync"/> against a real,
/// migrated <c>fact_test_&lt;id&gt;</c> database. <c>fact.TipoCambio</c> is created by
/// <c>007_publicacion.sql</c>. Per design.md Decision 1, the adapter SELECTs both origin rows by
/// PK (max 2 rows) and delegates the SBS&gt;MANUAL priority to
/// <see cref="SeleccionDeTipoCambio.Seleccionar"/> -- no ORDER BY/CASE priority logic in SQL.
/// </summary>
public sealed class SqlTipoCambioRepositoryTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;

    public async Task InitializeAsync() => _db = await MigratedDatabase();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    internal static async Task<TestDatabaseFixture> MigratedDatabase()
    {
        var db = await TestDatabaseFixture.CreateAsync();
        try
        {
            await db.CreateWithoutLoginUserAsync("usr_api");
            await db.CreateWithoutLoginUserAsync("usr_worker");
            await db.CreateExternalDboCatalogsAsync();
            await db.SeedDboMotivoFixtureRowsAsync();
            var exitCode = db.RunMigrations();
            Assert.Equal(0, exitCode);
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    [Fact]
    public async Task ObtenerVigenteAsync_ReturnsVigenteWithOrigenSbs_WhenOnlyAnSbsRowExists()
    {
        var fecha = new DateOnly(2026, 8, 14);
        await _db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.TipoCambio (Fecha, Origen, Compra, Venta, FechaConsulta)
             VALUES ('{fecha:yyyy-MM-dd}', 'SBS', 3.799, 3.802, '2026-08-14T08:00:00');
             """);
        var sut = new SqlTipoCambioRepository(_db.ConnectionString);

        var resultado = await sut.ObtenerVigenteAsync(fecha, CancellationToken.None);

        var vigente = Assert.IsType<ResultadoTipoCambio.Vigente>(resultado);
        Assert.Equal(OrigenTipoCambio.Sbs, vigente.Valor.Origen);
        Assert.Equal(3.802m, vigente.Valor.Venta);
        Assert.Equal(3.799m, vigente.Valor.Compra);
    }

    [Fact]
    public async Task ObtenerVigenteAsync_ReturnsVigenteWithOrigenSbs_WhenBothOriginsExist()
    {
        var fecha = new DateOnly(2026, 8, 14);
        await _db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.TipoCambio (Fecha, Origen, Compra, Venta, FechaConsulta) VALUES
                 ('{fecha:yyyy-MM-dd}', 'SBS', 3.799, 3.802, '2026-08-14T08:00:00'),
                 ('{fecha:yyyy-MM-dd}', 'MANUAL', 3.700, 3.750, '2026-08-14T07:00:00');
             """);
        var sut = new SqlTipoCambioRepository(_db.ConnectionString);

        var resultado = await sut.ObtenerVigenteAsync(fecha, CancellationToken.None);

        var vigente = Assert.IsType<ResultadoTipoCambio.Vigente>(resultado);
        Assert.Equal(OrigenTipoCambio.Sbs, vigente.Valor.Origen);
        Assert.Equal(3.802m, vigente.Valor.Venta);
    }

    [Fact]
    public async Task ObtenerVigenteAsync_ReturnsSinTipoCambio_WhenNoRowExistsForTheDate()
    {
        var fecha = new DateOnly(2026, 8, 16);
        var sut = new SqlTipoCambioRepository(_db.ConnectionString);

        var resultado = await sut.ObtenerVigenteAsync(fecha, CancellationToken.None);

        var sinTipoCambio = Assert.IsType<ResultadoTipoCambio.SinTipoCambio>(resultado);
        Assert.Equal(fecha, sinTipoCambio.Fecha);
    }

    // Tasks 2.4/2.5 -- design.md Decision 3: the PK enforces duplicate MANUAL loads, the adapter
    // only translates SqlException 2627/2601 into ResultadoCargaManual.YaExistia (a real composite
    // PK conflict, not a pre-check that would open a TOCTOU window).
    [Fact]
    public async Task CargarManualAsync_InsertsAManualRow_ForADateNotYetCovered_AndReturnsCargada()
    {
        var fecha = new DateOnly(2026, 8, 15);
        var fechaConsulta = new DateTime(2026, 8, 15, 9, 0, 0, DateTimeKind.Utc);
        var sut = new SqlTipoCambioRepository(_db.ConnectionString);

        var resultado = await sut.CargarManualAsync(fecha, 3.80m, 3.85m, fechaConsulta, cargadoPorUsuarioId: null, CancellationToken.None);

        Assert.Equal(ResultadoCargaManual.Cargada, resultado);
        var lookup = await sut.ObtenerVigenteAsync(fecha, CancellationToken.None);
        var vigente = Assert.IsType<ResultadoTipoCambio.Vigente>(lookup);
        Assert.Equal(OrigenTipoCambio.Manual, vigente.Valor.Origen);
        Assert.Equal(3.85m, vigente.Valor.Venta);
        Assert.Equal(3.80m, vigente.Valor.Compra);
    }

    [Fact]
    public async Task CargarManualAsync_ReturnsYaExistia_ForASecondManualLoadOfTheSameDate()
    {
        var fecha = new DateOnly(2026, 8, 15);
        var fechaConsulta = new DateTime(2026, 8, 15, 9, 0, 0, DateTimeKind.Utc);
        var sut = new SqlTipoCambioRepository(_db.ConnectionString);
        var primero = await sut.CargarManualAsync(fecha, 3.80m, 3.85m, fechaConsulta, cargadoPorUsuarioId: null, CancellationToken.None);
        Assert.Equal(ResultadoCargaManual.Cargada, primero);

        var segundo = await sut.CargarManualAsync(fecha, 3.81m, 3.86m, fechaConsulta, cargadoPorUsuarioId: null, CancellationToken.None);

        Assert.Equal(ResultadoCargaManual.YaExistia, segundo);
        var count = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.TipoCambio WHERE Fecha = '{fecha:yyyy-MM-dd}' AND Origen = 'MANUAL';");
        Assert.Equal(1, count);
    }
}
