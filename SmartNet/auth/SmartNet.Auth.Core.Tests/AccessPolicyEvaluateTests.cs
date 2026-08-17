using Microsoft.Extensions.Time.Testing;

namespace SmartNet.Auth.Core.Tests;

/// <summary>
/// design.md Login sequence step 2: "AccessPolicy.Evaluate(estado, ahora). If Locked, return the
/// standard failure without verifying the password." spec.md "A login attempt while
/// BloqueadoHasta is still in the future is rejected without evaluating the password."
///
/// FakeTimeProvider drives every `ahora` — no real clock anywhere in this suite (ADR 0019).
/// </summary>
public class AccessPolicyEvaluateTests
{
    private static readonly FakeTimeProvider Reloj = new(DateTimeOffset.Parse("2026-08-16T10:00:00Z"));

    [Fact]
    public void BloqueadoHastaInTheFuture_IsLocked()
    {
        var ahora = Reloj.GetUtcNow();
        var estado = new SmartNet.Auth.Core.UsuarioCredentialState(
            1, "u", "hash", 0, 1, ahora.AddMinutes(15), true);

        var decision = SmartNet.Auth.Core.AccessPolicy.Evaluate(estado, ahora);

        Assert.Equal(SmartNet.Auth.Core.AccessDecision.Locked, decision);
    }

    [Fact]
    public void BloqueadoHastaNull_IsNotLocked()
    {
        var ahora = Reloj.GetUtcNow();
        var estado = new SmartNet.Auth.Core.UsuarioCredentialState(
            1, "u", "hash", 0, 0, null, true);

        var decision = SmartNet.Auth.Core.AccessPolicy.Evaluate(estado, ahora);

        Assert.Equal(SmartNet.Auth.Core.AccessDecision.Allowed, decision);
    }

    [Fact]
    public void BloqueadoHastaInThePast_IsNotLocked()
    {
        var ahora = Reloj.GetUtcNow();
        var estado = new SmartNet.Auth.Core.UsuarioCredentialState(
            1, "u", "hash", 0, 1, ahora.AddMinutes(-1), true);

        var decision = SmartNet.Auth.Core.AccessPolicy.Evaluate(estado, ahora);

        Assert.Equal(SmartNet.Auth.Core.AccessDecision.Allowed, decision);
    }

    [Fact]
    public void BloqueadoHastaExactlyNow_IsNotLocked()
    {
        // Boundary: the lock has just expired the instant `ahora` equals it, not the instant
        // after — "BloqueadoHasta in the future" (spec.md) is a strict comparison.
        var ahora = Reloj.GetUtcNow();
        var estado = new SmartNet.Auth.Core.UsuarioCredentialState(
            1, "u", "hash", 0, 1, ahora, true);

        var decision = SmartNet.Auth.Core.AccessPolicy.Evaluate(estado, ahora);

        Assert.Equal(SmartNet.Auth.Core.AccessDecision.Allowed, decision);
    }
}
