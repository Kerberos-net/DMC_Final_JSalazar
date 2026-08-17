using SmartNet.Db.TestBootstrap;

namespace SmartNet.Catalogos.Infrastructure.Tests;

/// <summary>
/// Tasks 2.7/2.8 -- <see cref="SqlProveedorRepository"/> against a real, migrated
/// <c>fact_test_&lt;id&gt;</c> database. <c>dbo.Proveedor.coddocide</c> FKs to
/// <c>dbo.DocumentoIdentidad</c>, so that catalog is seeded first.
/// </summary>
public sealed class SqlProveedorRepositoryTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;

    public async Task InitializeAsync()
    {
        _db = await MigratedDatabase();
        await _db.SeedDocumentoIdentidadAsync("01", "DNI");
        await _db.SeedDocumentoIdentidadAsync("06", "RUC");
    }

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
    public async Task ObtenerPorCodigoAsync_ReturnsTheRow_ForAnExistingCode()
    {
        await _db.SeedProveedorAsync("P00001", "Proveedor Uno", coddocide: "06", rucpro: "20123456789");
        var sut = new SqlProveedorRepository(_db.ConnectionString);

        var proveedor = await sut.ObtenerPorCodigoAsync("P00001", CancellationToken.None);

        Assert.NotNull(proveedor);
        Assert.Equal("P00001", proveedor!.Codigo);
        Assert.Equal("Proveedor Uno", proveedor.Nombre);
        Assert.Equal("06", proveedor.CodigoTipoDocumento);
        Assert.Equal("20123456789", proveedor.Ruc);
    }

    [Fact]
    public async Task ObtenerPorCodigoAsync_ReturnsNull_NoException_ForAMissingCode()
    {
        var sut = new SqlProveedorRepository(_db.ConnectionString);

        var proveedor = await sut.ObtenerPorCodigoAsync("P99999", CancellationToken.None);

        Assert.Null(proveedor);
    }

    // rucpro is non-unique (IX_Proveedor_Ruc is a non-unique index, not a key) -- two providers can
    // share one RUC. BuscarPorRucAsync MUST return a list, never assume/enforce uniqueness.
    [Fact]
    public async Task BuscarPorRucAsync_ReturnsAListOfBothProviders_SharingOneRuc()
    {
        const string rucCompartido = "20999999999";
        await _db.SeedProveedorAsync("P00002", "Proveedor Dos", coddocide: "06", rucpro: rucCompartido);
        await _db.SeedProveedorAsync("P00003", "Proveedor Tres", coddocide: "06", rucpro: rucCompartido);
        await _db.SeedProveedorAsync("P00004", "Proveedor Cuatro", coddocide: "06", rucpro: "20111111111");
        var sut = new SqlProveedorRepository(_db.ConnectionString);

        var proveedores = await sut.BuscarPorRucAsync(rucCompartido, CancellationToken.None);

        Assert.Equal(2, proveedores.Count);
        Assert.Contains(proveedores, p => p.Codigo == "P00002");
        Assert.Contains(proveedores, p => p.Codigo == "P00003");
    }
}
