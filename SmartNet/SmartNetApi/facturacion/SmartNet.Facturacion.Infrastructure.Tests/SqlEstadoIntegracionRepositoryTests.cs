using SmartNet.Db.TestBootstrap;

namespace SmartNet.Facturacion.Infrastructure.Tests;

/// <summary>tasks.md 1.11 — design D7: read-only passthrough over fact.EstadoIntegracion (seeded
/// with its seven canonical rows by 009_datos_base.sql).</summary>
public sealed class SqlEstadoIntegracionRepositoryTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;

    public async Task InitializeAsync() => _db = await FacturacionTestDatabaseFixtureHelper.MigratedDatabaseAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task ListarAsync_ReturnsARowPerSeededIntegracion()
    {
        var sut = new SqlEstadoIntegracionRepository(_db.ConnectionString);

        var resultado = await sut.ListarAsync(CancellationToken.None);

        Assert.NotEmpty(resultado);
        Assert.Contains(resultado, e => e.Nombre == "GMAIL");
    }

    [Fact]
    public async Task ListarAsync_ReflectsAWrittenFailureCount()
    {
        await _db.ExecuteNonQueryAsync("UPDATE fact.EstadoIntegracion SET FallosSeguidos = 3, UltimoError = 'timeout' WHERE Nombre = 'SBS';");
        var sut = new SqlEstadoIntegracionRepository(_db.ConnectionString);

        var resultado = await sut.ListarAsync(CancellationToken.None);

        var sbs = Assert.Single(resultado, e => e.Nombre == "SBS");
        Assert.Equal(3, sbs.FallosConsecutivos);
        Assert.Equal("timeout", sbs.UltimoError);
    }
}
