namespace SmartNet.Catalogos.Core.Tests;

/// <summary>
/// design.md Interfaces/Contracts: <c>EsCandidata</c> — true for a leaf matching a declared
/// prefix, false for a hierarchy node or a non-matching leaf. tasks.md 1.10/1.11.
/// </summary>
public class ResolucionDePrefijosEsCandidataTests
{
    private static CuentaContable Hoja(string cuenta) => new(cuenta, cuenta, null, null, null);

    private static CuentaContable Nodo(string cuenta, byte nivel) => new(cuenta, cuenta, nivel, null, null);

    [Fact]
    public void LeafMatchingDeclaredPrefix_IsCandidata()
    {
        var plan = new[] { Hoja("631111") };

        var esCandidata = ResolucionDePrefijos.EsCandidata("631111", "631111", plan);

        Assert.True(esCandidata);
    }

    [Fact]
    public void HierarchyNodeMatchingPrefix_IsNotCandidata()
    {
        var plan = new[] { Nodo("403", 3), Hoja("403101") };

        var esCandidata = ResolucionDePrefijos.EsCandidata("403", "403", plan);

        Assert.False(esCandidata);
    }

    [Fact]
    public void NonMatchingLeaf_IsNotCandidata()
    {
        var plan = new[] { Hoja("631111"), Hoja("601111") };

        var esCandidata = ResolucionDePrefijos.EsCandidata("601111", "631111", plan);

        Assert.False(esCandidata);
    }
}
