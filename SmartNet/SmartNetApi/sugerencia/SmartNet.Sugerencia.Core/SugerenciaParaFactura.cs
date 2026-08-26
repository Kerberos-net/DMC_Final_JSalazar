using SmartNet.Catalogos.Core;

namespace SmartNet.Sugerencia.Core;

/// <summary>
/// Combined result exposed by the orchestration method (<c>ServicioDeSugerencia</c>, PR 2 /
/// design.md Interfaces/Contracts) so item #11 can invoke a single call without re-implementing
/// orchestration (spec.md "An orchestration method exposes cuenta + motivo + fundamento").
/// </summary>
public sealed record SugerenciaParaFactura(
    SugerenciaDeMotivo? Motivo,
    SugerenciaDeCuenta? Cuenta,
    IReadOnlyList<CuentaContable> CandidatasVigentes);
