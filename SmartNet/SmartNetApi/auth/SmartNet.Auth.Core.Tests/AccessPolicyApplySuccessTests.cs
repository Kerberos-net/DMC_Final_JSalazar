namespace SmartNet.Auth.Core.Tests;

/// <summary>
/// design.md Decision 8: "ApplySuccess sets IntentosFallidos = 0, BloqueadoHasta = NULL, and
/// NivelBloqueo = 0." ADR 0007 Revisión 4: "cualquier éxito | 0 | 0 | olvidado" — the lockout is
/// forgotten entirely, not merely decremented (task 2.12).
/// </summary>
public class AccessPolicyApplySuccessTests
{
    [Fact]
    public void ApplySuccess_ClearsAllThreeLockoutFields_RegardlessOfPriorState()
    {
        var ahora = DateTimeOffset.Parse("2026-08-16T10:00:00Z");
        var estado = new UsuarioCredentialState(
            UsuarioId: 1,
            NombreUsuario: "contador",
            ClaveHash: "hash",
            IntentosFallidos: 4,
            NivelBloqueo: 3,
            BloqueadoHasta: ahora.AddMinutes(120),
            Activo: true);

        var resultado = AccessPolicy.ApplySuccess(estado);

        Assert.Equal(0, resultado.IntentosFallidos);
        Assert.Null(resultado.BloqueadoHasta);
        Assert.Equal(0, resultado.NivelBloqueo);
    }

    [Fact]
    public void ApplySuccess_PreservesIdentityFields_ChangesOnlyLockoutState()
    {
        var estado = new UsuarioCredentialState(1, "contador", "hash", 2, 1, null, true);

        var resultado = AccessPolicy.ApplySuccess(estado);

        Assert.Equal(estado.UsuarioId, resultado.UsuarioId);
        Assert.Equal(estado.NombreUsuario, resultado.NombreUsuario);
        Assert.Equal(estado.ClaveHash, resultado.ClaveHash);
        Assert.Equal(estado.Activo, resultado.Activo);
    }

    [Fact]
    public void ApplySuccess_OnAlreadyCleanAccount_IsIdempotent()
    {
        var estado = new UsuarioCredentialState(1, "contador", "hash", 0, 0, null, true);

        var resultado = AccessPolicy.ApplySuccess(estado);

        Assert.Equal(estado, resultado);
    }
}
