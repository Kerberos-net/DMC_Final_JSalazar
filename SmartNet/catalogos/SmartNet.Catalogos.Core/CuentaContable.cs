namespace SmartNet.Catalogos.Core;

/// <summary>
/// Row of <c>dbo.CuentaContable</c> (design.md Interfaces/Contracts). <see cref="Cuenta"/> is
/// intentionally <c>string</c>, never a fixed-width type — REGLAS.md §3's prefix matching relies
/// on the unpadded value (spec.md "CuentaContable.cuenta MUST remain a variable-length type").
/// </summary>
public sealed record CuentaContable(
    string Cuenta,
    string Descripcion,
    byte? Nivel,
    string? CtaReflejaCodigo,
    string? CtaPuenteCodigo)
{
    // REGLAS.md §2: "Solo las de 6 dígitos son imputables (907)." Distinguished via nivel
    // (empty on leaves), not code length.
    public bool EsHojaImputable => Nivel is null;
}
