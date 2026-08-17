using SmartNet.Db.TestBootstrap;

namespace SmartNet.Catalogos.Infrastructure.Tests;

/// <summary>
/// Tasks 3.5-3.8 -- <see cref="SqlSugerenciaCuentaRepository"/> against a real, migrated
/// <c>fact_test_&lt;id&gt;</c> database. <c>fact.SugerenciaCuenta</c> is created by
/// <c>004_satelites_datos_maestros.sql</c>, no demo seed (unlike <c>fact.MotivoAtributo</c>), so no
/// post-migration cleanup is needed here. No method ranks/sorts/selects a single "best" candidate
/// (design.md Decision 2, spec.md -- item #9's job); the list methods below only prove raw storage
/// retrieval, in whatever order the adapter returns rows.
/// </summary>
public sealed class SqlSugerenciaCuentaRepositoryTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;

    public async Task InitializeAsync() => _db = await MigratedDatabase();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static async Task<TestDatabaseFixture> MigratedDatabase()
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

    private static readonly DateTimeOffset PrimerUso = new(2026, 1, 10, 9, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SegundoUso = new(2026, 2, 5, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListarPorProveedorYMotivoAsync_ReturnsEmptyCollection_NotNull_OnZeroRows()
    {
        var sut = new SqlSugerenciaCuentaRepository(_db.ConnectionString);

        var sugerencias = await sut.ListarPorProveedorYMotivoAsync("P00001", 22, CancellationToken.None);

        Assert.NotNull(sugerencias);
        Assert.Empty(sugerencias);
    }

    [Fact]
    public async Task ListarPorProveedorYMotivoAsync_ReturnsOnlyRowsMatchingBothKeys()
    {
        var sut = new SqlSugerenciaCuentaRepository(_db.ConnectionString);
        await sut.RegistrarUsoAsync("P00001", 22, "631111", PrimerUso, CancellationToken.None);
        await sut.RegistrarUsoAsync("P00001", 48, "6373", PrimerUso, CancellationToken.None);
        await sut.RegistrarUsoAsync("P00002", 22, "631111", PrimerUso, CancellationToken.None);

        var sugerencias = await sut.ListarPorProveedorYMotivoAsync("P00001", 22, CancellationToken.None);

        Assert.Single(sugerencias);
        Assert.Equal("631111", sugerencias[0].CuentaCodigo);
    }

    [Fact]
    public async Task ListarPorMotivoAsync_ReturnsAllRowsForThatMotivo_AcrossProviders()
    {
        var sut = new SqlSugerenciaCuentaRepository(_db.ConnectionString);
        await sut.RegistrarUsoAsync("P00001", 22, "631111", PrimerUso, CancellationToken.None);
        await sut.RegistrarUsoAsync("P00002", 22, "631112", PrimerUso, CancellationToken.None);
        await sut.RegistrarUsoAsync("P00001", 48, "6373", PrimerUso, CancellationToken.None);

        var sugerencias = await sut.ListarPorMotivoAsync(22, CancellationToken.None);

        Assert.Equal(2, sugerencias.Count);
        Assert.Contains(sugerencias, s => s.ProveedorCodigo == "P00001" && s.CuentaCodigo == "631111");
        Assert.Contains(sugerencias, s => s.ProveedorCodigo == "P00002" && s.CuentaCodigo == "631112");
    }

    [Fact]
    public async Task ListarPorProveedorAsync_ReturnsAllRowsForThatProveedor_AcrossMotivos()
    {
        var sut = new SqlSugerenciaCuentaRepository(_db.ConnectionString);
        await sut.RegistrarUsoAsync("P00001", 22, "631111", PrimerUso, CancellationToken.None);
        await sut.RegistrarUsoAsync("P00001", 48, "6373", PrimerUso, CancellationToken.None);
        await sut.RegistrarUsoAsync("P00002", 22, "631111", PrimerUso, CancellationToken.None);

        var sugerencias = await sut.ListarPorProveedorAsync("P00001", CancellationToken.None);

        Assert.Equal(2, sugerencias.Count);
        Assert.Contains(sugerencias, s => s.Motivo == 22 && s.CuentaCodigo == "631111");
        Assert.Contains(sugerencias, s => s.Motivo == 48 && s.CuentaCodigo == "6373");
    }

    [Fact]
    public async Task RegistrarUsoAsync_InsertsANewRow_WithVecesOneAndTheGivenInstant_ForANewCombination()
    {
        var sut = new SqlSugerenciaCuentaRepository(_db.ConnectionString);

        await sut.RegistrarUsoAsync("P00001", 22, "631111", PrimerUso, CancellationToken.None);
        var sugerencias = await sut.ListarPorProveedorYMotivoAsync("P00001", 22, CancellationToken.None);

        Assert.Single(sugerencias);
        Assert.Equal(1, sugerencias[0].Veces);
        Assert.Equal(PrimerUso, sugerencias[0].UltimoUso);
    }

    // Known bug class: an UPDATE that increments Veces but forgets to also write UltimoUso (or vice
    // versa). RegistrarUsoAsync's second call for the SAME combination must move BOTH fields in one
    // statement, and the instant must be the caller-supplied parameter, never SYSUTCDATETIME().
    [Fact]
    public async Task RegistrarUsoAsync_IncrementsVecesAndUpdatesUltimoUso_ForAnExistingCombination()
    {
        var sut = new SqlSugerenciaCuentaRepository(_db.ConnectionString);
        await sut.RegistrarUsoAsync("P00001", 22, "631111", PrimerUso, CancellationToken.None);

        await sut.RegistrarUsoAsync("P00001", 22, "631111", SegundoUso, CancellationToken.None);
        var sugerencias = await sut.ListarPorProveedorYMotivoAsync("P00001", 22, CancellationToken.None);

        Assert.Single(sugerencias);
        Assert.Equal(2, sugerencias[0].Veces);
        Assert.Equal(SegundoUso, sugerencias[0].UltimoUso);

        var count = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.SugerenciaCuenta WHERE ProveedorCodigo = 'P00001' AND Motivo = 22 AND CuentaCodigo = '631111';");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task RegistrarUsoAsync_LeavesOtherCombinations_Untouched()
    {
        var sut = new SqlSugerenciaCuentaRepository(_db.ConnectionString);
        await sut.RegistrarUsoAsync("P00001", 22, "631111", PrimerUso, CancellationToken.None);
        await sut.RegistrarUsoAsync("P00002", 22, "631112", PrimerUso, CancellationToken.None);

        await sut.RegistrarUsoAsync("P00001", 22, "631111", SegundoUso, CancellationToken.None);
        var otraSugerencia = await sut.ListarPorProveedorYMotivoAsync("P00002", 22, CancellationToken.None);

        Assert.Single(otraSugerencia);
        Assert.Equal(1, otraSugerencia[0].Veces);
        Assert.Equal(PrimerUso, otraSugerencia[0].UltimoUso);
    }
}
