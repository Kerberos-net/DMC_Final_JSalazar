using Microsoft.Extensions.Time.Testing;

namespace SmartNet.Auth.Core.Tests;

/// <summary>
/// design.md Decision 8, "Worked sequence" table, and ADR 0007 Revisión 4's own worked table
/// (both normative, and identical on every row they share). Task 2.10.
///
/// ADR 0007 Revisión 4's table:
/// | Evento                           | IntentosFallidos | NivelBloqueo | Bloqueo |
/// | fallo 5                          | 0                 | 1            | 15 min  |
/// | fallo 6 tras expirar (el margen) | 1                 | 1            | ninguno |
/// | fallo 10                         | 0                 | 2            | 30 min  |
/// | fallo 15                         | 0                 | 3            | 60 min  |
/// | fallo 20                         | 0                 | 3 (techo)    | 120 min |
/// | cualquier éxito                  | 0                 | 0            | olvidado|
///
/// design.md's worked sequence carries the same rows plus the 21-25 saturation-under-pressure
/// tail, which this suite also exercises (failure 25 stays at 120 min / NivelBloqueo=3).
/// </summary>
public class AccessPolicyApplyFailureTests
{
    private static readonly FakeTimeProvider Reloj = new(DateTimeOffset.Parse("2026-08-16T10:00:00Z"));
    private static readonly LockoutPolicy Politica = LockoutPolicy.Adr0007;

    private static UsuarioCredentialState FreshAccount() =>
        new(1, "contador", "hash", 0, 0, null, true);

    /// <summary>
    /// The full 1-25 lifetime-failure sequence, run in one continuous stateful walk — exactly
    /// how the real column values evolve, attempt after attempt, with no success anywhere.
    /// Each row is checked against the exact expectation from ADR 0007 Revisión 4 / design.md
    /// Decision 8's worked table. `ahora` advances realistically: it stays put across the
    /// margin failures (attacker retries immediately after the lock's advertised expiry) so
    /// `BloqueadoHasta` is provably in the past without a real clock (ADR 0019 — FakeTimeProvider
    /// only).
    /// </summary>
    [Fact]
    public void FullLifetimeSequence_1Through25_MatchesAdr0007Revision4WorkedTableExactly()
    {
        var estado = FreshAccount();
        var ahora = Reloj.GetUtcNow();

        // Failures 1-4: no lock armed, IntentosFallidos climbs, NivelBloqueo untouched.
        for (var fallo = 1; fallo <= 4; fallo++)
        {
            estado = AccessPolicy.ApplyFailure(estado, Politica, ahora);
            Assert.Equal(fallo, estado.IntentosFallidos);
            Assert.Equal(0, estado.NivelBloqueo);
            Assert.Null(estado.BloqueadoHasta);
        }

        // Failure 5 (5/5): arms lock A at 15 min. IntentosFallidos resets to 0 AT ARMING
        // (ADR 0007 Revisión 4: "se resetea al armar el bloqueo, no al expirar").
        estado = AccessPolicy.ApplyFailure(estado, Politica, ahora);
        Assert.Equal(0, estado.IntentosFallidos);
        Assert.Equal(1, estado.NivelBloqueo);
        Assert.Equal(ahora + TimeSpan.FromMinutes(15), estado.BloqueadoHasta);

        // Lock A ages out purely by comparing BloqueadoHasta to a later `ahora` — no job runs,
        // nothing is written (design.md Decision 8: "no hay evento de expiración").
        ahora += TimeSpan.FromMinutes(16);

        // Failure 6 (1/5) — THE MARGIN. Must NOT re-lock. NivelBloqueo stays at 1 (escalation
        // survives the margin — this is the entire reason the two-counter design exists).
        estado = AccessPolicy.ApplyFailure(estado, Politica, ahora);
        Assert.Equal(1, estado.IntentosFallidos);
        Assert.Equal(1, estado.NivelBloqueo);
        Assert.True(estado.BloqueadoHasta < ahora, "Failure 6 (the margin) must not re-lock the account.");

        // Failures 7-9 (2/5 .. 4/5): still no re-lock, NivelBloqueo unchanged.
        for (var posicionEnVentana = 2; posicionEnVentana <= 4; posicionEnVentana++)
        {
            estado = AccessPolicy.ApplyFailure(estado, Politica, ahora);
            Assert.Equal(posicionEnVentana, estado.IntentosFallidos);
            Assert.Equal(1, estado.NivelBloqueo);
        }

        // Failure 10 (5/5): arms lock B at 30 min. NivelBloqueo 1 -> 2. This is the row that
        // catches an off-by-one in the "read NivelBloqueo before the saturating increment" rule:
        // duracion = DuracionBase * Factor^min(NivelBloqueo=1, NivelMaximo=3) = 15 * 2^1 = 30.
        estado = AccessPolicy.ApplyFailure(estado, Politica, ahora);
        Assert.Equal(0, estado.IntentosFallidos);
        Assert.Equal(2, estado.NivelBloqueo);
        Assert.Equal(ahora + TimeSpan.FromMinutes(30), estado.BloqueadoHasta);

        ahora += TimeSpan.FromMinutes(31);

        // Failures 11-14: margin again, NivelBloqueo stays 2.
        for (var posicionEnVentana = 1; posicionEnVentana <= 4; posicionEnVentana++)
        {
            estado = AccessPolicy.ApplyFailure(estado, Politica, ahora);
            Assert.Equal(posicionEnVentana, estado.IntentosFallidos);
            Assert.Equal(2, estado.NivelBloqueo);
        }

        // Failure 15: arms lock C at 60 min. NivelBloqueo 2 -> 3. The tier-3 boundary: this MUST
        // land at failure 15, not 10 (one tier early) or 20 (one tier late) — the canonical
        // off-by-one this suite is built to catch.
        estado = AccessPolicy.ApplyFailure(estado, Politica, ahora);
        Assert.Equal(0, estado.IntentosFallidos);
        Assert.Equal(3, estado.NivelBloqueo);
        Assert.Equal(ahora + TimeSpan.FromMinutes(60), estado.BloqueadoHasta);

        ahora += TimeSpan.FromMinutes(61);

        // Failures 16-19: margin, NivelBloqueo stays 3.
        for (var posicionEnVentana = 1; posicionEnVentana <= 4; posicionEnVentana++)
        {
            estado = AccessPolicy.ApplyFailure(estado, Politica, ahora);
            Assert.Equal(posicionEnVentana, estado.IntentosFallidos);
            Assert.Equal(3, estado.NivelBloqueo);
        }

        // Failure 20: arms lock D at 120 min. NivelBloqueo STAYS at 3 — saturated, the cap.
        // duracion = 15 * 2^min(NivelBloqueo=3, NivelMaximo=3) = 15 * 8 = 120.
        estado = AccessPolicy.ApplyFailure(estado, Politica, ahora);
        Assert.Equal(0, estado.IntentosFallidos);
        Assert.Equal(3, estado.NivelBloqueo);
        Assert.Equal(ahora + TimeSpan.FromMinutes(120), estado.BloqueadoHasta);

        ahora += TimeSpan.FromMinutes(121);

        // Failures 21-24: margin under the cap.
        for (var posicionEnVentana = 1; posicionEnVentana <= 4; posicionEnVentana++)
        {
            estado = AccessPolicy.ApplyFailure(estado, Politica, ahora);
            Assert.Equal(posicionEnVentana, estado.IntentosFallidos);
            Assert.Equal(3, estado.NivelBloqueo);
        }

        // Failure 25: STILL 120 min — the cap holds under repeated, sustained pressure, not just
        // once at failure 20.
        estado = AccessPolicy.ApplyFailure(estado, Politica, ahora);
        Assert.Equal(0, estado.IntentosFallidos);
        Assert.Equal(3, estado.NivelBloqueo);
        Assert.Equal(ahora + TimeSpan.FromMinutes(120), estado.BloqueadoHasta);
    }

