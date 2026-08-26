namespace SmartNet.Contable.Core;

/// <summary>Bloque de un asiento contable — REGLAS.md §5.</summary>
public enum Bloque
{
    Principal,
    Destino,
}

/// <summary>Naturaleza de la línea — REGLAS.md §7 invariante 5.</summary>
public enum TipoLinea
{
    D,
    H,
}

/// <summary>
/// Línea de un <see cref="AsientoContable"/> congelado (design.md Interfaces/Contracts). Espejo de
/// <c>fact.AsientoContableDetalle</c>, sin campos de ciclo de vida (esos son de #11).
/// </summary>
public sealed record LineaAsiento(
    short Orden,
    Bloque Bloque,
    TipoLinea Tipo,
    decimal Debe,
    decimal Haber,
    string? CuentaCodigo,
    string? CuentaDescripcion,
    string? CtaReflejaCodigo,
    string? CtaPuenteCodigo)
{
    /// <summary>REGLAS.md §7 invariante global 2: ninguna línea sin cuenta contable asignada.</summary>
    public bool SinCuenta => CuentaCodigo is null;
}
