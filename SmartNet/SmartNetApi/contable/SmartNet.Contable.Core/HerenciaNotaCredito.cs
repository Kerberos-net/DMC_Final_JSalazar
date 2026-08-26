namespace SmartNet.Contable.Core;

/// <summary>
/// Los cuatro atributos que una NC con referencia interna hereda de la factura referenciada
/// (REGLAS.md §5) — pre-aplanada, nunca el <see cref="AsientoContable"/> completo (design.md
/// Decisión 2: ese asiento arrastra campos de ciclo de vida que #8 no debe leer, y #8, sin
/// repositorio, no puede obtenerlo de todas formas).
/// </summary>
public sealed record HerenciaNotaCredito(
    Afectacion AfectacionCongelada,
    TipoCambioCongelado? TipoCambioCongelado,
    IReadOnlyList<CargoSolicitado> CargosCongelados,
    string? MotivoDescripcion,
    TipoComprobante ComprobanteOriginal)
{
    /// <summary>
    /// Adaptador puro, punto de enganche explícito para #10: aplana el asiento de la factura una
    /// sola vez. #10 no lo reimplementa — #10 aporta de dónde sale ese asiento (referencia
    /// interna/externa, tope acumulado, reparto).
    /// </summary>
    public static HerenciaNotaCredito DesdeAsiento(AsientoContable factura)
    {
        ArgumentNullException.ThrowIfNull(factura);

        var cargosPrincipal = factura.Lineas
            .Where(l => l.Bloque == Bloque.Principal && l.Tipo == TipoLinea.D && l.CuentaCodigo is not null)
            .Where(l => l.CuentaCodigo != "401111" && l.CuentaCodigo != "401131")
            .Select(l => new CargoSolicitado(
                new SmartNet.Catalogos.Core.CuentaContable(
                    l.CuentaCodigo!, l.CuentaDescripcion ?? string.Empty, null,
                    l.CtaReflejaCodigo, l.CtaPuenteCodigo),
                l.Debe))
            .ToList();

        return new HerenciaNotaCredito(
            factura.AfectacionCongelada,
            factura.TipoCambioVenta is decimal venta
                ? TipoCambioCongelado.Heredado(venta)
                : null,
            cargosPrincipal,
            factura.MotivoDescripcion,
            factura.Comprobante);
    }
}
