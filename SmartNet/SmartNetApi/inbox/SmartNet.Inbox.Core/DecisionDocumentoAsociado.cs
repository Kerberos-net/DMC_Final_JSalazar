namespace SmartNet.Inbox.Core;

/// <summary>
/// Decision for an associated-document (paired PDF) event (design.md Decision 4). Closed
/// hierarchy, mirrors <see cref="DecisionPromocion"/>. Never a flag on <c>PromoverAsync</c> --
/// merging is its own concern with no meaning for <see cref="ResultadoPromocion"/>.
/// </summary>
public abstract record DecisionDocumentoAsociado
{
    private protected DecisionDocumentoAsociado() { }

    /// <summary>Project this event's document onto the already-promoted partner <c>Factura</c>.</summary>
    public sealed record Fusiona(long FacturaId) : DecisionDocumentoAsociado;

    /// <summary>Leave the event <c>PENDIENTE</c>; the partner is not yet resolvable (self-heals).</summary>
    public sealed record Difiere : DecisionDocumentoAsociado;

    /// <summary>The partner will never resolve -- discard, mirrors <see cref="DecisionPromocion.Descarta"/>.</summary>
    public sealed record Descarta(string Motivo) : DecisionDocumentoAsociado;
}
