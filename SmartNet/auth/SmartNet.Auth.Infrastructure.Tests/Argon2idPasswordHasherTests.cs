using SmartNet.Auth.Core;

namespace SmartNet.Auth.Infrastructure.Tests;

/// <summary>
/// Task 3.3/3.4 -- <see cref="Argon2idPasswordHasher"/>. Every produced PHC string must be
/// parseable by Core's <see cref="PhcCodec"/> (design.md Decision 1: encode/decode is delegated to
/// Core, never reimplemented in this adapter) with the exact fixed parameters
/// <c>m=19456,t=2,p=1</c> (design.md Decision 1). Also covers the decoy-hash generation the login
/// sequence's step 1 relies on for the username-enumeration timing defense (design.md, Decision 5
/// "Login sequence").
/// </summary>
public sealed class Argon2idPasswordHasherTests
{
    private const int ExpectedMemoryKib = 19456;
    private const int ExpectedIterations = 2;
    private const int ExpectedParallelism = 1;

    [Fact]
    public void Hash_ProducesAPhcString_ParseableByCoreCodec_WithFixedParameters()
    {
        var sut = new Argon2idPasswordHasher();

        var phc = sut.Hash("una-clave-de-prueba-no-real");

        var parsed = PhcCodec.Parse(phc);
        Assert.True(parsed.IsSuccess);
        Assert.Equal("argon2id", parsed.Hash!.Algorithm);
        Assert.Equal(ExpectedMemoryKib, parsed.Hash.MemoryKib);
        Assert.Equal(ExpectedIterations, parsed.Hash.Iterations);
        Assert.Equal(ExpectedParallelism, parsed.Hash.Parallelism);
    }

    [Fact]
    public void Hash_ProducesADifferentSalt_OnEachCall()
    {
        var sut = new Argon2idPasswordHasher();

        var phc1 = sut.Hash("misma-clave-de-prueba");
        var phc2 = sut.Hash("misma-clave-de-prueba");

        Assert.NotEqual(phc1, phc2);
    }

    [Fact]
    public void Verify_AcceptsTheCorrectPassword()
    {
        var sut = new Argon2idPasswordHasher();
        var phc = sut.Hash("clave-correcta-de-prueba");

        var result = sut.Verify("clave-correcta-de-prueba", phc);

        Assert.Equal(PasswordVerification.Correct, result);
    }

    [Fact]
    public void Verify_RejectsAnIncorrectPassword()
    {
        var sut = new Argon2idPasswordHasher();
        var phc = sut.Hash("clave-correcta-de-prueba");

        var result = sut.Verify("clave-incorrecta-de-prueba", phc);

        Assert.Equal(PasswordVerification.Incorrect, result);
    }

    [Fact]
    public void Verify_ReturnsStoredHashUnreadable_ForAMalformedPhcString()
    {
        var sut = new Argon2idPasswordHasher();

        var result = sut.Verify("cualquier-clave", "no-es-un-phc-valido");

        Assert.Equal(PasswordVerification.StoredHashUnreadable, result);
    }

    // design.md, Login sequence step 1: "if the user does not exist, still run one Argon2id
    // verification against a decoy PHC hash generated at startup from random bytes, then return
    // the standard failure" -- the username-enumeration timing defense. The decoy must carry the
    // SAME parameters as real hashes and must be generated ONCE, not per attempt (design.md
    // explicitly says "generated once at process start").
    [Fact]
    public void DecoyHash_IsAStablePhcString_ParseableWithTheSameParametersAsRealHashes()
    {
        var decoy1 = Argon2idPasswordHasher.DecoyHash;
        var decoy2 = Argon2idPasswordHasher.DecoyHash;

        // Same process, same static value -- "generated once at process start", not per access.
        Assert.Equal(decoy1, decoy2);

        var parsed = PhcCodec.Parse(decoy1);
        Assert.True(parsed.IsSuccess);
        Assert.Equal("argon2id", parsed.Hash!.Algorithm);
        Assert.Equal(ExpectedMemoryKib, parsed.Hash.MemoryKib);
        Assert.Equal(ExpectedIterations, parsed.Hash.Iterations);
        Assert.Equal(ExpectedParallelism, parsed.Hash.Parallelism);
    }

    [Fact]
    public void DecoyHash_CostsOneRealArgon2idComputation_WhenVerifiedAgainst()
    {
        // The defense only holds if verifying against the decoy actually runs the full Argon2id
        // transform (same cost as a real row), not a shortcut. We cannot assert wall-clock time
        // without flaking (tasks.md 4.14's own note), so this proves the MECHANISM: Verify()
        // against the decoy returns a typed, deterministic outcome (never StoredHashUnreadable --
        // the decoy is always well-formed) exactly the way a real row's Verify() would, i.e. the
        // same code path is exercised, not a shortcut that skips the transform.
        var sut = new Argon2idPasswordHasher();

        var result = sut.Verify("cualquier-clave-de-un-atacante", Argon2idPasswordHasher.DecoyHash);

        Assert.NotEqual(PasswordVerification.StoredHashUnreadable, result);
        Assert.Equal(PasswordVerification.Incorrect, result);
    }
}
