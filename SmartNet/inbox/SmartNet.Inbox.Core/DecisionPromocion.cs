namespace SmartNet.Inbox.Core;

/// <summary>
/// Closed result of <see cref="PoliticaDePromocion.Decidir"/> (design.md Interfaces/Contracts,
/// spec.md "Pure promotion decision"). Not a bool: "insuficiente" always carries a
/// <see cref="Descarta.Motivo"/> — spec.md "MotivoDescarte describing the missing field". The
/// <c>private protected</c> constructor closes the hierarchy to these two cases only.
/// </summary>
public abstract record DecisionPromocion
{
    private protected DecisionPromocion() { }

    /// <summary>Datos estructuralmente suficientes (design D1) — el evento se promueve a Factura.</summary>
    public sealed record Promueve : DecisionPromocion;

    /// <summary>
    /// Falta un campo estructuralmente requerido, o el Procesamiento no terminó en COMPLETADO.
    /// <see cref="Motivo"/> alimenta directamente <c>InboxEvent.MotivoDescarte</c> — nunca se
    /// crea ninguna fila Factura (spec.md "Insufficient data creates no Factura").
    /// </summary>
    public sealed record Descarta(string Motivo) : DecisionPromocion;
}
