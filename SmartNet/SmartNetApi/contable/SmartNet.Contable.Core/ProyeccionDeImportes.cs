namespace SmartNet.Contable.Core;

/// <summary>
/// BACKLOG #19 (design D3) — derivación PURA (ADR 0019: sin BD/HTTP/reloj) de los tres escalares
/// que <c>fact.AsientoContable</c> persiste (<c>BasePEN</c>, <c>IgvPEN</c>, <c>NetoPEN</c>) a partir
/// de los importes originales del comprobante y el tipo de cambio VENTA aplicable.
///
/// Cubre REGLAS.md §6 (conversión, delegada en <see cref="ConversionDeMoneda.Convertir"/>) MÁS §5
/// (la boleta <c>03</c> y la factura EXONERADA / INAFECTA no otorgan crédito fiscal: el IGV se
/// incorpora al costo, de modo que <c>IgvPEN = 0</c> y <c>BasePEN = NetoPEN = total convertido</c>).
/// <c>Convertir</c> por sí sola es §6 y dejaría un <c>IgvPEN</c> fantasma en una boleta.
///
/// <c>NetoPEN = BasePEN + IgvPEN</c> se cumple SIEMPRE por construcción (verificado contra los
/// goldens §10.1 / §10.2 / §10.3 / §10.7). Para un comprobante en soles el llamador pasa
/// <paramref name="tcVenta"/> = <c>1</c>.
/// </summary>
public static class ProyeccionDeImportes
{
    public static ProyeccionEscalar Derivar(
        TipoComprobante comprobante, Afectacion afectacion, decimal baseOrig, decimal igvOrig, decimal tcVenta)
    {
        // REGLAS.md §5: crédito fiscal solo cuando la afectación congelada es GRAVADA y el
        // comprobante no es una boleta. Cualquier otro caso colapsa el IGV al costo.
        var otorgaCreditoFiscal = comprobante != TipoComprobante.Boleta && afectacion == Afectacion.Gravada;

        if (otorgaCreditoFiscal)
        {
            var (totalPEN, igvPEN, basePEN) = ConversionDeMoneda.Convertir(baseOrig, igvOrig, tcVenta);
            return new ProyeccionEscalar(basePEN, igvPEN, totalPEN);
        }

        var totalOrig = baseOrig + igvOrig;
        var netoPEN = Math.Round(totalOrig * tcVenta, 2, MidpointRounding.AwayFromZero);
        return new ProyeccionEscalar(netoPEN, 0m, netoPEN);
    }
}

/// <summary>
/// BACKLOG #19 (design D3) — los tres escalares de <c>fact.AsientoContable</c> que
/// <see cref="ProyeccionDeImportes.Derivar"/> produce. <c>NetoPEN == BasePEN + IgvPEN</c> siempre.
/// </summary>
public readonly record struct ProyeccionEscalar(decimal BasePEN, decimal IgvPEN, decimal NetoPEN);
