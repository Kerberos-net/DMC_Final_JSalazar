namespace SmartNet.Sugerencia.Core;

/// <summary>
/// Result of <c>CascadaDeSugerencia.SugerirCuenta</c> (design.md Interfaces/Contracts).
/// <see cref="VecesDelAmbito"/> is the denominator for <see cref="Fundamento"/>: the sum of
/// <c>Veces</c> over the rows that survived the <c>ResolverCandidatas</c> filter in the winning
/// tier only (design.md Decision 3) — never all stored history, so the fraction shown always maps
/// to accounts the assistant can currently act on.
/// </summary>
public sealed record SugerenciaDeCuenta(
    string CuentaCodigo,
    EscalonSugerencia Escalon,
    int Veces,
    int VecesDelAmbito,
    string Fundamento);
