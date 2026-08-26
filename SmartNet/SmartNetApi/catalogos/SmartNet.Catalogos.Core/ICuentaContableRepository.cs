namespace SmartNet.Catalogos.Core;

/// <summary>
/// Port over <c>dbo.CuentaContable</c> (design.md Interfaces/Contracts). Read-only — ADR 0003
/// external catalog. Implementation lives in <c>SmartNet.Catalogos.Infrastructure</c> (Phase 2).
/// </summary>
public interface ICuentaContableRepository
{
    Task<IReadOnlyList<CuentaContable>> ListarPlanCompletoAsync(CancellationToken ct);

    Task<CuentaContable?> ObtenerAsync(string cuenta, CancellationToken ct);
}
