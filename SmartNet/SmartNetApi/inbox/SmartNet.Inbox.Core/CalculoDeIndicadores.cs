namespace SmartNet.Inbox.Core;

/// <summary>
/// Pure indicator computation (design D5, ADR 0019 level 1). <paramref name="proveedorResuelto"/>
/// (<c>dbo.Proveedor</c> lookup) and <paramref name="existeIdentidadPrevia"/>
/// (<c>IX_Factura_Identidad</c>) are SELECT-only facts resolved by Infrastructure and passed in —
/// this method touches no database. <c>FechaEnDomingo</c> derives from
/// <see cref="EventoInbox.Comprobante"/>'s own <c>FechaEmision</c>, never from a clock.
/// </summary>
public static class CalculoDeIndicadores
{
    public static IndicadoresFactura Calcular(EventoInbox evento, bool proveedorResuelto, bool existeIdentidadPrevia)
    {
        var fechaEmision = evento.Comprobante?.FechaEmision;
        var fechaEnDomingo = fechaEmision is { } fecha && fecha.DayOfWeek == DayOfWeek.Sunday;

        return new IndicadoresFactura(
            EsProveedorGenerico: !proveedorResuelto,
            PosibleDuplicado: existeIdentidadPrevia,
            TieneCamposNoExtraidos: evento.CamposNoExtraidos.Count > 0,
            FechaEnDomingo: fechaEnDomingo,
            AfectacionMixta: evento.AfectacionMixta);
    }
}
