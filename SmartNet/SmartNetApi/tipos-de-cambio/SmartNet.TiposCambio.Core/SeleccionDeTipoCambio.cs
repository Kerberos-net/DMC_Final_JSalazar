namespace SmartNet.TiposCambio.Core;

/// <summary>
/// Pure SBS>MANUAL priority rule (design.md Decision 1, ADR 0019 level 1). Takes the candidate
/// rows for a single date as input — no DB, HTTP, or system clock. Verified by PurityScanTests.
/// </summary>
public static class SeleccionDeTipoCambio
{
    /// <summary>
    /// SBS gana; MANUAL es el respaldo; origen desconocido o fecha distinta se descartan. An
    /// unexpected <see cref="OrigenTipoCambio"/> value (impossible under
    /// <c>CK_TipoCambio_Origen</c>, possible under a future schema edit) must never become a
    /// frozen rate.
    /// </summary>
    public static ResultadoTipoCambio Seleccionar(DateOnly fecha, IReadOnlyList<TipoCambio> candidatas)
    {
        TipoCambio? sbs = null;
        TipoCambio? manual = null;

        foreach (var candidata in candidatas)
        {
            if (candidata.Fecha != fecha)
            {
                continue;
            }

            switch (candidata.Origen)
            {
                case OrigenTipoCambio.Sbs:
                    sbs = candidata;
                    break;
                case OrigenTipoCambio.Manual:
                    manual = candidata;
                    break;
                default:
                    // Unknown Origen — discarded, never selected.
                    break;
            }
        }

        var ganadora = sbs ?? manual;

        return ganadora is null
            ? new ResultadoTipoCambio.SinTipoCambio(fecha)
            : new ResultadoTipoCambio.Vigente(ganadora);
    }
}
