namespace SmartNet.Contable.Core;

/// <summary>
/// Mapeo canonico entre el codigo SUNAT de dos caracteres que persiste
/// <c>fact.Factura.TipoComprobante</c> (01 / 03 / 07 — REGLAS.md §5) y el enum de dominio
/// <see cref="TipoComprobante"/>. Es el UNICO lugar donde vive ese conjunto de codigos:
/// <c>SqlUnidadDeTrabajo</c> y la validacion de correcciones de factura (BACKLOG #18 PR5) lo
/// comparten en vez de enumerar cada uno su propia lista.
/// </summary>
public static class CodigoComprobante
{
    private static readonly IReadOnlyDictionary<string, TipoComprobante> PorCodigo =
        new Dictionary<string, TipoComprobante>(StringComparer.Ordinal)
        {
            ["01"] = TipoComprobante.Factura,
            ["03"] = TipoComprobante.Boleta,
            ["07"] = TipoComprobante.NotaCredito,
        };

    /// <summary>Los codigos aceptados, en orden de presentacion.</summary>
    public static IReadOnlyList<string> Aceptados { get; } = new[] { "01", "03", "07" };

    /// <summary><c>true</c> si <paramref name="codigo"/> es uno de los codigos aceptados.</summary>
    public static bool EsValido(string? codigo) => codigo is not null && PorCodigo.ContainsKey(codigo);

    /// <summary>Convierte un codigo aceptado al enum de dominio; lanza si no lo es.</summary>
    public static TipoComprobante Convertir(string codigo) => PorCodigo.TryGetValue(codigo, out var tipo)
        ? tipo
        : throw new InvalidOperationException($"TipoComprobante desconocido: '{codigo}'.");
}
