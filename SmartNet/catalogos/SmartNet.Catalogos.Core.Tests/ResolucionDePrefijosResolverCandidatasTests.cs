namespace SmartNet.Catalogos.Core.Tests;

/// <summary>
/// spec.md capability "resolucion-de-prefijos" / design.md Decision 1: <c>ResolverCandidatas</c>
/// receives the whole flat chart and filters leaves internally — ordinal <c>StartsWith</c>
/// matching, deduplicated by code, deterministic ascending ordinal order (REGLAS.md §3 escalón 3).
/// tasks.md 1.8/1.9. In-memory plan, no DB.
/// </summary>
public class ResolucionDePrefijosResolverCandidatasTests
{
    private static CuentaContable Hoja(string cuenta, string? ctaRefleja = null, string? ctaPuente = null) =>
        new(cuenta, cuenta, null, ctaRefleja, ctaPuente);

    private static CuentaContable Nodo(string cuenta, byte nivel) =>
        new(cuenta, cuenta, nivel, null, null);

    [Fact]
    public void SinglePrefix_ReturnsOnlyTheMatchingLeaf()
    {
        var plan = new[] { Hoja("631111"), Hoja("631112"), Hoja("601111") };

        var candidatas = ResolucionDePrefijos.ResolverCandidatas("631111", plan);

        var candidata = Assert.Single(candidatas);
        Assert.Equal("631111", candidata.Cuenta);
    }

    [Fact]
    public void MultiplePrefixes_ReturnUnionWithoutDuplicates()
    {
        // "6373" y "637" solapan: 637301 matchea ambos prefijos pero debe aparecer una sola vez.
        var plan = new[] { Hoja("637301"), Hoja("637302"), Hoja("637401") };

        var candidatas = ResolucionDePrefijos.ResolverCandidatas("6373,637", plan);

        Assert.Equal(3, candidatas.Count);
        Assert.Equal(new[] { "637301", "637302", "637401" }, candidatas.Select(c => c.Cuenta));
    }

    [Fact]
    public void PrefixWithNoMatch_ReturnsEmptyResult()
    {
        var plan = new[] { Hoja("631111") };

        var candidatas = ResolucionDePrefijos.ResolverCandidatas("999999", plan);

        Assert.Empty(candidatas);
    }

    [Fact]
    public void HierarchyNode_IsExcludedEvenIfItsCodeMatchesThePrefix()
    {
        var plan = new[] { Nodo("403", 3), Hoja("403101") };

        var candidatas = ResolucionDePrefijos.ResolverCandidatas("403", plan);

        var candidata = Assert.Single(candidatas);
        Assert.Equal("403101", candidata.Cuenta);
    }

    [Fact]
    public void Result_IsOrderedAscendingOrdinal()
    {
        var plan = new[] { Hoja("104302"), Hoja("104101"), Hoja("104201") };

        var candidatas = ResolucionDePrefijos.ResolverCandidatas("104", plan);

        Assert.Equal(new[] { "104101", "104201", "104302" }, candidatas.Select(c => c.Cuenta));
    }

    [Fact]
    public void NullPrefixes_ReturnsEmptyResult()
    {
        var plan = new[] { Hoja("631111") };

        var candidatas = ResolucionDePrefijos.ResolverCandidatas(null, plan);

        Assert.Empty(candidatas);
    }

    [Fact]
    public void NewLeafUnderAnAlreadyDeclaredPrefix_AppearsWithoutChangingTheMotivo()
    {
        var planOriginal = new[] { Hoja("104101") };
        var planConNuevaHoja = new[] { Hoja("104101"), Hoja("104999") };

        var candidatasOriginal = ResolucionDePrefijos.ResolverCandidatas("104", planOriginal);
        var candidatasConNuevaHoja = ResolucionDePrefijos.ResolverCandidatas("104", planConNuevaHoja);

        Assert.Single(candidatasOriginal);
        Assert.Equal(2, candidatasConNuevaHoja.Count);
        Assert.Contains(candidatasConNuevaHoja, c => c.Cuenta == "104999");
    }
}
