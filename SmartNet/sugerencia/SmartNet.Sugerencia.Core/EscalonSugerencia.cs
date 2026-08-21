namespace SmartNet.Sugerencia.Core;

/// <summary>
/// Tier reached by <c>CascadaDeSugerencia.SugerirCuenta</c> (REGLAS.md §3, ADR 0011 rev. 4,
/// design.md Interfaces/Contracts). Exposed as data so the rationale (<c>Fundamento</c>) can be
/// rendered without recomputation (spec.md "Every suggestion carries an auditable rationale").
/// </summary>
public enum EscalonSugerencia
{
    ProveedorYMotivo = 1,
    MotivoGlobal = 2,
    PrimeraCandidata = 3,
}
