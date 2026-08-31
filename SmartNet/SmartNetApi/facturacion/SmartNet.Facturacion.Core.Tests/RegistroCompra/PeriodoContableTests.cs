using SmartNet.Facturacion.Core.RegistroCompra;

namespace SmartNet.Facturacion.Core.Tests.RegistroCompra;

/// <summary>
/// spec registro-compra-api req 1 / design D2: <c>PeriodoContable(int Anio, int Mes)</c> is a pure
/// Core value with <c>TryParse("YYYY-MM")</c>. No clock, no infra — PurityScan-guarded. The adapter
/// derives the half-open <c>[primerDia, primerDiaMesSiguiente)</c> range; the value type itself
/// only parses and validates. tasks.md 1.1/1.2.
/// </summary>
public class PeriodoContableTests
{
    [Fact]
    public void TryParse_AcceptsWellFormedYearMonth()
    {
        var ok = PeriodoContable.TryParse("2026-08", out var periodo);

        Assert.True(ok);
        Assert.NotNull(periodo);
        Assert.Equal(2026, periodo!.Value.Anio);
        Assert.Equal(8, periodo.Value.Mes);
    }

    [Theory]
    [InlineData("2026-13")]   // month out of range
    [InlineData("2026-00")]   // month out of range
    [InlineData("agosto")]    // not numeric
    [InlineData("2026-8")]    // month not zero-padded / wrong width
    [InlineData("2026/08")]   // wrong separator
    [InlineData("26-08")]     // year wrong width
    [InlineData("2026-08-01")] // extra component
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_RejectsMalformedInput(string? entrada)
    {
        var ok = PeriodoContable.TryParse(entrada, out var periodo);

        Assert.False(ok);
        Assert.Null(periodo);
    }

    [Fact]
    public void RecordEquality_IsValueBased()
    {
        PeriodoContable.TryParse("2026-08", out var a);
        PeriodoContable.TryParse("2026-08", out var b);
        PeriodoContable.TryParse("2026-09", out var c);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
