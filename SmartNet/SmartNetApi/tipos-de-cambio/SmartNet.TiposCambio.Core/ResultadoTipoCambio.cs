namespace SmartNet.TiposCambio.Core;

/// <summary>
/// Closed result of a vigente-rate lookup (design.md Decision 2, ADR 0018 pt. 3). Not
/// <c>TipoCambio?</c>: "sin tipo de cambio" is a distinct, exhaustively-handleable case, not a
/// null field. The <c>private protected</c> constructor closes the hierarchy to
/// <see cref="Vigente"/> and <see cref="SinTipoCambio"/> only — no other assembly can add a
/// third case.
/// </summary>
public abstract record ResultadoTipoCambio
{
    private protected ResultadoTipoCambio() { }

    /// <summary>A rate was found for the queried date — <see cref="Valor"/> carries the winning row.</summary>
    public sealed record Vigente(TipoCambio Valor) : ResultadoTipoCambio;

    /// <summary>
    /// No row exists for the queried date, under either origin. ADR 0018 pt. 3: the future #11
    /// endpoint translates this into a 409 — "la factura no se abre para edición".
    /// </summary>
    public sealed record SinTipoCambio(DateOnly Fecha) : ResultadoTipoCambio;
}
