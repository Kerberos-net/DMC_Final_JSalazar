using SmartNet.Catalogos.Core;
using SmartNet.Contable.Core;
using SmartNet.TiposCambio.Core;

namespace SmartNet.Contable.Core.Tests;

/// <summary>
/// tasks.md 2.7: construction guard tests for <see cref="EntradaAsiento"/> and
/// <see cref="TipoCambioCongelado"/> — ArgumentNullException on required nulls.
/// </summary>
public class EntradaAsientoConstructionTests
{
    private static CuentaContable CuentaMotivo() =>
        new("631111", "FLETE TRASLADO DE MERCADERIA", null, "946311", "791111");

    private static TipoCambioCongelado TipoCambioVenta() =>
        TipoCambioCongelado.DeTipoCambio(new TipoCambio(
            new DateOnly(2026, 8, 12), OrigenTipoCambio.Sbs, 3.700000m, 3.712000m,
            new DateTime(2026, 8, 12, 20, 0, 0)));

    [Fact]
    public void TipoCambioCongelado_DeTipoCambio_UsaVentaNoCompra()
    {
        var tc = new TipoCambio(
            new DateOnly(2026, 8, 12), OrigenTipoCambio.Sbs, 3.700000m, 3.712000m,
            new DateTime(2026, 8, 12, 20, 0, 0));

        var congelado = TipoCambioCongelado.DeTipoCambio(tc);

        Assert.Equal(3.712000m, congelado.Venta);
    }

    [Fact]
    public void TipoCambioCongelado_DeTipoCambio_NullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => TipoCambioCongelado.DeTipoCambio(null!));
    }

    [Fact]
    public void TipoCambioCongelado_Heredado_UsaElValorRecibido()
    {
        var congelado = TipoCambioCongelado.Heredado(3.712000m);

        Assert.Equal(3.712000m, congelado.Venta);
    }

    [Fact]
    public void EntradaAsiento_CargosNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new EntradaAsiento(
            ProveedorCodigo: "P00123",
            EsRelacionada: false,
            Moneda: MonedaAsiento.Pen,
            FechaContable: new DateOnly(2026, 8, 12),
            MotivoDescripcion: "FLETE",
            Comprobante: TipoComprobante.Factura,
            Afectacion: Afectacion.Gravada,
            BaseOrig: 1000.00m,
            IgvOrig: 180.00m,
            PercepcionOrig: 0m,
            TipoCambio: TipoCambioVenta(),
            Cargos: null!,
            Herencia: null));
    }

    [Fact]
    public void EntradaAsiento_PenSinTipoCambio_NoLanza()
    {
        // REGLAS.md §6: solo aplica conversión a moneda extranjera. Un asiento en soles no tiene
        // tipo de cambio — TipoCambio nulo es un estado válido, no un error de programación.
        var entrada = new EntradaAsiento(
            ProveedorCodigo: "P00123",
            EsRelacionada: false,
            Moneda: MonedaAsiento.Pen,
            FechaContable: new DateOnly(2026, 8, 12),
            MotivoDescripcion: "FLETE",
            Comprobante: TipoComprobante.Factura,
            Afectacion: Afectacion.Gravada,
            BaseOrig: 1000.00m,
            IgvOrig: 180.00m,
            PercepcionOrig: 0m,
            TipoCambio: null,
            Cargos: new[] { new CargoSolicitado(CuentaMotivo(), 1000.00m) },
            Herencia: null);

        Assert.Null(entrada.TipoCambio);
    }

    [Fact]
    public void EntradaAsiento_ConstruccionValida_NoLanza()
    {
        var entrada = new EntradaAsiento(
            ProveedorCodigo: "P00123",
            EsRelacionada: false,
            Moneda: MonedaAsiento.Pen,
            FechaContable: new DateOnly(2026, 8, 12),
            MotivoDescripcion: "FLETE",
            Comprobante: TipoComprobante.Factura,
            Afectacion: Afectacion.Gravada,
            BaseOrig: 1000.00m,
            IgvOrig: 180.00m,
            PercepcionOrig: 0m,
            TipoCambio: TipoCambioVenta(),
            Cargos: new[] { new CargoSolicitado(CuentaMotivo(), 1000.00m) },
            Herencia: null);

        Assert.Equal("P00123", entrada.ProveedorCodigo);
    }
}
