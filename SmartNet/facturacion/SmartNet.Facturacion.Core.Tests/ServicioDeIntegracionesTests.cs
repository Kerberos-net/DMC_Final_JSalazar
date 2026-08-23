using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Core.Tests;

/// <summary>
/// tasks.md 1.5/1.6 — design D7: "sincronizar/reconectar/reprocesar enqueue only". No audit row,
/// no direct Python call — INSERT fact.CommandQueue only, and the "estado" read is passthrough.
/// </summary>
public class ServicioDeIntegracionesTests
{
    [Fact]
    public async Task EncolarAsync_WritesExactlyOneCommandQueueRow_WithTheGivenCorrelationId()
    {
        var commandQueue = new FakeCommandQueueRepository();
        var estados = new FakeEstadoIntegracionRepository();
        var sut = new ServicioDeIntegraciones(commandQueue, estados);
        var correlationId = Guid.NewGuid();

        await sut.EncolarAsync("SINCRONIZAR_GMAIL", referencia: null, payload: "{}", correlationId, CancellationToken.None);

        var encolado = Assert.Single(commandQueue.Encolados);
        Assert.Equal("SINCRONIZAR_GMAIL", encolado.Tipo);
        Assert.Equal(correlationId, encolado.CorrelationId);
    }

    [Fact]
    public async Task EncolarAsync_PassesTheReferenciaThrough_ForReprocesarDocumento()
    {
        var commandQueue = new FakeCommandQueueRepository();
        var estados = new FakeEstadoIntegracionRepository();
        var sut = new ServicioDeIntegraciones(commandQueue, estados);

        await sut.EncolarAsync("REPROCESAR_DOCUMENTO", referencia: 42, payload: "{}", Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(42, commandQueue.Encolados[0].Referencia);
    }

    [Fact]
    public async Task ObtenerEstadoAsync_ReturnsExactlyWhatTheRepositoryLists_NoTransformation()
    {
        var commandQueue = new FakeCommandQueueRepository();
        var estados = new FakeEstadoIntegracionRepository
        {
            Estados = new[]
            {
                new EstadoIntegracion("GMAIL", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, 0),
                new EstadoIntegracion("SBS", DateTimeOffset.UtcNow, null, "timeout", 3),
            },
        };
        var sut = new ServicioDeIntegraciones(commandQueue, estados);

        var resultado = await sut.ObtenerEstadoAsync(CancellationToken.None);

        Assert.Equal(2, resultado.Count);
        Assert.Equal("SBS", resultado[1].Nombre);
        Assert.Equal(3, resultado[1].FallosConsecutivos);
    }
}
