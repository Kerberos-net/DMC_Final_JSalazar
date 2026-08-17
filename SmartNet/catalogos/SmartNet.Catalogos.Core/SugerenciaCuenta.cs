namespace SmartNet.Catalogos.Core;

/// <summary>
/// Row of <c>fact.SugerenciaCuenta</c> (`004_satelites_datos_maestros.sql`). Composite key
/// (<see cref="ProveedorCodigo"/>, <see cref="Motivo"/>, <see cref="CuentaCodigo"/>). Storage
/// access only — no ranking/sorting/single-best-candidate selection (spec.md, design.md
/// Decision 2 — that is item #9's job).
/// </summary>
public sealed record SugerenciaCuenta(
    string ProveedorCodigo,
    int Motivo,
    string CuentaCodigo,
    int Veces,
    DateTimeOffset UltimoUso);
