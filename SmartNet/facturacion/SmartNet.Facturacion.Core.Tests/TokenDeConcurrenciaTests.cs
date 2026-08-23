using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Core.Tests;

/// <summary>
/// tasks.md 1.1/1.2 — design D2: "SHARED TokenDeConcurrencia.Codificar(byte[8]) /
/// TryDecodificar(string), pure static codec ... ETag = '"' + Base64(rowversion) + '"'."
/// No IConcurrencyToken interface — a value has no polymorphism to hide.
/// </summary>
public class TokenDeConcurrenciaTests
{
    private static readonly byte[] RowVersionA = { 0, 0, 0, 0, 0, 0, 0, 1 };
    private static readonly byte[] RowVersionB = { 0, 0, 0, 0, 0, 0, 0, 42 };

    [Fact]
    public void Codificar_WrapsBase64OfTheRowversionInQuotes()
    {
        var etag = TokenDeConcurrencia.Codificar(RowVersionA);

        Assert.Equal($"\"{Convert.ToBase64String(RowVersionA)}\"", etag);
    }

    [Fact]
    public void Codificar_ProducesADifferentTag_ForADifferentRowversion()
    {
        var etagA = TokenDeConcurrencia.Codificar(RowVersionA);
        var etagB = TokenDeConcurrencia.Codificar(RowVersionB);

        Assert.NotEqual(etagA, etagB);
    }

    [Fact]
    public void TryDecodificar_RoundTripsTheOriginalBytes_WhenTheTagWasProducedByCodificar()
    {
        var etag = TokenDeConcurrencia.Codificar(RowVersionA);

        var exito = TokenDeConcurrencia.TryDecodificar(etag, out var decodificado);

        Assert.True(exito);
        Assert.Equal(RowVersionA, decodificado);
    }

    [Fact]
    public void TryDecodificar_RoundTrips_ADifferentRowversionCorrectly()
    {
        var etag = TokenDeConcurrencia.Codificar(RowVersionB);

        var exito = TokenDeConcurrencia.TryDecodificar(etag, out var decodificado);

        Assert.True(exito);
        Assert.Equal(RowVersionB, decodificado);
    }

    [Theory]
    [InlineData("")]
    [InlineData("sin-comillas")]
    [InlineData("\"no-es-base64!!\"")]
    [InlineData("\"")]
    public void TryDecodificar_ReturnsFalse_ForAMalformedTag(string malformado)
    {
        var exito = TokenDeConcurrencia.TryDecodificar(malformado, out var decodificado);

        Assert.False(exito);
        Assert.Null(decodificado);
    }

    [Fact]
    public void TryDecodificar_ReturnsFalse_WhenTheTagIsNull()
    {
        var exito = TokenDeConcurrencia.TryDecodificar(null, out var decodificado);

        Assert.False(exito);
        Assert.Null(decodificado);
    }
}
