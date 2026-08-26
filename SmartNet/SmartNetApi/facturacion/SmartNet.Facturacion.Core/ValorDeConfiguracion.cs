using System.Globalization;

namespace SmartNet.Facturacion.Core;

/// <summary>
/// design D6 — validación pura por <c>Tipo</c> de una escritura a <c>fact.Configuracion</c>
/// (ADR 0019: sin HTTP/DB/reloj). Mismo vocabulario cerrado que <c>CK_Configuracion_Tipo</c>
/// (007_publicacion.sql:38-39). <c>valor = null</c> siempre es válido — "usar ValorPorDefecto"
/// (007_publicacion.sql:29) — la escritura de un valor concreto queda a cargo de las reglas
/// específicas por <c>Tipo</c> de abajo.
/// </summary>
public static class ValorDeConfiguracion
{
    private const int MaxLongitudTexto = 400;
    private const string FormatoFecha = "yyyy-MM-dd";

    public static bool Validar(string tipo, string? valor)
    {
        if (valor is null)
        {
            return true;
        }

        return tipo switch
        {
            "TEXTO" => valor.Length <= MaxLongitudTexto,
            "ENTERO" => long.TryParse(valor, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            // decimal.TryParse, NUNCA float/double -- REGLAS.md exige aritmética decimal exacta.
            // NumberStyles.Number (default de decimal.TryParse) incluye AllowThousands, lo que
            // aceptaria "3,14" como 314 en InvariantCulture (coma = separador de miles) -- se
            // excluye explicitamente para que la coma nunca sea un separador silencioso.
            "DECIMAL" => decimal.TryParse(
                valor,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out _),
            "BOOLEANO" => valor is "true" or "false",
            "FECHA" => DateOnly.TryParseExact(
                valor, FormatoFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            "LISTA" => EsListaValida(valor),
            _ => throw new ArgumentOutOfRangeException(
                nameof(tipo), tipo, "Tipo de configuración desconocido (fuera del vocabulario de CK_Configuracion_Tipo)."),
        };
    }

    // D1b/D6 -- mismo criterio "ninguna vacía" que INGESTA.EXTENSIONES_PERMITIDAS (009_datos_base.sql).
    private static bool EsListaValida(string valor)
    {
        var items = valor.Split(',');
        return items.Length > 0 && items.All(item => item.Trim().Length > 0);
    }
}
