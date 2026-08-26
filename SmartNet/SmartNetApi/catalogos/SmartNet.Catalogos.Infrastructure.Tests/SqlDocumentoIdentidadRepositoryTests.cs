using SmartNet.Db.TestBootstrap;

namespace SmartNet.Catalogos.Infrastructure.Tests;

/// <summary>Task 2.9/2.10 -- <see cref="SqlDocumentoIdentidadRepository"/> against a real, migrated database.</summary>
public sealed class SqlDocumentoIdentidadRepositoryTests : IAsyncLifetime
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
        await _db.SeedDocumentoIdentidadAsync("01", "DNI");
        await _db.SeedDocumentoIdentidadAsync("06", "RUC");
        var sut = new SqlDocumentoIdentidadRepository(_db.ConnectionString);

        var documentos = await sut.ListarAsync(CancellationToken.None);

        Assert.Equal(2, documentos.Count);
        Assert.Contains(documentos, d => d.Codigo == "01" && d.Nombre == "DNI");
    }

    [Fact]
    public async Task ListarAsync_ReturnsEmptyCollection_NotNull_OnZeroRows()
    {
        var sut = new SqlDocumentoIdentidadRepository(_db.ConnectionString);

        var documentos = await sut.ListarAsync(CancellationToken.None);

        Assert.NotNull(documentos);
        Assert.Empty(documentos);
    }
}
