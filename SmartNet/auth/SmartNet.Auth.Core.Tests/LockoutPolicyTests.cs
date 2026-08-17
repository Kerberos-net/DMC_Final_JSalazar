namespace SmartNet.Auth.Core.Tests;

/// <summary>
/// ADR 0007 Revisión 4: "cinco fallos consecutivos arman un bloqueo. La duración crece ...
/// 15 → 30 → 60 → 120 minutos, duplicando desde la base y con techo en 120."
/// design.md Decision 8 pins the same numbers as <c>LockoutPolicy.Adr0007</c>.
/// </summary>
public class LockoutPolicyTests
{
    [Fact]
    public void Adr0007_HasTheNormativeThresholdBaseDurationFactorAndCeiling()
    {
        var politica = SmartNet.Auth.Core.LockoutPolicy.Adr0007;

        Assert.Equal(5, politica.UmbralFallos);
        Assert.Equal(TimeSpan.FromMinutes(15), politica.DuracionBase);
        Assert.Equal(2, politica.Factor);
        Assert.Equal(3, politica.NivelMaximo);
    }
}
