namespace SmartNet.Auth.Core.Tests;

/// <summary>
/// design.md Decision 1 / Decision 5 ("the missing PHC codec is a benefit" — it belongs in the
/// pure core, not the Argon2 adapter). Task 2.14/2.15.
///
/// Security-relevant boundary: this runs on every login attempt, including against a
/// potentially corrupted or foreign-format row. Malformed input and an unknown algorithm MUST be
/// typed failures, never an unhandled exception — an unhandled exception on a crafted PHC string
/// during login is exactly the kind of thing that becomes a DoS or an information-disclosure bug.
/// </summary>
public class PhcCodecTests
{
    private static readonly byte[] Salt = System.Text.Encoding.ASCII.GetBytes("0123456789abcdef");
    private static readonly byte[] Hash = System.Text.Encoding.ASCII.GetBytes("fedcba9876543210fedcba9876543210");

    [Fact]
    public void Encode_ThenParse_RoundTripsEveryField()
    {
        var original = new PhcHash(
            Algorithm: "argon2id",
            Version: 19,
            MemoryKib: 19456,
            Iterations: 2,
            Parallelism: 1,
            Salt: Salt,
            Hash: Hash);

        var encoded = PhcCodec.Encode(original);
        var resultado = PhcCodec.Parse(encoded);

        Assert.True(resultado.IsSuccess);
        var parsed = resultado.Hash!;
        Assert.Equal(original.Algorithm, parsed.Algorithm);
        Assert.Equal(original.Version, parsed.Version);
        Assert.Equal(original.MemoryKib, parsed.MemoryKib);
        Assert.Equal(original.Iterations, parsed.Iterations);
        Assert.Equal(original.Parallelism, parsed.Parallelism);
        Assert.Equal(original.Salt, parsed.Salt);
        Assert.Equal(original.Hash, parsed.Hash);
    }

    [Fact]
    public void Encode_ProducesTheExactPhcShape()
    {
        var hash = new PhcHash("argon2id", 19, 19456, 2, 1, Salt, Hash);

        var encoded = PhcCodec.Encode(hash);

        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", encoded);
        Assert.Equal(5, encoded.Split('$').Length - 1); // $argon2id$v=19$m=..$salt$hash
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-phc-string-at-all")]
    [InlineData("$argon2id$")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$")] // missing salt and hash
    [InlineData("$argon2id$v=notanumber$m=19456,t=2,p=1$c2FsdA$aGFzaA")]
    [InlineData("$argon2id$v=19$m=notaparam$c2FsdA$aGFzaA")]
    [InlineData("$$$$$")]
    public void Parse_OnMalformedInput_ReturnsTypedFailure_NeverThrows(string malformed)
    {
        var resultado = PhcCodec.Parse(malformed);

        Assert.False(resultado.IsSuccess);
        Assert.Equal(PhcParseError.Malformed, resultado.Error);
        Assert.Null(resultado.Hash);
    }

    [Fact]
    public void Parse_OnNullInput_ReturnsTypedFailure_NeverThrows()
    {
        var resultado = PhcCodec.Parse(null!);

        Assert.False(resultado.IsSuccess);
        Assert.Equal(PhcParseError.Malformed, resultado.Error);
    }

    [Theory]
    [InlineData("$bcrypt$v=1$m=19456,t=2,p=1$c2FsdA$aGFzaA")]
    [InlineData("$pbkdf2-sha256$v=1$i=100000$c2FsdA$aGFzaA")]
    [InlineData("$argon2i$v=19$m=19456,t=2,p=1$c2FsdA$aGFzaA")] // legacy sibling algorithm, not argon2id
    public void Parse_OnUnknownOrUnsupportedAlgorithm_ReturnsTypedFailure_NeverThrows(string foreignFormat)
    {
        var resultado = PhcCodec.Parse(foreignFormat);

        Assert.False(resultado.IsSuccess);
        Assert.Equal(PhcParseError.UnknownAlgorithm, resultado.Error);
        Assert.Null(resultado.Hash);
    }
}
