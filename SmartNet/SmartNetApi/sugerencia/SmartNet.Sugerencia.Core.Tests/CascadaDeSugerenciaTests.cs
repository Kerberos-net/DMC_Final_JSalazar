using SmartNet.Catalogos.Core;

namespace SmartNet.Sugerencia.Core.Tests;

/// <summary>
/// spec.md capability "sugerencia-cuenta" / design.md Interfaces-Contracts, Cascade algorithm.
/// Pure in-memory fixtures — no DB/HTTP/clock (ADR 0019). tasks.md Phase 2.
/// </summary>
public class CascadaDeSugerenciaTests
{
    private static SugerenciaCuenta Uso(string proveedor, int motivo, string cuenta, int veces, DateTimeOffset ultimoUso) =>
        new(proveedor, motivo, cuenta, veces, ultimoUso);

    private static CuentaContable Candidata(string cuenta) => new(cuenta, cuenta, null, null, null);

    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Tier1_Resolves_When_ProviderSpecificHistoryExists()
    {
        var usoDelProveedorEnElMotivo = new[]
        {
            Uso("P1", 10, "601111", 3, T0),
            Uso("P1", 10, "601112", 7, T0),
        };
        var usoGlobalDelMotivo = new[] { Uso("P2", 10, "601113", 99, T0) };
        var candidatasVigentes = new[] { Candidata("601111"), Candidata("601112"), Candidata("601113") };

        var resultado = CascadaDeSugerencia.SugerirCuenta(
            usoDelProveedorEnElMotivo, usoGlobalDelMotivo, candidatasVigentes);

        Assert.NotNull(resultado);
        Assert.Equal("601112", resultado!.CuentaCodigo);
        Assert.Equal(EscalonSugerencia.ProveedorYMotivo, resultado.Escalon);
        Assert.Equal(7, resultado.Veces);
    }

    [Fact]
    public void FallsToTier2_When_ProviderHasNoHistoryForThisMotivo()
    {
        var usoDelProveedorEnElMotivo = Array.Empty<SugerenciaCuenta>();
        var usoGlobalDelMotivo = new[]
        {
            Uso("P2", 10, "601111", 4, T0),
            Uso("P3", 10, "601112", 9, T0),
        };
        var candidatasVigentes = new[] { Candidata("601111"), Candidata("601112") };

        var resultado = CascadaDeSugerencia.SugerirCuenta(
            usoDelProveedorEnElMotivo, usoGlobalDelMotivo, candidatasVigentes);

        Assert.NotNull(resultado);
        Assert.Equal("601112", resultado!.CuentaCodigo);
        Assert.Equal(EscalonSugerencia.MotivoGlobal, resultado.Escalon);
    }

    [Fact]
    public void FallsToTier3_WithoutATie_ReturnsLowestCuentaCodigo()
    {
        var candidatasVigentes = new[] { Candidata("601112"), Candidata("601111"), Candidata("601113") };

        var resultado = CascadaDeSugerencia.SugerirCuenta(
            Array.Empty<SugerenciaCuenta>(), Array.Empty<SugerenciaCuenta>(), candidatasVigentes);

        Assert.NotNull(resultado);
        Assert.Equal("601111", resultado!.CuentaCodigo);
        Assert.Equal(EscalonSugerencia.PrimeraCandidata, resultado.Escalon);
        Assert.Equal(0, resultado.Veces);
        Assert.Equal(0, resultado.VecesDelAmbito);
    }

    [Fact]
    public void FallsToTier3_IsDeterministic_RegardlessOfInputRowOrder()
    {
        var ordenA = new[] { Candidata("601113"), Candidata("601111"), Candidata("601112") };
        var ordenB = new[] { Candidata("601112"), Candidata("601113"), Candidata("601111") };

        var resultadoA = CascadaDeSugerencia.SugerirCuenta(
            Array.Empty<SugerenciaCuenta>(), Array.Empty<SugerenciaCuenta>(), ordenA);
        var resultadoB = CascadaDeSugerencia.SugerirCuenta(
            Array.Empty<SugerenciaCuenta>(), Array.Empty<SugerenciaCuenta>(), ordenB);

        Assert.Equal("601111", resultadoA!.CuentaCodigo);
        Assert.Equal(resultadoA.CuentaCodigo, resultadoB!.CuentaCodigo);
    }

