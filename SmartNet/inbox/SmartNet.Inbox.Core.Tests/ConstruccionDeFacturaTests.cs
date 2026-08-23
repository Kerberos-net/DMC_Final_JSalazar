namespace SmartNet.Inbox.Core.Tests;

/// <summary>
/// spec.md "Sufficient data promotes to Factura" — <c>ConstruccionDeFactura.Construir</c> builds
/// the in-memory <c>FacturaPromovida</c> (Factura fields + FacturaExtraccion rows) that
/// Infrastructure will INSERT inside one transaction (design D2/D9). Pure: no DB, HTTP, or clock.
/// </summary>
public class ConstruccionDeFacturaTests
{
    private static readonly EventoInbox Evento = new(
        1, "COMPLETADO", 8, "XML", 9, "factura.xml", "application/xml", "2026/08/factura.xml", 2048,
        new ComprobanteExtraido("01", "F001-123", "20100000001", "Acme SAC", 1180.00m, "PEN", new DateOnly(2026, 8, 10)),
        new[] { new EvidenciaCampo("total", "1180.00", "XML"), new EvidenciaCampo("moneda", "PEN", "XML") },
        false,
        new[] { "igv" },
        Array.Empty<string>());

    private static readonly IndicadoresFactura Indicadores = new(
        EsProveedorGenerico: false,
        PosibleDuplicado: false,
        TieneCamposNoExtraidos: true,
        FechaEnDomingo: false,
        AfectacionMixta: false);

    [Fact]
    public void Construir_CopiaLosCamposDelComprobanteAlaFacturaPromovida()
    {
        var factura = ConstruccionDeFactura.Construir(Evento, "P00042", Indicadores);

        Assert.Equal("P00042", factura.ProveedorCodigo);
        Assert.Equal("01", factura.TipoComprobante);
        Assert.Equal("F001-123", factura.Numero);
        Assert.Equal("20100000001", factura.RucProveedor);
        Assert.Equal(1180.00m, factura.TotalOrig);
        Assert.Equal("PEN", factura.Moneda);
        Assert.Equal(new DateOnly(2026, 8, 10), factura.FechaEmision);
    }

    [Fact]
    public void Construir_CopiaLosIndicadoresSinTocarEsReferenciaExterna()
    {
        var factura = ConstruccionDeFactura.Construir(Evento, "P00042", Indicadores);

        Assert.Equal(Indicadores, factura.Indicadores);
    }

    [Fact]
    public void Construir_MapeaCadaEvidenciaAUnaFilaFacturaExtraccion()
    {
        var factura = ConstruccionDeFactura.Construir(Evento, "P00042", Indicadores);

        Assert.Equal(2, factura.Extracciones.Count);
        Assert.Contains(factura.Extracciones, e => e.CampoNombre == "total" && e.ValorExtraido == "1180.00" && e.Fuente == "XML");
        Assert.Contains(factura.Extracciones, e => e.CampoNombre == "moneda" && e.ValorExtraido == "PEN" && e.Fuente == "XML");
    }

    [Fact]
    public void Construir_AsignaEstadoInicialPendienteValidacion()
    {
        var factura = ConstruccionDeFactura.Construir(Evento, "P00042", Indicadores);

        Assert.Equal("PENDIENTE_VALIDACION", factura.Estado);
    }
}
