using SmartNet.Db.TestBootstrap;

namespace SmartNet.Catalogos.Infrastructure.Tests;

/// <summary>Task 2.9/2.10 -- <see cref="SqlOrigenRepository"/> against a real, migrated database.</summary>
public sealed class SqlOrigenRepositoryTests : IAsyncLifetime
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
    public async Task ListarAsync_ReturnsAllSeededRows()
    {
        await _db.SeedOrigenAsync("01", "Compras");
        await _db.SeedOrigenAsync("02", "Caja Chica");
        var sut = new SqlOrigenRepository(_db.ConnectionString);

        var origenes = await sut.ListarAsync(CancellationToken.None);

        Assert.Equal(2, origenes.Count);
        Assert.Contains(origenes, o => o.Codigo == "01" && o.Descripcion == "Compras");
    }

    [Fact]
    public async Task ListarAsync_ReturnsEmptyCollection_NotNull_OnZeroRows()
    {
        var sut = new SqlOrigenRepository(_db.ConnectionString);

        var origenes = await sut.ListarAsync(CancellationToken.None);

        Assert.NotNull(origenes);
        Assert.Empty(origenes);
    }
}
