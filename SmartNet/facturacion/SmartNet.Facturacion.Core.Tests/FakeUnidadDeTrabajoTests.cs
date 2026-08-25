namespace SmartNet.Facturacion.Core.Tests;

/// <summary>
/// tasks.md 1.3/1.4 — design D10: <see cref="IUnidadDeTrabajo.MarcarFacturaValidadaAsync"/> es un
/// state-CAS (no version-CAS): <c>PENDIENTE_VALIDACION -&gt; VALIDADA</c> (Aplicada), ya
/// <c>VALIDADA</c> (YaValidada, reconfirmación tras reabrir — D1), o cualquier otro estado
/// (NoTransicionable, hoy solo alcanzable desde <c>DESCARTADA</c>). El doble de prueba modela "la
/// transacción ve sus propias escrituras": tras <c>Aplicada</c>, el próximo
/// <see cref="FakeUnidadDeTrabajo.CargarFacturaAsync"/> ya ve <c>Estado = VALIDADA</c> (design.md
/// File Changes, fila FakeUnidadDeTrabajo.cs).
/// </summary>
public sealed class FakeUnidadDeTrabajoTests
{
    private static FacturaPersistida Factura(string estado) => new(
        FacturaId: 100, Estado: estado, ProveedorCodigo: "P00234", RucProveedor: "20100000001",
        TipoComprobante: "01", Numero: "F001-123", TotalOrig: 118.00m, Moneda: "PEN",
        FechaEmision: new DateOnly(2026, 8, 10), Motivo: null, Afectacion: "GRAVADA",
        Version: new byte[] { 1 });

    [Fact]
    public async Task MarcarFacturaValidadaAsync_DesdePendienteValidacion_DevuelveAplicada()
    {
        var uow = new FakeUnidadDeTrabajo { FacturaACargar = Factura(FacturaPersistida.PendienteValidacion) };

        var resultado = await uow.MarcarFacturaValidadaAsync(100, CancellationToken.None);

        Assert.Equal(TransicionEstadoFactura.Aplicada, resultado);
        Assert.Contains(nameof(IUnidadDeTrabajo.MarcarFacturaValidadaAsync), uow.Llamadas);
    }

    [Fact]
    public async Task MarcarFacturaValidadaAsync_Aplicada_SeReflejaEnLaProximaCarga()
    {
        // "La transacción ve sus propias escrituras" (design D2/D9) — de lo contrario el golden
        // payload de FACTURA_VALIDADA llevaría "estado": "PENDIENTE_VALIDACION".
        var uow = new FakeUnidadDeTrabajo { FacturaACargar = Factura(FacturaPersistida.PendienteValidacion) };

        await uow.MarcarFacturaValidadaAsync(100, CancellationToken.None);
        var recargada = await uow.CargarFacturaAsync(100, CancellationToken.None);

        Assert.Equal(FacturaPersistida.Validada, recargada!.Estado);
    }

    [Fact]
    public async Task MarcarFacturaValidadaAsync_DesdeValidada_DevuelveYaValidada_YNoRompeElEstado()
    {
        // D1 — reconfirmación tras reabrir: la factura ya quedó VALIDADA, el asiento vuelve a BORRADOR.
        var uow = new FakeUnidadDeTrabajo { FacturaACargar = Factura(FacturaPersistida.Validada) };

        var resultado = await uow.MarcarFacturaValidadaAsync(100, CancellationToken.None);

        Assert.Equal(TransicionEstadoFactura.YaValidada, resultado);
        var recargada = await uow.CargarFacturaAsync(100, CancellationToken.None);
        Assert.Equal(FacturaPersistida.Validada, recargada!.Estado);
    }

    [Fact]
    public async Task MarcarFacturaValidadaAsync_DesdeDescartada_DevuelveNoTransicionable()
    {
        var uow = new FakeUnidadDeTrabajo { FacturaACargar = Factura(FacturaPersistida.Descartada) };

        var resultado = await uow.MarcarFacturaValidadaAsync(100, CancellationToken.None);

        Assert.Equal(TransicionEstadoFactura.NoTransicionable, resultado);
        var recargada = await uow.CargarFacturaAsync(100, CancellationToken.None);
        Assert.Equal(FacturaPersistida.Descartada, recargada!.Estado); // no muta el estado en el camino terminal
    }
}
