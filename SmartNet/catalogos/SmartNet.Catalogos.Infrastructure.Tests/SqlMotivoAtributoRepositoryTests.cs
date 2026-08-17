using SmartNet.Catalogos.Core;
using SmartNet.Db.TestBootstrap;

namespace SmartNet.Catalogos.Infrastructure.Tests;

/// <summary>
/// Tasks 3.3/3.4 -- <see cref="SqlMotivoAtributoRepository"/> against a real, migrated
/// <c>fact_test_&lt;id&gt;</c> database. <c>fact.MotivoAtributo</c> is created by
/// <c>004_satelites_datos_maestros.sql</c>. <c>Activo</c>/`origen '02'` filtering is a Core-level
/// concern (design.md); this adapter returns raw rows only -- no `WHERE Activo = 1` anywhere here.
/// </summary>
public sealed class SqlMotivoAtributoRepositoryTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;

    // 010_motivo_atributo_demo.sql (run by RunMigrations()) inserts 23 demo rows into
    // fact.MotivoAtributo unconditionally -- delete them so every test below starts from a clean
    // table, same discipline as SqlMotivoRepositoryTests' post-migration DELETE for dbo.Motivo.
    public async Task InitializeAsync()
    {
        _db = await MigratedDatabase();
        await _db.ExecuteNonQueryAsync("DELETE FROM fact.MotivoAtributo;");
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
    public async Task ObtenerAsync_ReturnsNull_NoException_ForAnUnseededMotivo()
    {
        var sut = new SqlMotivoAtributoRepository(_db.ConnectionString);

        var atributo = await sut.ObtenerAsync(999, CancellationToken.None);

        Assert.Null(atributo);
    }

    [Fact]
    public async Task GuardarAsync_InsertsANewRow_ForAMotivoNotYetPresent()
    {
        var sut = new SqlMotivoAtributoRepository(_db.ConnectionString);

        await sut.GuardarAsync(new MotivoAtributo(22, Activo: true, OrigenLibro: "02"), CancellationToken.None);
        var atributo = await sut.ObtenerAsync(22, CancellationToken.None);

        Assert.NotNull(atributo);
        Assert.Equal(22, atributo!.Motivo);
        Assert.True(atributo.Activo);
        Assert.Equal("02", atributo.OrigenLibro);
    }

    [Fact]
    public async Task ListarAsync_ReturnsEmptyCollection_NotNull_OnZeroRows()
    {
        var sut = new SqlMotivoAtributoRepository(_db.ConnectionString);

        var atributos = await sut.ListarAsync(CancellationToken.None);

        Assert.NotNull(atributos);
        Assert.Empty(atributos);
    }

    [Fact]
    public async Task ListarAsync_ReturnsAllSeededRows()
    {
        var sut = new SqlMotivoAtributoRepository(_db.ConnectionString);
        await sut.GuardarAsync(new MotivoAtributo(22, Activo: true, OrigenLibro: "02"), CancellationToken.None);
        await sut.GuardarAsync(new MotivoAtributo(48, Activo: false, OrigenLibro: "01"), CancellationToken.None);

        var atributos = await sut.ListarAsync(CancellationToken.None);

        Assert.Equal(2, atributos.Count);
        Assert.Contains(atributos, a => a.Motivo == 22 && a.Activo && a.OrigenLibro == "02");
        Assert.Contains(atributos, a => a.Motivo == 48 && !a.Activo && a.OrigenLibro == "01");
    }

    // Known bug class: an UPDATE that writes only the first field (Activo) and forgets the second
    // (OrigenLibro). GuardarAsync must flip BOTH in the same upsert, not just Activo.
    [Fact]
    public async Task GuardarAsync_UpdatesBothFields_WhenTheMotivoAlreadyExists()
    {
        var sut = new SqlMotivoAtributoRepository(_db.ConnectionString);
        await sut.GuardarAsync(new MotivoAtributo(48, Activo: true, OrigenLibro: "02"), CancellationToken.None);

        await sut.GuardarAsync(new MotivoAtributo(48, Activo: false, OrigenLibro: "01"), CancellationToken.None);
        var atributo = await sut.ObtenerAsync(48, CancellationToken.None);

        Assert.NotNull(atributo);
        Assert.False(atributo!.Activo);
        Assert.Equal("01", atributo.OrigenLibro);

        var count = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.MotivoAtributo WHERE Motivo = 48;");
        Assert.Equal(1, count);
    }
}
