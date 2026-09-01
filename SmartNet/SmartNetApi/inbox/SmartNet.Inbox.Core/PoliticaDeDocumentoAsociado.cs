namespace SmartNet.Inbox.Core;

/// <summary>
/// Pure routing policy for the paired-PDF merge branch (design.md Decision 1, ADR 0019 level 1).
/// Runs BEFORE <see cref="PoliticaDePromocion.Decidir"/> in <c>PromocionBackgroundService</c>.
/// </summary>
public static class PoliticaDeDocumentoAsociado
{
    /// <summary>
    /// Decision 1: the predicate is <c>DocumentoAsociadoId != null AND TipoDocumento == "PDF"</c>,
    /// never <c>DocumentoAsociadoId != null</c> alone -- <c>asociar_documentos</c> writes the FK on
    /// BOTH paired rows (XML and PDF), so the naive predicate would defer the XML side forever and
    /// nothing would ever create the <c>Factura</c>. "Paired + PDF" uniquely identifies the
    /// secondary side; the XML stays primary (ADR 0017).
    /// </summary>
    public static bool EsDocumentoAsociado(EventoInbox evento) =>
        evento.DocumentoAsociadoId is not null && evento.TipoDocumento == "PDF";

    /// <summary>Pure 1:1 map from partner resolution to the merge/defer/discard decision.</summary>
    public static DecisionDocumentoAsociado Decidir(ResolucionPar resolucion) => resolucion switch
    {
        ResolucionPar.Fusionable fusionable => new DecisionDocumentoAsociado.Fusiona(fusionable.FacturaId),
        ResolucionPar.NoDisponible => new DecisionDocumentoAsociado.Difiere(),
        ResolucionPar.ParNoPromovible noPromovible => new DecisionDocumentoAsociado.Descarta(noPromovible.Motivo),
        _ => throw new ArgumentOutOfRangeException(nameof(resolucion)),
    };
}
