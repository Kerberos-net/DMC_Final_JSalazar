namespace SmartNet.Contable.Core;

/// <summary>
/// REGLAS.md §6 / ADR 0018: <c>totalPEN</c> e <c>igvPEN</c> se anclan redondeando el original a
/// venta; <c>basePEN</c> se DERIVA de la resta, nunca de convertir la base original. El céntimo de
/// redondeo lo absorbe la cuenta de cargo, no una línea de ajuste (que no existe).
/// </summary>
public static class ConversionDeMoneda
{
    public static (decimal TotalPEN, decimal IgvPEN, decimal BasePEN) Convertir(
        decimal baseOrig, decimal igvOrig, decimal tcVenta)
    {
        var totalOrig = baseOrig + igvOrig;
        var totalPEN = Math.Round(totalOrig * tcVenta, 2, MidpointRounding.AwayFromZero);
        var igvPEN = Math.Round(igvOrig * tcVenta, 2, MidpointRounding.AwayFromZero);
        var basePEN = totalPEN - igvPEN;
        return (totalPEN, igvPEN, basePEN);
    }
}
