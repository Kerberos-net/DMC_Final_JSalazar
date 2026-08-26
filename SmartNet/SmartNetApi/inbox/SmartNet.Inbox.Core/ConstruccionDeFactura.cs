namespace SmartNet.Inbox.Core;

/// <summary>
/// Pure builder (design.md Interfaces/Contracts, ADR 0019 level 1) — called only after
/// <see cref="PoliticaDePromocion.Decidir"/> already returned <see cref="DecisionPromocion.Promueve"/>;
/// assumes <see cref="EventoInbox.Comprobante"/>'s structurally-required fields are present.
/// </summary>
public static class ConstruccionDeFactura
{
    public static FacturaPromovida Construir(EventoInbox e, string proveedorCodigo, IndicadoresFactura indicadores)
    {
        var c = e.Comprobante!;
        var extracciones = e.Evidencia
            .Select(ev => new FacturaExtraccionPromovida(ev.Campo, ev.Valor, ev.Fuente))
            .ToList();

        return new FacturaPromovida(
            ProveedorCodigo: proveedorCodigo,
            TipoComprobante: c.TipoComprobante!,
            Numero: c.Numero,
            RucProveedor: c.RucProveedor,
            TotalOrig: c.Monto!.Value,
            Moneda: c.Moneda!,
            FechaEmision: c.FechaEmision!.Value,
            Indicadores: indicadores,
            Extracciones: extracciones,
            Estado: "PENDIENTE_VALIDACION");
    }
}
