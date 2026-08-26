namespace SmartNet.Contable.Core;

/// <summary>
/// Resolución determinista de la cuenta de proveedor cruzando moneda con EsRelacionada —
/// REGLAS.md §4 "Cuenta de proveedor". Siempre bajo 4212/4312 EMITIDAS.
/// </summary>
public static class CuentaDeProveedor
{
    public static string Codigo(MonedaAsiento moneda, bool esRelacionada) => (moneda, esRelacionada) switch
    {
        (MonedaAsiento.Pen, false) => "421211",
        (MonedaAsiento.Usd, false) => "421212",
        (MonedaAsiento.Pen, true) => "431211",
        (MonedaAsiento.Usd, true) => "431212",
        _ => throw new ArgumentOutOfRangeException(nameof(moneda)),
    };

    public static string Descripcion(MonedaAsiento moneda, bool esRelacionada) => (moneda, esRelacionada) switch
    {
        (MonedaAsiento.Pen, false) => "FACTURAS Y BOLETAS EN SOLES",
        (MonedaAsiento.Usd, false) => "FACTURAS Y BOLETAS EN DOLARES",
        (MonedaAsiento.Pen, true) => "FACTURAS Y BOLETAS RELAC. EN SOLES",
        (MonedaAsiento.Usd, true) => "FACTURAS Y BOLETAS RELAC. EN DOLARES",
        _ => throw new ArgumentOutOfRangeException(nameof(moneda)),
    };
}
