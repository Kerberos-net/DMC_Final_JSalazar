namespace SmartNet.Catalogos.Core.Tests;

/// <summary>
/// REGLAS.md §3's 5 worked examples, against the real `SmartNet/SmartNetBD/fixtures/data/CuentaContable.csv`
/// (1650 rows, `|`-delimited, no header) — linked into this test project as `fixtures/CuentaContable.csv`
/// (see the .csproj). Pure, no DB (design.md Testing Strategy, "Unit (golden)"). tasks.md 1.13/1.14.
///
/// Exact prefixes and expected counts per motivo — confirmed in WU0 (Phase 0 gate, tasks.md 0.1–0.3)
/// against the real fixture, not re-derived here:
///   motivo 22 → "631111"                                        → 1  candidata
///   motivo 48 → "6373"                                           → 6  candidatas
///   motivo  6 → "104"                                             → 20 candidatas
///   motivo 70 → "16"                                              → 34 candidatas
///   motivo  8 → "4011,4017,4018,403,417,167101,1674"               → 22 candidatas
/// </summary>
public class ResolucionDePrefijosGoldenTests
{
    private static readonly Lazy<IReadOnlyList<CuentaContable>> PlanDeCuentas = new(CargarPlanDeCuentasDesdeFixture);

    private static IReadOnlyList<CuentaContable> CargarPlanDeCuentasDesdeFixture()
    {
        var rutaFixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "CuentaContable.csv");
        var filas = new List<CuentaContable>();

        foreach (var linea in File.ReadLines(rutaFixture))
        {
            if (linea.Length == 0)
            {
                continue;
            }

            var campos = linea.Split('|');

            var cuenta = campos[0];
            var descripcion = campos[1];
            byte? nivel = campos[2].Length == 0 ? null : byte.Parse(campos[2]);
            string? ctaRefleja = campos.Length > 3 && campos[3].Length > 0 ? campos[3] : null;
            string? ctaPuente = campos.Length > 4 && campos[4].Length > 0 ? campos[4] : null;

            filas.Add(new CuentaContable(cuenta, descripcion, nivel, ctaRefleja, ctaPuente));
        }

        return filas;
    }

    [Fact]
    public void FixtureLoads1650RowsWith907Leaves()
    {
        var plan = PlanDeCuentas.Value;

        Assert.Equal(1650, plan.Count);
        Assert.Equal(907, plan.Count(c => c.EsHojaImputable));
    }

    [Theory]
    [InlineData("631111", 1)] // motivo 22
    [InlineData("6373", 6)] // motivo 48
    [InlineData("104", 20)] // motivo 6
    [InlineData("16", 34)] // motivo 70
    [InlineData("4011,4017,4018,403,417,167101,1674", 22)] // motivo 8
    public void GoldenExamples_MatchReglasMdSeccion3(string prefijosDeclarados, int candidatasEsperadas)
    {
        var candidatas = ResolucionDePrefijos.ResolverCandidatas(prefijosDeclarados, PlanDeCuentas.Value);

        Assert.Equal(candidatasEsperadas, candidatas.Count);
        Assert.All(candidatas, c => Assert.True(c.EsHojaImputable));
    }
}
