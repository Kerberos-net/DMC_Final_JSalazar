namespace SmartNet.Facturacion.Core.RegistroCompra;

/// <summary>
/// spec registro-compra-api req 1 / design D2 — an accounting period expressed as a calendar month.
/// Pure Core value (no clock, no infra): it only parses and validates the <c>YYYY-MM</c> wire form.
/// The SQL adapter turns it into a half-open <c>[primerDia, primerDiaMesSiguiente)</c> range; this
/// type never touches <see cref="System.DateTime"/> ambient members, so PurityScanTests stays green.
/// </summary>
public readonly record struct PeriodoContable(int Anio, int Mes)
{
    /// <summary>
    /// Parses the exact form <c>YYYY-MM</c> (four-digit year, dash, two-digit month 01-12).
    /// Anything else — wrong width, wrong separator, non-numeric, month out of range, extra
    /// components, blank or null — yields <c>false</c> and a null <paramref name="periodo"/>.
    /// The endpoint maps a <c>false</c> result to a 400 RFC 9457 problem-details response.
    /// </summary>
    public static bool TryParse(string? texto, out PeriodoContable? periodo)
    {
        periodo = null;

        if (string.IsNullOrEmpty(texto) || texto.Length != 7 || texto[4] != '-')
        {
            return false;
        }

        var anioTexto = texto.AsSpan(0, 4);
        var mesTexto = texto.AsSpan(5, 2);

        if (!int.TryParse(anioTexto, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var anio)
            || !int.TryParse(mesTexto, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var mes))
        {
            return false;
        }

        if (mes is < 1 or > 12)
        {
            return false;
        }

        periodo = new PeriodoContable(anio, mes);
        return true;
    }
}