    [Fact]
    public void Tier1_TieInVeces_ResolvedByUltimoUsoDescending()
    {
        var masReciente = T0.AddDays(5);
        var usoDelProveedorEnElMotivo = new[]
        {
            Uso("P1", 10, "601111", 5, T0),
            Uso("P1", 10, "601112", 5, masReciente),
        };
        var candidatasVigentes = new[] { Candidata("601111"), Candidata("601112") };

        var resultado = CascadaDeSugerencia.SugerirCuenta(
            usoDelProveedorEnElMotivo, Array.Empty<SugerenciaCuenta>(), candidatasVigentes);

        Assert.Equal("601112", resultado!.CuentaCodigo);
    }

    [Fact]
    public void Tier2_TieInVecesAndUltimoUso_ResolvedByCuentaCodigoAscending()
    {
        var usoGlobalDelMotivo = new[]
        {
            Uso("P2", 10, "601112", 5, T0),
            Uso("P3", 10, "601111", 5, T0),
        };
        var candidatasVigentes = new[] { Candidata("601111"), Candidata("601112") };

        var resultado = CascadaDeSugerencia.SugerirCuenta(
            Array.Empty<SugerenciaCuenta>(), usoGlobalDelMotivo, candidatasVigentes);

        Assert.Equal("601111", resultado!.CuentaCodigo);
    }

    [Fact]
    public void HistoricallyUsedAccount_NoLongerInLiveCandidates_IsExcluded()
    {
        // Tier-1 top-ranked row points at 601199, which fell out of ResolverCandidatas'
        // output (chart-of-accounts change / motivo deactivated) — spec.md scenario.
        var usoDelProveedorEnElMotivo = new[]
        {
            Uso("P1", 10, "601199", 20, T0),
            Uso("P1", 10, "601111", 3, T0),
        };
        var candidatasVigentes = new[] { Candidata("601111"), Candidata("601112") };

        var resultado = CascadaDeSugerencia.SugerirCuenta(
            usoDelProveedorEnElMotivo, Array.Empty<SugerenciaCuenta>(), candidatasVigentes);

        Assert.Equal("601111", resultado!.CuentaCodigo);
        Assert.Equal(EscalonSugerencia.ProveedorYMotivo, resultado.Escalon);
    }

    [Fact]
    public void FirstEverInvoiceForProvider_MotivoHasPriorGlobalHistory_FallsToTier2()
    {
        var usoGlobalDelMotivo = new[] { Uso("OTRO_PROVEEDOR", 10, "601111", 6, T0) };
        var candidatasVigentes = new[] { Candidata("601111") };

        var resultado = CascadaDeSugerencia.SugerirCuenta(
            Array.Empty<SugerenciaCuenta>(), usoGlobalDelMotivo, candidatasVigentes);

        Assert.Equal(EscalonSugerencia.MotivoGlobal, resultado!.Escalon);
        Assert.Equal("601111", resultado.CuentaCodigo);
    }

    [Fact]
    public void FirstEverInvoiceForProvider_MotivoHasNoHistoryAnywhere_FallsToTier3()
    {
        var candidatasVigentes = new[] { Candidata("601112"), Candidata("601111") };

        var resultado = CascadaDeSugerencia.SugerirCuenta(
            Array.Empty<SugerenciaCuenta>(), Array.Empty<SugerenciaCuenta>(), candidatasVigentes);

        Assert.Equal(EscalonSugerencia.PrimeraCandidata, resultado!.Escalon);
        Assert.Equal("601111", resultado.CuentaCodigo);
    }