    [Fact]
    public void Failures1Through4_DoNotArmALock()
    {
        var estado = FreshAccount();
        var ahora = Reloj.GetUtcNow();

        for (var fallo = 1; fallo <= 4; fallo++)
        {
            estado = AccessPolicy.ApplyFailure(estado, Politica, ahora);
        }

        Assert.Equal(4, estado.IntentosFallidos);
        Assert.Equal(0, estado.NivelBloqueo);
        Assert.Null(estado.BloqueadoHasta);
    }

    [Fact]
    public void Failure5_ArmsA15MinuteLock_AdvancesNivelBloqueoTo1_AndResetsIntentosFallidosAtArmingNotExpiry()
    {
        var estado = FreshAccount();
        var ahora = Reloj.GetUtcNow();

        for (var fallo = 1; fallo <= 5; fallo++)
        {
            estado = AccessPolicy.ApplyFailure(estado, Politica, ahora);
        }

        Assert.Equal(0, estado.IntentosFallidos);
        Assert.Equal(1, estado.NivelBloqueo);
        Assert.Equal(ahora + TimeSpan.FromMinutes(15), estado.BloqueadoHasta);
    }

    /// <summary>
    /// design.md: "Assert the duration formula reads NivelBloqueo BEFORE the saturating
    /// increment." Isolated from the sequential walk: an account hand-set to NivelBloqueo=2
    /// (as if it had already survived two prior locks) must produce a 60-minute duration on its
    /// next arming (15 * 2^2 = 60), not 120 (which would be 15 * 2^3, i.e. reading the value
    /// AFTER a hypothetical increment).
    /// </summary>
    [Fact]
    public void ApplyFailure_ReadsNivelBloqueoBeforeTheSaturatingIncrement_WhenArming()
    {
        var ahora = Reloj.GetUtcNow();
        var estado = new UsuarioCredentialState(1, "u", "hash", 4, 2, null, true);

        var resultado = AccessPolicy.ApplyFailure(estado, Politica, ahora);

        Assert.Equal(ahora + TimeSpan.FromMinutes(60), resultado.BloqueadoHasta);
        Assert.Equal(3, resultado.NivelBloqueo);
    }

    [Fact]
    public void NivelBloqueoSaturatedAt3_StaysAt3_AndDurationStaysAt120_UnderRepeatedArmings()
    {
        var ahora = Reloj.GetUtcNow();
        var estado = new UsuarioCredentialState(1, "u", "hash", 4, 3, null, true);

        for (var arming = 0; arming < 3; arming++)
        {
            estado = AccessPolicy.ApplyFailure(estado, Politica, ahora);
            Assert.Equal(3, estado.NivelBloqueo);
            Assert.Equal(ahora + TimeSpan.FromMinutes(120), estado.BloqueadoHasta);

            ahora += TimeSpan.FromMinutes(121);
            for (var posicionEnVentana = 1; posicionEnVentana <= 4; posicionEnVentana++)
            {
                estado = AccessPolicy.ApplyFailure(estado, Politica, ahora);
            }
        }
    }
}
