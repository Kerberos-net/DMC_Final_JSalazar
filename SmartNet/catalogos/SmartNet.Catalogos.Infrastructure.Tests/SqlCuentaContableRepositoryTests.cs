using SmartNet.Db.TestBootstrap;

namespace SmartNet.Catalogos.Infrastructure.Tests;

/// <summary>
/// Tasks 2.3/2.4 -- <see cref="SqlCuentaContableRepository"/> against a real, migrated
/// <c>fact_test_&lt;id&gt;</c> database. <c>dbo.CuentaContable</c> is seeded locally
/// (design.md Decision 3) since <c>CreateExternalDboCatalogsAsync</c> leaves it empty.
/// </summary>
public sealed class SqlCuentaContableRepositoryTests : IAsyncLifetime
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
    public async Task ListarPlanCompletoAsync_MapsEveryColumn_IncludingNivelCtaReflejaCtaPuente()
    {
        await _db.SeedCuentaContableAsync("631111", "Fletes traslado de mercaderia", nivel: null, ctaRefleja: "631112", ctaPuente: "631113");
        await _db.SeedCuentaContableAsync("403", "Proveedores", nivel: 3);
        var sut = new SqlCuentaContableRepository(_db.ConnectionString);

        var plan = await sut.ListarPlanCompletoAsync(CancellationToken.None);

        Assert.Equal(2, plan.Count);
        var hoja = plan.Single(c => c.Cuenta == "631111");
        Assert.Equal("Fletes traslado de mercaderia", hoja.Descripcion);
        Assert.Null(hoja.Nivel);
        Assert.Equal("631112", hoja.CtaReflejaCodigo);
        Assert.Equal("631113", hoja.CtaPuenteCodigo);
        var nodo = plan.Single(c => c.Cuenta == "403");
        Assert.Equal((byte)3, nodo.Nivel);
        Assert.Null(nodo.CtaReflejaCodigo);
        Assert.Null(nodo.CtaPuenteCodigo);
    }

    [Fact]
    public async Task ObtenerAsync_ReturnsTheRow_ForAnExistingCode()
    {
        await _db.SeedCuentaContableAsync("631111", "Fletes traslado de mercaderia");
        var sut = new SqlCuentaContableRepository(_db.ConnectionString);

        var cuenta = await sut.ObtenerAsync("631111", CancellationToken.None);

        Assert.NotNull(cuenta);
        Assert.Equal("631111", cuenta!.Cuenta);
        Assert.Equal("Fletes traslado de mercaderia", cuenta.Descripcion);
    }

    [Fact]
    public async Task ObtenerAsync_ReturnsNull_NoException_ForAMissingCode()
    {
        var sut = new SqlCuentaContableRepository(_db.ConnectionString);

        var cuenta = await sut.ObtenerAsync("999999", CancellationToken.None);

        Assert.Null(cuenta);
    }
}
