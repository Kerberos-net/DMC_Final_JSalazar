namespace SmartNet.Contable.Core;

/// <summary>
/// Un incumplimiento puntual de <see cref="InvarianteContable"/> con los importes en conflicto
/// (design.md Decisión 3). No lleva código HTTP: traducir a 409/412/422 es de #11.
/// </summary>
public sealed record InvarianteIncumplida(
    InvarianteContable Invariante,
    decimal? ImporteEsperado,
    decimal? ImporteReal,
    string Detalle);
