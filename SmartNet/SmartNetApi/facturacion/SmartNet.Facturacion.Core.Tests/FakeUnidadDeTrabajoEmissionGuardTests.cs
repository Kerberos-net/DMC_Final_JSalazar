namespace SmartNet.Facturacion.Core.Tests;

/// <summary>
/// tasks.md 2.8 / design D8 — a lo sumo un evento por <c>(Tipo, FacturaId)</c> por transacción.
/// <see cref="FakeUnidadDeTrabajo"/> es el mirror de prueba de la guarda que
/// <c>SqlUnidadDeTrabajo.EmitirOutboxAsync</c> implementa contra la base real (Phase 3): fail-loud
/// dentro de la transacción, nunca un dedupe silencioso.
/// </summary>
public sealed class FakeUnidadDeTrabajoEmissionGuardTests
{
    [Fact]
    public async Task EmitirOutboxAsync_SameTipoAndFacturaId_Twice_Throws()
    {
        var uow = new FakeUnidadDeTrabajo();

        await uow.EmitirOutboxAsync("FACTURA_VALIDADA", 100, "{}", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => uow.EmitirOutboxAsync("FACTURA_VALIDADA", 100, "{}", CancellationToken.None));
    }

    [Fact]
    public async Task EmitirOutboxAsync_DifferentTipo_SameFacturaId_DoesNotThrow()
    {
        var uow = new FakeUnidadDeTrabajo();

        await uow.EmitirOutboxAsync("FACTURA_VALIDADA", 100, "{}", CancellationToken.None);
        await uow.EmitirOutboxAsync("DOCUMENTACION_ACTUALIZADA", 100, "{}", CancellationToken.None);

        Assert.Equal(2, uow.EventosOutbox.Count);
    }

    [Fact]
    public async Task EmitirOutboxAsync_SameTipo_DifferentFacturaId_DoesNotThrow()
    {
        var uow = new FakeUnidadDeTrabajo();

        await uow.EmitirOutboxAsync("FACTURA_VALIDADA", 100, "{}", CancellationToken.None);
        await uow.EmitirOutboxAsync("FACTURA_VALIDADA", 200, "{}", CancellationToken.None);

        Assert.Equal(2, uow.EventosOutbox.Count);
    }
}
