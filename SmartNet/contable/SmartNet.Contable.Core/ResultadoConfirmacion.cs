namespace SmartNet.Contable.Core;

/// <summary>
/// Jerarquía cerrada del resultado de <see cref="InvariantesDeConfirmacion.Evaluar"/> (design.md
/// Decisión 3, copia exacta de la forma de <c>ResultadoTipoCambio</c> — item #4). Un incumplimiento
/// de §7 es un resultado esperado del dominio, no una excepción. El ctor <c>private protected</c>
/// cierra la jerarquía: ningún otro ensamblado puede añadir un tercer caso.
/// </summary>
public abstract record ResultadoConfirmacion
{
    private protected ResultadoConfirmacion() { }

    /// <summary>El asiento pasó todas las invariantes evaluables y puede confirmarse.</summary>
    public sealed record Confirmable(AsientoContable Asiento) : ResultadoConfirmacion;

    /// <summary>Al menos una invariante falló — <see cref="Fallos"/> lista TODAS, no solo la primera.</summary>
    public sealed record InvariantesIncumplidas(IReadOnlyList<InvarianteIncumplida> Fallos) : ResultadoConfirmacion;
}
