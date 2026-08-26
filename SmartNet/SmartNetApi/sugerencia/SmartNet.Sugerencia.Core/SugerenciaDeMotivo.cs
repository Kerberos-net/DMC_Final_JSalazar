namespace SmartNet.Sugerencia.Core;

/// <summary>
/// Result of <c>CascadaDeSugerencia.SugerirMotivo</c> (design.md Interfaces/Contracts). Same
/// mechanism as <see cref="SugerenciaDeCuenta"/>, indexed only by provider (REGLAS.md §3: "El
/// mismo mecanismo, considerando solo el proveedor, sugiere el motivo") — no tier field, since
/// there is only one tier here (no catalog-wide "first motivo" concept).
/// </summary>
public sealed record SugerenciaDeMotivo(int Motivo, int Veces, int VecesDelAmbito, string Fundamento);
