using SmartNet.Db.TestBootstrap;

namespace SmartNet.Catalogos.Infrastructure.Tests;

/// <summary>
/// Tasks 2.5/2.6 -- <see cref="SqlMotivoRepository"/> against a real, migrated
/// <c>fact_test_&lt;id&gt;</c> database. <c>dbo.Motivo</c> is seeded by
/// <c>SeedDboMotivoFixtureRowsAsync</c> (28-row demo subset, WU0/WU1's shared fixture) — required
/// for migration 010 to pass (it THROWs unless dbo.Motivo has exactly 23 reclassified rows), so the
/// empty-collection case empties the table AFTER migration instead of skipping the seed.
/// </summary>
public sealed class SqlMotivoRepositoryTests : IAsyncLifetime
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

    [Fact]
    public async Task ObtenerAsync_ReturnsTheRow_ForAnExistingCode()
    {
        var sut = new SqlMotivoRepository(_db.ConnectionString);

        var motivo = await sut.ObtenerAsync(22, CancellationToken.None);

        Assert.NotNull(motivo);
        Assert.Equal(22, motivo!.Codigo);
        Assert.Equal("631111", motivo.Cuenta);
    }

    [Fact]
    public async Task ObtenerAsync_ReturnsNull_NoException_ForAMissingCode()
    {
        var sut = new SqlMotivoRepository(_db.ConnectionString);

        var motivo = await sut.ObtenerAsync(999, CancellationToken.None);

        Assert.Null(motivo);
    }

    [Fact]
    public async Task ListarAsync_ReturnsAllSeededRows()
    {
        var sut = new SqlMotivoRepository(_db.ConnectionString);

        var motivos = await sut.ListarAsync(CancellationToken.None);

        Assert.Equal(28, motivos.Count);
        Assert.Contains(motivos, m => m.Codigo == 48 && m.Cuenta == "6373");
    }

    // dbo.Motivo cannot stay unseeded through RunMigrations(): 010_motivo_atributo_demo.sql THROWs
    // unless it finds exactly 23 reclassified motives (MOTIVOS-CLASIFICACION.md). So the
    // empty-collection case migrates normally, then empties dbo.Motivo AFTER migration to exercise
    // the adapter's zero-row read path without breaking the migration chain.
    [Fact]
    public async Task ListarAsync_ReturnsEmptyCollection_NotNull_OnZeroRows()
    {
        await _db.ExecuteNonQueryAsync("DELETE FROM dbo.Motivo;");
        var sut = new SqlMotivoRepository(_db.ConnectionString);

        var motivos = await sut.ListarAsync(CancellationToken.None);

        Assert.NotNull(motivos);
        Assert.Empty(motivos);
    }
}
