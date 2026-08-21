using SmartNet.TiposCambio.Core;

namespace SmartNet.Contable.Core;

/// <summary>
/// Envoltorio de una sola línea sobre el tipo de cambio venta ya congelado (design.md Decisión 1).
/// Un <c>decimal</c> desnudo dejaría pasar <c>Compra</c> sin que nada lo notara en el call site —
/// ADR 0018 pt. 1: un pasivo en moneda extranjera se convierte a venta, nunca a compra.
/// </summary>
public sealed record TipoCambioCongelado
{
    public decimal Venta { get; }

    private TipoCambioCongelado(decimal venta)
    {
        Venta = venta;
    }

    /// <summary>Lee <c>TipoCambio.Venta</c>, nunca <c>.Compra</c> — ADR 0018 pt. 1.</summary>
    public static TipoCambioCongelado DeTipoCambio(TipoCambio tc)
    {
        ArgumentNullException.ThrowIfNull(tc);
        return new TipoCambioCongelado(tc.Venta);
    }

    /// <summary>
    /// NC con referencia interna: hereda el TC congelado de la factura referenciada, nunca calcula
    /// el de su propia fecha — REGLAS.md §6 "La nota de crédito hereda el tipo de cambio de su
    /// factura".
    /// </summary>
    public static TipoCambioCongelado Heredado(decimal ventaCongelada) =>
        new(ventaCongelada);
}
