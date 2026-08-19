namespace SmartNet.Inbox.Core;

/// <summary>
/// Pure sufficiency rule (design D1, ADR 0019 level 1, spec.md "Structural check does not weigh
/// REGLAS.md business rules"). Only checks presence/absence of the four <c>fact.Factura</c>
/// columns declared <c>NOT NULL</c> with no default plus <c>Procesamiento.Estado='COMPLETADO'</c>
/// — never the values themselves. <c>Numero</c>/<c>RucProveedor</c> absence never blocks (their
/// nullability is normative, 005_negocio.sql comments).
/// </summary>
public static class PoliticaDePromocion
{
    public static DecisionPromocion Decidir(EventoInbox evento)
    {
        if (evento.EstadoProcesamiento != "COMPLETADO" || evento.Comprobante is null)
        {
            return new DecisionPromocion.Descarta(
                "estadoProcesamiento no es COMPLETADO o no hay comprobante extraído");
        }

        var faltantes = new List<string>();
        var c = evento.Comprobante;

        if (string.IsNullOrEmpty(c.TipoComprobante))
        {
            faltantes.Add("tipoComprobante");
        }

        if (c.Monto is null)
        {
            faltantes.Add("monto");
        }

        if (string.IsNullOrEmpty(c.Moneda))
        {
            faltantes.Add("moneda");
        }

        if (c.FechaEmision is null)
        {
            faltantes.Add("fechaEmision");
        }

        return faltantes.Count == 0
            ? new DecisionPromocion.Promueve()
            : new DecisionPromocion.Descarta("Faltan campos requeridos: " + string.Join(", ", faltantes));
    }
}
