using SmartNet.Catalogos.Core;
using SmartNet.Db.TestBootstrap;

namespace SmartNet.Catalogos.Infrastructure.Tests;

/// <summary>
/// Tasks 3.1/3.2 -- <see cref="SqlProveedorAtributoRepository"/> against a real, migrated
/// <c>fact_test_&lt;id&gt;</c> database. <c>fact.ProveedorAtributo</c> is created by
/// <c>004_satelites_datos_maestros.sql</c> (part of <c>RunMigrations()</c>) -- no local seed helper
/// needed since <see cref="GuardarAsync"/> writes the row under test itself. No existence check
/// against <c>dbo.Proveedor</c> is exercised or expected here (design.md Decision 2): the code
/// below is never seeded into <c>dbo.Proveedor</c> and <see cref="GuardarAsync"/> must still
/// succeed.
/// </summary>
public sealed class SqlProveedorAtributoRepositoryTests : IAsyncLifetime
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
    public async Task ObtenerAsync_ReturnsNull_NoException_ForAnUnseededCode()
    {
        var sut = new SqlProveedorAtributoRepository(_db.ConnectionString);

        var atributo = await sut.ObtenerAsync("P00001", CancellationToken.None);

        Assert.Null(atributo);
    }

    [Fact]
    public async Task GuardarAsync_InsertsANewRow_ForACodeNotYetPresent()
    {
        var sut = new SqlProveedorAtributoRepository(_db.ConnectionString);

        await sut.GuardarAsync(new ProveedorAtributo("P00001", EsRelacionada: true), CancellationToken.None);
        var atributo = await sut.ObtenerAsync("P00001", CancellationToken.None);

        Assert.NotNull(atributo);
        Assert.Equal("P00001", atributo!.ProveedorCodigo);
        Assert.True(atributo.EsRelacionada);
    }

    // The known bug class in this project: an UPDATE that writes only the first field and silently
    // forgets the rest. EsRelacionada is the only non-key field, but the upsert path (UPDATE branch,
    // not the INSERT branch exercised above) must still be proven to actually flip it.
    [Fact]
    public async Task GuardarAsync_UpdatesTheExistingRow_WhenTheCombinationAlreadyExists()
    {
        var sut = new SqlProveedorAtributoRepository(_db.ConnectionString);
        await sut.GuardarAsync(new ProveedorAtributo("P00002", EsRelacionada: false), CancellationToken.None);

        await sut.GuardarAsync(new ProveedorAtributo("P00002", EsRelacionada: true), CancellationToken.None);
        var atributo = await sut.ObtenerAsync("P00002", CancellationToken.None);

        Assert.NotNull(atributo);
        Assert.True(atributo!.EsRelacionada);

        var count = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.ProveedorAtributo WHERE ProveedorCodigo = 'P00002';");
        Assert.Equal(1, count);
    }
}