    [Fact]
    public void EmptyCandidatasVigentes_ReturnsNull()
    {
        var resultado = CascadaDeSugerencia.SugerirCuenta(
            Array.Empty<SugerenciaCuenta>(), Array.Empty<SugerenciaCuenta>(), Array.Empty<CuentaContable>());

        Assert.Null(resultado);
    }

    [Fact]
    public void SugerirMotivo_ReturnsProvidersMostUsedMotivo()
    {
        var usoDelProveedor = new[]
        {
            Uso("P1", 10, "601111", 3, T0),
            Uso("P1", 10, "601112", 4, T0),
            Uso("P1", 20, "601200", 10, T0),
        };
        var motivosOfrecibles = new HashSet<int> { 10, 20 };

        var resultado = CascadaDeSugerencia.SugerirMotivo(usoDelProveedor, motivosOfrecibles);

        Assert.NotNull(resultado);
        Assert.Equal(20, resultado!.Motivo);
        Assert.Equal(10, resultado.Veces);
        Assert.Equal(17, resultado.VecesDelAmbito);
    }

    [Fact]
    public void SugerirMotivo_AggregatesVecesPerMotivo_BeforeComparing()
    {
        // Motivo 10 has two rows (3 + 4 = 7); motivo 20 has one row (5) — 10 must win on the
        // aggregate, not on any single row.
        var usoDelProveedor = new[]
        {
            Uso("P1", 10, "601111", 3, T0),
            Uso("P1", 10, "601112", 4, T0),
            Uso("P1", 20, "601200", 5, T0),
        };
        var motivosOfrecibles = new HashSet<int> { 10, 20 };

        var resultado = CascadaDeSugerencia.SugerirMotivo(usoDelProveedor, motivosOfrecibles);

        Assert.Equal(10, resultado!.Motivo);
        Assert.Equal(7, resultado.Veces);
    }

    [Fact]
    public void SugerirMotivo_NoHistoryForProvider_ReturnsNull()
    {
        var resultado = CascadaDeSugerencia.SugerirMotivo(
            Array.Empty<SugerenciaCuenta>(), new HashSet<int> { 10, 20 });

        Assert.Null(resultado);
    }

    [Fact]
    public void Tier1Result_ExposesUsageCounts_ForRationaleRendering()
    {
        // spec.md "Tier-1 result exposes usage counts": Veces=14 of 15 total observations for
        // (proveedor, motivo) — VecesDelAmbito is the sum of Veces over the filtered winning-tier
        // rows only (design.md Decision 3), so item #12 can render "usado 14 de 15 veces" without
        // recomputation.
        var usoDelProveedorEnElMotivo = new[]
        {
            Uso("P1", 10, "601111", 14, T0),
            Uso("P1", 10, "601112", 1, T0),
        };
        var candidatasVigentes = new[] { Candidata("601111"), Candidata("601112") };

        var resultado = CascadaDeSugerencia.SugerirCuenta(
            usoDelProveedorEnElMotivo, Array.Empty<SugerenciaCuenta>(), candidatasVigentes);

        Assert.NotNull(resultado);
        Assert.Equal(EscalonSugerencia.ProveedorYMotivo, resultado!.Escalon);
        Assert.Equal(14, resultado.Veces);
        Assert.Equal(15, resultado.VecesDelAmbito);
        Assert.False(string.IsNullOrWhiteSpace(resultado.Fundamento));
    }

    [Fact]
    public void SugerirMotivo_ExcludesMotivosNotOfrecibles()
    {
        var usoDelProveedor = new[]
        {
            Uso("P1", 10, "601111", 99, T0),
            Uso("P1", 20, "601200", 1, T0),
        };
        // Motivo 10 has the highest Veces but is not currently offerable (Activo=false or
        // OrigenLibro != "02", computed by the caller) — must not be suggested.
        var motivosOfrecibles = new HashSet<int> { 20 };

        var resultado = CascadaDeSugerencia.SugerirMotivo(usoDelProveedor, motivosOfrecibles);

        Assert.Equal(20, resultado!.Motivo);
    }
}
