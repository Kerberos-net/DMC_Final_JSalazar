namespace SmartNet.Inbox.Core.Tests;

/// <summary>
/// spec.md "Sufficient data promotes to Factura" / "Insufficient data creates no Factura" /
/// "Structural check does not weigh REGLAS.md business rules" — design D1: sufficiency is the
/// four NOT NULL Factura columns (TipoComprobante, TotalOrig, Moneda, FechaEmision) plus
/// Procesamiento.Estado='COMPLETADO'; Numero/RucProveedor absence never blocks.
/// </summary>
public class PoliticaDePromocionTests
{
    private static readonly ComprobanteExtraido Completo = new(
        TipoComprobante: "01",
        Numero: null,
        RucProveedor: null,
        NombreProveedor: "Acme SAC",
        Monto: 1180.00m,
        Moneda: "PEN",
        FechaEmision: new DateOnly(2026, 8, 10));

    private static EventoInbox EventoCon(ComprobanteExtraido? comprobante, string estado = "COMPLETADO") =>
        new(1, estado, 8, "XML", 9, "factura.xml", "application/xml", "2026/08/factura.xml", 2048,
            comprobante, Array.Empty<EvidenciaCampo>(), false,
            Array.Empty<string>(), Array.Empty<string>());

    [Fact]
    public void Decidir_PromueveCuandoLosCuatroCamposRequeridosEstanPresentes_SinNumeroNiRuc()
    {
        var decision = PoliticaDePromocion.Decidir(EventoCon(Completo));

        Assert.IsType<DecisionPromocion.Promueve>(decision);
    }

    [Theory]
    [InlineData("tipoComprobante")]
    [InlineData("monto")]
    [InlineData("moneda")]
    [InlineData("fechaEmision")]
    public void Decidir_DescartaCuandoFaltaUnCampoEstructuralmenteRequerido(string campoFaltante)
    {
        var comprobante = campoFaltante switch
        {
            "tipoComprobante" => Completo with { TipoComprobante = null },
            "monto" => Completo with { Monto = null },
            "moneda" => Completo with { Moneda = null },
            "fechaEmision" => Completo with { FechaEmision = null },
            _ => throw new ArgumentOutOfRangeException(nameof(campoFaltante)),
        };

        var decision = PoliticaDePromocion.Decidir(EventoCon(comprobante));

        var descarta = Assert.IsType<DecisionPromocion.Descarta>(decision);
        Assert.Contains(campoFaltante, descarta.Motivo);
    }

    [Fact]
    public void Decidir_DescartaCuandoComprobanteEsNulo_ProcesamientoFallido()
    {
        var decision = PoliticaDePromocion.Decidir(EventoCon(null, estado: "ERROR"));

        Assert.IsType<DecisionPromocion.Descarta>(decision);
    }

    [Fact]
    public void Decidir_NuncaPesaReglasNegocioSobreValoresPresentes()
    {
        // Valores estructuralmente completos pero contables-invalidos (p.ej. moneda inventada) NO
        // deben bloquear: REGLAS.md §1-4 se evalua en el flujo de validacion existente, no aqui.
        var comprobanteRaro = Completo with { Moneda = "ZZZ", Monto = -1m };

        var decision = PoliticaDePromocion.Decidir(EventoCon(comprobanteRaro));

        Assert.IsType<DecisionPromocion.Promueve>(decision);
    }
}
