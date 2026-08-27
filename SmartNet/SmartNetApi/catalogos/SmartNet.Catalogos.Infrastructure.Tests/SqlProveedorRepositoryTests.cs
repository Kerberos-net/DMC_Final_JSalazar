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

    // BACKLOG #18 PR8 (api-catalogos-proveedores): paged name/RUC search for the SPA picker.
    // Read-only SELECT on dbo.Proveedor, ordered by `proveedor`, P00000 ("Varios") filtered out.

    [Fact]
    public async Task BuscarAsync_MatchesByNameFragment_OrderedByNombre()
    {
        await _db.SeedProveedorAsync("P00010", "ACME PERU SAC", coddocide: "06", rucpro: "20100000001");
        await _db.SeedProveedorAsync("P00011", "ACME ANDINA EIRL", coddocide: "06", rucpro: "20100000002");
        await _db.SeedProveedorAsync("P00012", "OTRO PROVEEDOR", coddocide: "06", rucpro: "20100000003");
        var sut = new SqlProveedorRepository(_db.ConnectionString);

        var busqueda = await sut.BuscarAsync("ACME", 1, CancellationToken.None);

        Assert.False(busqueda.HayMas);
        Assert.Equal(new[] { "ACME ANDINA EIRL", "ACME PERU SAC" }, busqueda.Resultados.Select(p => p.Nombre));
        Assert.Equal("20100000002", busqueda.Resultados[0].Ruc);
    }

    [Fact]
    public async Task BuscarAsync_MatchesByRuc()
    {
        await _db.SeedProveedorAsync("P00013", "COMERCIAL DELTA", coddocide: "06", rucpro: "20555555555");
        await _db.SeedProveedorAsync("P00014", "COMERCIAL GAMMA", coddocide: "06", rucpro: "20111111111");
        var sut = new SqlProveedorRepository(_db.ConnectionString);

        var busqueda = await sut.BuscarAsync("20555555555", 1, CancellationToken.None);

        Assert.Single(busqueda.Resultados);
        Assert.Equal("P00013", busqueda.Resultados[0].Codigo);
    }

    [Fact]
    public async Task BuscarAsync_ExcludesP00000_EvenWhenItMatchesTextually()
    {
        await _db.SeedProveedorAsync("P00000", "VARIOS", coddocide: "06", rucpro: null);
        await _db.SeedProveedorAsync("P00015", "VARIOS HERMANOS SAC", coddocide: "06", rucpro: "20222222222");
        var sut = new SqlProveedorRepository(_db.ConnectionString);

        var busqueda = await sut.BuscarAsync("VARIOS", 1, CancellationToken.None);

        Assert.Equal(new[] { "P00015" }, busqueda.Resultados.Select(p => p.Codigo));
    }

    [Fact]
    public async Task BuscarAsync_PagesResults_AndReportsHayMas()
    {
        for (var i = 0; i < SqlProveedorRepository.TamanoPagina + 3; i++)
        {
            await _db.SeedProveedorAsync($"Q{i:D5}", $"PAGINADO {i:D3}", coddocide: "06", rucpro: null);
        }
        var sut = new SqlProveedorRepository(_db.ConnectionString);

        var primera = await sut.BuscarAsync("PAGINADO", 1, CancellationToken.None);
        var segunda = await sut.BuscarAsync("PAGINADO", 2, CancellationToken.None);

        Assert.Equal(SqlProveedorRepository.TamanoPagina, primera.Resultados.Count);
        Assert.True(primera.HayMas);
        Assert.Equal(3, segunda.Resultados.Count);
        Assert.False(segunda.HayMas);
        Assert.Equal("PAGINADO 000", primera.Resultados[0].Nombre);
        Assert.Equal("PAGINADO 020", segunda.Resultados[0].Nombre);
    }

    [Fact]
    public async Task BuscarAsync_PagePastTheEnd_ReturnsEmpty_HayMasFalse()
    {
        await _db.SeedProveedorAsync("P00016", "SIGMA UNO", coddocide: "06", rucpro: null);
        var sut = new SqlProveedorRepository(_db.ConnectionString);

        var busqueda = await sut.BuscarAsync("SIGMA", 5, CancellationToken.None);

        Assert.Empty(busqueda.Resultados);
        Assert.False(busqueda.HayMas);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    public async Task BuscarAsync_BlankOrShortQuery_ReturnsEmpty(string consulta)
    {
        await _db.SeedProveedorAsync("P00017", "ALGUN PROVEEDOR", coddocide: "06", rucpro: null);
        var sut = new SqlProveedorRepository(_db.ConnectionString);

        var busqueda = await sut.BuscarAsync(consulta, 1, CancellationToken.None);

        Assert.Empty(busqueda.Resultados);
        Assert.False(busqueda.HayMas);
    }

    [Fact]
    public async Task BuscarAsync_NoMatches_ReturnsEmpty()
    {
        await _db.SeedProveedorAsync("P00018", "PROVEEDOR REAL", coddocide: "06", rucpro: null);
        var sut = new SqlProveedorRepository(_db.ConnectionString);

        var busqueda = await sut.BuscarAsync("ZZZNOEXISTE", 1, CancellationToken.None);

        Assert.Empty(busqueda.Resultados);
        Assert.False(busqueda.HayMas);
    }
}
