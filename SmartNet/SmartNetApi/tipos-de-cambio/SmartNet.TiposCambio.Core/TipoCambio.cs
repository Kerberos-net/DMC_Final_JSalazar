namespace SmartNet.TiposCambio.Core;

/// <summary>
/// Origin of a <c>fact.TipoCambio</c> row (CHECK <c>CK_TipoCambio_Origen</c>). Composite PK
/// <c>(Fecha, Origen)</c> — see item #1's <c>007_publicacion.sql</c>.
/// </summary>
public enum OrigenTipoCambio
{
    Sbs,
    Manual,
}

/// <summary>
/// Row of <c>fact.TipoCambio</c> (design.md Interfaces/Contracts). <see cref="Compra"/> and
/// <see cref="Venta"/> are both <c>NOT NULL DECIMAL(12,6)</c> in the DDL; the accounting
/// consumer (#8) reads <see cref="Venta"/> only — ADR 0018 pt. 1, a pasivo converts at venta.
/// <see cref="FechaConsulta"/> is received as a parameter, never <c>DateTime.UtcNow</c>
/// (ADR 0019) — see PurityScanTests.
/// </summary>
public sealed record TipoCambio(
    DateOnly Fecha,
    OrigenTipoCambio Origen,
    decimal Compra,
    decimal Venta,
    DateTime FechaConsulta);
