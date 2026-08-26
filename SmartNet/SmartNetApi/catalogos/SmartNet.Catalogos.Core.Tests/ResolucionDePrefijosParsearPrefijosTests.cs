namespace SmartNet.Catalogos.Core.Tests;

/// <summary>
/// design.md Interfaces/Contracts: <c>ParsearPrefijos</c> — "Split por coma, trim, descarta
/// vacíos, deduplica (ordinal). null/"" → lista vacía." tasks.md 1.6/1.7.
/// </summary>
public class ResolucionDePrefijosParsearPrefijosTests
{
    [Fact]
    public void NullInput_ReturnsEmptyList()
    {
        var resultado = ResolucionDePrefijos.ParsearPrefijos(null);

        Assert.Empty(resultado);
    }

    [Fact]
    public void EmptyStringInput_ReturnsEmptyList()
    {
        var resultado = ResolucionDePrefijos.ParsearPrefijos("");

        Assert.Empty(resultado);
    }

    [Fact]
    public void CommaSeparatedInput_SplitsIntoPrefixes()
    {
        var resultado = ResolucionDePrefijos.ParsearPrefijos("4011,4017,4018,403,417");

        Assert.Equal(new[] { "4011", "4017", "4018", "403", "417" }, resultado);
    }

    [Fact]
    public void PrefixesWithSurroundingWhitespace_AreTrimmed()
    {
        var resultado = ResolucionDePrefijos.ParsearPrefijos(" 4011 , 4017 ,403");

        Assert.Equal(new[] { "4011", "4017", "403" }, resultado);
    }

    [Fact]
    public void EmptyTokensBetweenCommas_AreDiscarded()
    {
        var resultado = ResolucionDePrefijos.ParsearPrefijos("4011,,403,");

        Assert.Equal(new[] { "4011", "403" }, resultado);
    }

    [Fact]
    public void DuplicatePrefixes_AreDeduplicatedOrdinally()
    {
        var resultado = ResolucionDePrefijos.ParsearPrefijos("4011,403,4011,403");

        Assert.Equal(new[] { "4011", "403" }, resultado);
    }

    [Fact]
    public void SingleDeclaredPrefix_ReturnsThatOnePrefix()
    {
        var resultado = ResolucionDePrefijos.ParsearPrefijos("631111");

        Assert.Equal(new[] { "631111" }, resultado);
    }
}
