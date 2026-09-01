namespace SmartNet.Inbox.Core.Tests;

/// <summary>
/// design.md Decision 1 -- branch predicate is <c>DocumentoAsociadoId != null AND
/// TipoDocumento == "PDF"</c>, never <c>DocumentoAsociadoId != null</c> alone (that broken version
/// would defer the XML side forever too, since <c>asociar_documentos</c> writes the FK on both
/// paired rows). <see cref="PoliticaDeDocumentoAsociado.Decidir"/> is a pure 1:1 map from
/// <see cref="ResolucionPar"/> to <see cref="DecisionDocumentoAsociado"/>.
/// </summary>
public class PoliticaDeDocumentoAsociadoTests
{
    private static EventoInbox EventoCon(string tipoDocumento, long? documentoAsociadoId) =>
        new(1, "COMPLETADO", 8, tipoDocumento, documentoAsociadoId, "factura.pdf", "application/pdf",
            "2026/08/factura.pdf", 2048, null, Array.Empty<EvidenciaCampo>(), null,
            Array.Empty<string>(), Array.Empty<string>());

    [Fact]
    public void EsDocumentoAsociado_EsVerdadero_CuandoEsPdfConDocumentoAsociadoId()
    {
        var evento = EventoCon("PDF", 2);

        Assert.True(PoliticaDeDocumentoAsociado.EsDocumentoAsociado(evento));
    }

    /// <summary>Decision 1 regression guard: an XML event ALSO carries the FK (it's the paired
    /// primary side) -- the predicate must not fire for it, or nothing would ever promote.</summary>
    [Fact]
    public void EsDocumentoAsociado_EsFalso_CuandoEsXmlConDocumentoAsociadoId()
    {
        var evento = EventoCon("XML", 2);

        Assert.False(PoliticaDeDocumentoAsociado.EsDocumentoAsociado(evento));
    }

    [Fact]
    public void EsDocumentoAsociado_EsFalso_CuandoEsPdfSinDocumentoAsociadoId()
    {
        var evento = EventoCon("PDF", null);

        Assert.False(PoliticaDeDocumentoAsociado.EsDocumentoAsociado(evento));
    }

    [Fact]
    public void Decidir_MapeaFusionable_AFusiona()
    {
        var decision = PoliticaDeDocumentoAsociado.Decidir(new ResolucionPar.Fusionable(FacturaId: 42));

        var fusiona = Assert.IsType<DecisionDocumentoAsociado.Fusiona>(decision);
        Assert.Equal(42, fusiona.FacturaId);
    }

    [Fact]
    public void Decidir_MapeaNoDisponible_ADifiere()
    {
        var decision = PoliticaDeDocumentoAsociado.Decidir(new ResolucionPar.NoDisponible());

        Assert.IsType<DecisionDocumentoAsociado.Difiere>(decision);
    }

    [Fact]
    public void Decidir_MapeaParNoPromovible_ADescartaConElMismoMotivo()
    {
        var decision = PoliticaDeDocumentoAsociado.Decidir(
            new ResolucionPar.ParNoPromovible(Motivo: "socio descartado"));

        var descarta = Assert.IsType<DecisionDocumentoAsociado.Descarta>(decision);
        Assert.Equal("socio descartado", descarta.Motivo);
    }
}
