namespace SmartNet.Facturacion.Core.Tests;

/// <summary>
/// tasks.md 5.1/5.2 — design D6: <c>ValorDeConfiguracion.Validar(tipo, valor)</c> is a PURE Core
/// type (no HTTP/DB, ADR 0019) mirroring <c>CK_Configuracion_Tipo</c>
/// (007_publicacion.sql:38-39): TEXTO/ENTERO/DECIMAL/BOOLEANO/FECHA/LISTA. <c>valor = null</c> is
/// always legal ("use ValorPorDefecto", 007_publicacion.sql:29).
/// </summary>
public class ValorDeConfiguracionTests
{
    // --- valor = null is always legal, regardless of Tipo ---

    [Theory]
    [InlineData("TEXTO")]
    [InlineData("ENTERO")]
    [InlineData("DECIMAL")]
    [InlineData("BOOLEANO")]
    [InlineData("FECHA")]
    [InlineData("LISTA")]
    public void Validar_ReturnsTrue_WhenValorIsNull_RegardlessOfTipo(string tipo)
    {
        Assert.True(ValorDeConfiguracion.Validar(tipo, null));
    }

    // --- TEXTO: <= 400 chars (NVARCHAR(400), 007_publicacion.sql:30) ---

    [Fact]
    public void Validar_Texto_AcceptsAShortString()
    {
        Assert.True(ValorDeConfiguracion.Validar("TEXTO", "hola"));
    }

    [Fact]
    public void Validar_Texto_AcceptsExactly400Chars()
    {
        Assert.True(ValorDeConfiguracion.Validar("TEXTO", new string('a', 400)));
    }

    [Fact]
    public void Validar_Texto_Rejects401Chars()
    {
        Assert.False(ValorDeConfiguracion.Validar("TEXTO", new string('a', 401)));
    }

    // --- ENTERO: long.TryParse invariant ---

    [Theory]
    [InlineData("0")]
    [InlineData("-42")]
    [InlineData("9223372036854775807")]
    public void Validar_Entero_AcceptsAnIntegerLiteral(string valor)
    {
        Assert.True(ValorDeConfiguracion.Validar("ENTERO", valor));
    }

    [Theory]
    [InlineData("3.14")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("1,5")]
    public void Validar_Entero_RejectsANonIntegerLiteral(string valor)
    {
        Assert.False(ValorDeConfiguracion.Validar("ENTERO", valor));
    }

    // --- DECIMAL: decimal.TryParse invariant, never float ---

    [Theory]
    [InlineData("3.14")]
    [InlineData("-0.5")]
    [InlineData("42")]
    public void Validar_Decimal_AcceptsADecimalLiteral(string valor)
    {
        Assert.True(ValorDeConfiguracion.Validar("DECIMAL", valor));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("3,14")]
    public void Validar_Decimal_RejectsANonDecimalLiteral(string valor)
    {
        Assert.False(ValorDeConfiguracion.Validar("DECIMAL", valor));
    }

    // --- BOOLEANO: canonical "true"/"false" only ---

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public void Validar_Booleano_AcceptsTheCanonicalLiterals(string valor)
    {
        Assert.True(ValorDeConfiguracion.Validar("BOOLEANO", valor));
    }

    [Theory]
    [InlineData("True")]
    [InlineData("FALSE")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("si")]
    public void Validar_Booleano_RejectsAnyNonCanonicalLiteral(string valor)
    {
        Assert.False(ValorDeConfiguracion.Validar("BOOLEANO", valor));
    }

    // --- FECHA: exact yyyy-MM-dd ---

    [Fact]
    public void Validar_Fecha_AcceptsAnIsoDate()
    {
        Assert.True(ValorDeConfiguracion.Validar("FECHA", "2026-08-25"));
    }

    [Theory]
    [InlineData("2026/08/25")]
    [InlineData("25-08-2026")]
    [InlineData("2026-8-25")]
    [InlineData("2026-13-01")]
    [InlineData("no-fecha")]
    public void Validar_Fecha_RejectsAnythingNotExactlyIsoFormat(string valor)
    {
        Assert.False(ValorDeConfiguracion.Validar("FECHA", valor));
    }

    // --- LISTA: comma-separated items, none empty (D1b/D6, INGESTA.EXTENSIONES_PERMITIDAS precedent) ---

    [Fact]
    public void Validar_Lista_AcceptsCommaSeparatedNonEmptyItems()
    {
        Assert.True(ValorDeConfiguracion.Validar("LISTA", "a@x.com,b@x.com"));
    }

    [Fact]
    public void Validar_Lista_AcceptsASingleItem()
    {
        Assert.True(ValorDeConfiguracion.Validar("LISTA", "a@x.com"));
    }

    [Theory]
    [InlineData("a@x.com,")]
    [InlineData(",a@x.com")]
    [InlineData("a@x.com,,b@x.com")]
    [InlineData("")]
    [InlineData("   ")]
    public void Validar_Lista_RejectsAnyEmptyItem(string valor)
    {
        Assert.False(ValorDeConfiguracion.Validar("LISTA", valor));
    }

    // --- unknown Tipo: defensive, mirrors CK_Configuracion_Tipo's closed vocabulary ---

    [Fact]
    public void Validar_ThrowsForAnUnknownTipo()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ValorDeConfiguracion.Validar("DESCONOCIDO", "x"));
    }
}
