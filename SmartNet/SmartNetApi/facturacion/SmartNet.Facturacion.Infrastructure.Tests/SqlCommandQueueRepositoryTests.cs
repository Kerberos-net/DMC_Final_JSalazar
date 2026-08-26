using SmartNet.Db.TestBootstrap;

namespace SmartNet.Facturacion.Infrastructure.Tests;

/// <summary>tasks.md 1.11 — design D7: an enqueue is a plain INSERT, never a Python call (ADR 0003).</summary>
public sealed class SqlCommandQueueRepositoryTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;

    public async Task InitializeAsync() => _db = await FacturacionTestDatabaseFixtureHelper.MigratedDatabaseAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task EncolarAsync_InsertsOnePendienteRow_WithTheGivenCorrelationId()
    {
        var sut = new SqlCommandQueueRepository(_db.ConnectionString);
        var correlationId = Guid.NewGuid();

        await sut.EncolarAsync("SINCRONIZAR_GMAIL", referencia: null, payload: "{}", correlationId, CancellationToken.None);

        var estado = await _db.ExecuteScalarAsync<string>(
            $"SELECT Estado FROM fact.CommandQueue WHERE CorrelationId = '{correlationId}';");
        Assert.Equal("PENDIENTE", estado!.TrimEnd());
    }

    [Fact]
    public async Task EncolarAsync_AllowsTheNewReconectarGoogleTipo_From015()
    {
        var sut = new SqlCommandQueueRepository(_db.ConnectionString);
        var correlationId = Guid.NewGuid();

        await sut.EncolarAsync("RECONECTAR_GOOGLE", referencia: null, payload: "{}", correlationId, CancellationToken.None);

        var cantidad = await _db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.CommandQueue WHERE CorrelationId = '{correlationId}' AND Tipo = 'RECONECTAR_GOOGLE';");
        Assert.Equal(1, cantidad);
    }
}
