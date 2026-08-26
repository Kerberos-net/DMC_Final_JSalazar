namespace SmartNet.Catalogos.Core;

/// <summary>
/// Port over <c>fact.SugerenciaCuenta</c> (design.md Interfaces/Contracts). Storage access only —
/// no method ranks, sorts, or selects a single "best" candidate (spec.md, design.md Decision 2:
/// that logic belongs to item #9). <see cref="RegistrarUsoAsync"/> receives the instant as a
/// parameter, never <c>SYSUTCDATETIME()</c> inside the adapter, so #9 stays deterministic to test.
/// </summary>
public interface ISugerenciaCuentaRepository
{
    Task<IReadOnlyList<SugerenciaCuenta>> ListarPorProveedorYMotivoAsync(
        string proveedorCodigo, int motivo, CancellationToken ct);

    Task<IReadOnlyList<SugerenciaCuenta>> ListarPorMotivoAsync(int motivo, CancellationToken ct);

    Task<IReadOnlyList<SugerenciaCuenta>> ListarPorProveedorAsync(string proveedorCodigo, CancellationToken ct);

    Task RegistrarUsoAsync(
        string proveedorCodigo, int motivo, string cuentaCodigo, DateTimeOffset instante, CancellationToken ct);
}
