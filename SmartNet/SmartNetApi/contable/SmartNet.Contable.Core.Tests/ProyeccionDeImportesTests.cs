namespace SmartNet.Contable.Core.Tests;

/// <summary>
/// BACKLOG #19 (design D3, tasks 3.1) — <see cref="ProyeccionDeImportes.Derivar"/> against the
/// REGLAS.md §10 goldens. Pure: no DB / HTTP / clock (ADR 0019).
/// </summary>
public class ProyeccionDeImportesTests
{
    [Fact]
    public void Derivar_FacturaGravadaEnSoles_SplitsBaseAndIgv()
    {
        // REGLAS.md §10.1 — base 1000.00, IGV 180.00, total 1180.00, TC venta = 1 (PEN).
        var proyeccion = ProyeccionDeImportes.Derivar(
            TipoComprobante.Factura, Afectacion.Gravada, baseOrig: 1000.00m, igvOrig: 180.00m, tcVenta: 1m);

        Assert.Equal(1000.00m, proyeccion.BasePEN);
        Assert.Equal(180.00m, proyeccion.IgvPEN);
        Assert.Equal(1180.00m, proyeccion.NetoPEN);
        Assert.Equal(proyeccion.BasePEN + proyeccion.IgvPEN, proyeccion.NetoPEN);
    }

    [Fact]
    public void Derivar_Boleta_CollapsesIgvIntoCost()
    {
        // REGLAS.md §10.2 — boleta, total 1180.00: sin línea de IGV, cargo = total.
        var proyeccion = ProyeccionDeImportes.Derivar(
            TipoComprobante.Boleta, Afectacion.Gravada, baseOrig: 1000.00m, igvOrig: 180.00m, tcVenta: 1m);

        Assert.Equal(1180.00m, proyeccion.BasePEN);
        Assert.Equal(0m, proyeccion.IgvPEN);
        Assert.Equal(1180.00m, proyeccion.NetoPEN);
    }

    [Theory]
    [InlineData("EXONERADA")]
    [InlineData("INAFECTA")]
    public void Derivar_FacturaNoGravada_CollapsesIgvIntoCost(string afectacion)
    {
        var afec = afectacion == "EXONERADA" ? Afectacion.Exonerada : Afectacion.Inafecta;

        var proyeccion = ProyeccionDeImportes.Derivar(
            TipoComprobante.Factura, afec, baseOrig: 1000.00m, igvOrig: 0m, tcVenta: 1m);

        Assert.Equal(1000.00m, proyeccion.BasePEN);
        Assert.Equal(0m, proyeccion.IgvPEN);
        Assert.Equal(1000.00m, proyeccion.NetoPEN);
    }

    [Fact]
    public void Derivar_FacturaEnDolares_AnclaTotalEIgvYDerivaBase()
    {
        // REGLAS.md §10.3 — base 1000.00, IGV 180.00 (USD), TC venta 3.7895.
        var proyeccion = ProyeccionDeImportes.Derivar(
            TipoComprobante.Factura, Afectacion.Gravada, baseOrig: 1000.00m, igvOrig: 180.00m, tcVenta: 3.7895m);

        Assert.Equal(4471.61m, proyeccion.NetoPEN);
        Assert.Equal(682.11m, proyeccion.IgvPEN);
        Assert.Equal(3789.50m, proyeccion.BasePEN);
        Assert.Equal(proyeccion.BasePEN + proyeccion.IgvPEN, proyeccion.NetoPEN);
    }

    [Fact]
    public void Derivar_NotaCreditoDelCienPorCientoEnDolares_DejaElPasivoEnCeroExacto()
    {
        // REGLAS.md §10.7 — base 10000.00, IGV 1525.42 (USD), TC heredado 3.712000.
        var proyeccion = ProyeccionDeImportes.Derivar(
            TipoComprobante.NotaCredito, Afectacion.Gravada, baseOrig: 10000.00m, igvOrig: 1525.42m, tcVenta: 3.712000m);

        Assert.Equal(42782.36m, proyeccion.NetoPEN);
        Assert.Equal(5662.36m, proyeccion.IgvPEN);
        Assert.Equal(37120.00m, proyeccion.BasePEN);
    }

    [Fact]
    public void Derivar_NotaCreditoSobreBoleta_NoTieneLineaDeIgv()
    {
        // REGLAS.md §10.6 — NC 07 sobre boleta: afectación congelada no GRAVADA -> IGV al costo.
        var proyeccion = ProyeccionDeImportes.Derivar(
            TipoComprobante.NotaCredito, Afectacion.Inafecta, baseOrig: 118.00m, igvOrig: 0m, tcVenta: 1m);

        Assert.Equal(118.00m, proyeccion.BasePEN);
        Assert.Equal(0m, proyeccion.IgvPEN);
        Assert.Equal(118.00m, proyeccion.NetoPEN);
    }

    [Fact]
    public void Derivar_TypeIsStaticAndPure_LivesInContableCore()
    {
        // tasks 3.3 — el escaneo de pureza es asembly-wide (PurityScanTests); este test documenta
        // que ProyeccionDeImportes pertenece a SmartNet.Contable.Core y no toma dependencias de infra.
        var tipo = typeof(ProyeccionDeImportes);
        Assert.True(tipo.IsAbstract && tipo.IsSealed, "ProyeccionDeImportes debe ser una clase estática.");
        Assert.Equal("SmartNet.Contable.Core", tipo.Assembly.GetName().Name);
    }
}
