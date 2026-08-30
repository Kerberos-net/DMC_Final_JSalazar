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

    // `%` and `_` in the typed term must be literal, not LIKE wildcards -- searching "A_B" must
    // not also match "AXB" (SqlProveedorRepository escapes them; the query stays parameterised).
    [Fact]
    public async Task BuscarAsync_TreatsLikeWildcardsInTheTermAsLiterals()
    {
        await _db.SeedProveedorAsync("P00019", "A_B LOGISTICA", coddocide: "06", rucpro: null);
        await _db.SeedProveedorAsync("P00020", "AXB LOGISTICA", coddocide: "06", rucpro: null);
        var sut = new SqlProveedorRepository(_db.ConnectionString);

        var busqueda = await sut.BuscarAsync("A_B", 1, CancellationToken.None);

        Assert.Equal(new[] { "P00019" }, busqueda.Resultados.Select(p => p.Codigo));
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

    // ---- BACKLOG #22 PR5: catalogo mode (api spec req 1, 2, 3; design D6/D7) ----

    private async Task SeedCatalogoSetAsync(int cantidad, string prefijoNombre)
    {
        for (var i = 0; i < cantidad; i++)
        {
            await _db.SeedProveedorAsync($"C{i:D5}", $"{prefijoNombre} {i:D3}", coddocide: "06", rucpro: $"20{i:D9}");
        }
    }

    [Fact]
    public async Task ListarCatalogoAsync_TotalRegistros_IsFullFilteredCount_OnPage1AndPage3()
    {
        await SeedCatalogoSetAsync(45, "CAT");
        var sut = new SqlProveedorRepository(_db.ConnectionString);

        var pagina1 = await sut.ListarCatalogoAsync("CAT", "proveedor", "asc", 1, 20, CancellationToken.None);
        var pagina3 = await sut.ListarCatalogoAsync("CAT", "proveedor", "asc", 3, 20, CancellationToken.None);

        Assert.Equal(45, pagina1.TotalRegistros);
        Assert.Equal(3, pagina1.TotalPaginas);
        Assert.Equal(20, pagina1.Items.Count);
        Assert.Equal(1, pagina1.Pagina);
        Assert.Equal(20, pagina1.TamanioPagina);

        Assert.Equal(45, pagina3.TotalRegistros);
        Assert.Equal(5, pagina3.Items.Count);
        Assert.Equal("CAT 040", pagina3.Items[0].Nombre);
    }

    [Fact]
    public async Task ListarCatalogoAsync_PagePastTheEnd_ReturnsEmptyItems_WithCorrectTotals()
    {
        await SeedCatalogoSetAsync(10, "FIN");
        var sut = new SqlProveedorRepository(_db.ConnectionString);

        var pagina = await sut.ListarCatalogoAsync("FIN", "proveedor", "asc", 9, 20, CancellationToken.None);

        Assert.Empty(pagina.Items);
        Assert.Equal(10, pagina.TotalRegistros);
        Assert.Equal(1, pagina.TotalPaginas);
    }

    [Fact]
    public async Task ListarCatalogoAsync_IncludesP00000()
    {
        await _db.SeedProveedorAsync("P00000", "VARIOS", coddocide: "06", rucpro: null);
        await _db.SeedProveedorAsync("C00001", "VARIOS HERMANOS", coddocide: "06", rucpro: "20222222222");
        var sut = new SqlProveedorRepository(_db.ConnectionString);

        var pagina = await sut.ListarCatalogoAsync("VARIOS", "codigo", "asc", 1, 20, CancellationToken.None);

        Assert.Equal(new[] { "C00001", "P00000" }, pagina.Items.Select(p => p.Codigo));
    }

    [Fact]
    public async Task ListarCatalogoAsync_BlankQuery_ListsEverything()
    {
        await SeedCatalogoSetAsync(3, "TODO");
        var sut = new SqlProveedorRepository(_db.ConnectionString);

        var pagina = await sut.ListarCatalogoAsync("  ", "proveedor", "asc", 1, 20, CancellationToken.None);

        Assert.Equal(3, pagina.TotalRegistros);
    }

    [Theory]
    [InlineData("proveedor", "asc", new[] { "CAT 000", "CAT 001", "CAT 002" })]
    [InlineData("proveedor", "desc", new[] { "CAT 002", "CAT 001", "CAT 000" })]
    [InlineData("codigo", "asc", new[] { "CAT 000", "CAT 001", "CAT 002" })]
    [InlineData("codigo", "desc", new[] { "CAT 002", "CAT 001", "CAT 000" })]
    [InlineData("ruc", "asc", new[] { "CAT 000", "CAT 001", "CAT 002" })]
    [InlineData("ruc", "desc", new[] { "CAT 002", "CAT 001", "CAT 000" })]
    public async Task ListarCatalogoAsync_ServerSort_PerKeyAndDirection(string orden, string direccion, string[] esperado)
    {
        await SeedCatalogoSetAsync(3, "CAT");
        var sut = new SqlProveedorRepository(_db.ConnectionString);

        var pagina = await sut.ListarCatalogoAsync("CAT", orden, direccion, 1, 20, CancellationToken.None);

        Assert.Equal(esperado, pagina.Items.Select(p => p.Nombre));
    }

    // design D7 CORRECTNESS: `proveedor` repeats and `rucpro` is non-unique AND nullable; without a
    // unique `, codpro ASC` tiebreak OFFSET/FETCH drops or duplicates rows across a page boundary.
    [Fact]
    public async Task ListarCatalogoAsync_CodproTiebreak_IsStableAcrossAPageBoundary_WhenNameRepeats()
    {
        for (var i = 0; i < 10; i++)
        {
            await _db.SeedProveedorAsync($"T{i:D5}", "NOMBRE REPETIDO", coddocide: "06", rucpro: null);
        }
        var sut = new SqlProveedorRepository(_db.ConnectionString);

        var pagina1 = await sut.ListarCatalogoAsync("REPETIDO", "proveedor", "asc", 1, 4, CancellationToken.None);
        var pagina2 = await sut.ListarCatalogoAsync("REPETIDO", "proveedor", "asc", 2, 4, CancellationToken.None);
        var pagina3 = await sut.ListarCatalogoAsync("REPETIDO", "proveedor", "asc", 3, 4, CancellationToken.None);

        var vistos = pagina1.Items.Concat(pagina2.Items).Concat(pagina3.Items).Select(p => p.Codigo).ToArray();

        Assert.Equal(10, vistos.Length);
        Assert.Equal(10, vistos.Distinct().Count());
        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"T{i:D5}"), vistos);
    }

    [Fact]
    public async Task ListarCatalogoAsync_RucproNullsSortFirst_Ascending()
    {
        await _db.SeedProveedorAsync("N00001", "CON RUC", coddocide: "06", rucpro: "20100000000");
        await _db.SeedProveedorAsync("N00002", "SIN RUC", coddocide: "06", rucpro: null);
        var sut = new SqlProveedorRepository(_db.ConnectionString);

        var pagina = await sut.ListarCatalogoAsync(null, "ruc", "asc", 1, 20, CancellationToken.None);

        Assert.Equal("N00002", pagina.Items[0].Codigo);
    }

    [Fact]
    public async Task ListarCatalogoAsync_RespectsTamanio()
    {
        await SeedCatalogoSetAsync(20, "TAM");
        var sut = new SqlProveedorRepository(_db.ConnectionString);

        var pagina = await sut.ListarCatalogoAsync("TAM", "proveedor", "asc", 1, 6, CancellationToken.None);

        Assert.Equal(6, pagina.Items.Count);
        Assert.Equal(6, pagina.TamanioPagina);
        Assert.Equal(20, pagina.TotalRegistros);
        Assert.Equal(4, pagina.TotalPaginas);
    }

    [Fact]
    public async Task ListarCatalogoCompletoAsync_IsUnpaged_SameOrderAsThePagedQuery()
    {
        await SeedCatalogoSetAsync(30, "FULL");
        var sut = new SqlProveedorRepository(_db.ConnectionString);

        var completo = await sut.ListarCatalogoCompletoAsync("FULL", "codigo", "desc", CancellationToken.None);
        var pagina1 = await sut.ListarCatalogoAsync("FULL", "codigo", "desc", 1, 20, CancellationToken.None);

        Assert.Equal(30, completo.Count);
        Assert.Equal(pagina1.Items.Select(p => p.Codigo), completo.Take(20).Select(p => p.Codigo));
        Assert.Equal("FULL 029", completo[0].Nombre);
    }
}
