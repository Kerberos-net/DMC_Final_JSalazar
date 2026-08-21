namespace SmartNet.Contable.Core;

/// <summary>Afectación tributaria congelada del comprobante — REGLAS.md §5.</summary>
public enum Afectacion
{
    Gravada,
    Exonerada,
    Inafecta,
}

/// <summary>Tipo de comprobante — REGLAS.md §5 (01 / 03 / 07).</summary>
public enum TipoComprobante
{
    Factura,
    Boleta,
    NotaCredito,
}

/// <summary>
/// Asiento contable compuesto por <see cref="ComposicionDeAsiento.Componer"/> (design.md
/// Interfaces/Contracts). Espejo de <c>fact.AsientoContable</c>, sin campos de ciclo de vida
/// (<c>Estado</c>, <c>NumeroAsiento</c>, <c>Version</c> son de #11). Autocontenido: las líneas
/// guardan la cuenta y las cuentas de destino congeladas — REGLAS.md §5 "Por qué congeladas".
/// </summary>
public sealed record AsientoContable(
    string ProveedorCodigo,
    DateOnly FechaContable,
    string? MotivoDescripcion,
    decimal? TipoCambioVenta,
    decimal BasePEN,
    decimal IgvPEN,
    decimal NetoPEN,
    Afectacion AfectacionCongelada,
    TipoComprobante Comprobante,
    IReadOnlyList<LineaAsiento> Lineas);
