using System.Security.Cryptography;
using System.Text;

namespace SmartNet.Auth.Infrastructure.Tests;

/// <summary>
/// Task 3.5/3.6 -- <see cref="CsprngSessionTokenFactory"/> (design.md Decision 4). The token is
/// 256 random bits, Base64Url-encoded (43 chars, no padding); the stored value is
/// lowercase-hex SHA-256 of the raw token, never the token itself.
/// </summary>
public sealed class CsprngSessionTokenFactoryTests
{
    [Fact]
    public void Create_ReturnsA43CharacterBase64UrlToken()
    {
        var sut = new CsprngSessionTokenFactory();

        var (token, _) = sut.Create();

        Assert.Equal(43, token.Length);
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void Create_TokenDecodesTo256Bits()
    {
        var sut = new CsprngSessionTokenFactory();

        var (token, _) = sut.Create();

        var raw = Base64UrlDecode(token);
        Assert.Equal(32, raw.Length); // 256 bits
    }

    [Fact]
    public void Create_ReturnsADifferentToken_OnEachCall()
    {
        var sut = new CsprngSessionTokenFactory();

        var (token1, _) = sut.Create();
        var (token2, _) = sut.Create();

        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public void Create_TokenHash_IsLowercaseHexSha256_OfTheToken()
    {
        var sut = new CsprngSessionTokenFactory();

        var (token, tokenHash) = sut.Create();

        var expected = ExpectedLowercaseHexSha256(token);
        Assert.Equal(expected, tokenHash);
        Assert.Equal(64, tokenHash.Length); // SHA-256 -> 32 bytes -> 64 hex chars
        Assert.Equal(tokenHash, tokenHash.ToLowerInvariant());
    }

    [Fact]
    public void HashOf_IsDeterministic_AndMatchesCreatesHash_ForTheSameToken()
    {
        var sut = new CsprngSessionTokenFactory();
        var (token, tokenHash) = sut.Create();

        var recomputed1 = sut.HashOf(token);
        var recomputed2 = sut.HashOf(token);

        Assert.Equal(tokenHash, recomputed1);
        Assert.Equal(recomputed1, recomputed2);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }

    private static string ExpectedLowercaseHexSha256(string token)
    {
        var raw = Base64UrlDecode(token);
        var digest = SHA256.HashData(raw);
        return Convert.ToHexStringLower(digest);
    }
}
