namespace SmartNet.Inbox.Core;

/// <summary>
/// Outcome of resolving an associated PDF's paired XML factura (design.md Decision 2). Closed
/// hierarchy, mirrors <see cref="DecisionPromocion"/>.
/// </summary>
public abstract record ResolucionPar
{
    private protected ResolucionPar() { }

    /// <summary>Query A hit: a non-<c>DESCARTADA</c> partner <c>fact.Factura</c> exists.</summary>
    public sealed record Fusionable(long FacturaId) : ResolucionPar;

    /// <summary>The partner event is <c>DESCARTADO</c>, or its <c>Factura</c> was later
    /// <c>DESCARTADA</c> by a human -- this PDF must not self-promote (design D2/D3).</summary>
    public sealed record ParNoPromovible(string Motivo) : ResolucionPar;

    /// <summary>The partner event is absent or still <c>PENDIENTE</c> -- not yet resolvable.</summary>
    public sealed record NoDisponible : ResolucionPar;
}
