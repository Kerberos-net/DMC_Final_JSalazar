using System.Security.Cryptography;
using Konscious.Security.Cryptography;
using SmartNet.Auth.Core;

namespace SmartNet.Auth.Infrastructure;

/// <summary>
/// Adapter over the raw Konscious Argon2id transform (design.md Decision 1). Encoding/decoding of
/// the PHC string is delegated entirely to Core's <see cref="PhcCodec"/> -- this class never
/// reimplements the format, only wraps the KDF that produces/verifies the raw hash bytes.
/// </summary>
public sealed class Argon2idPasswordHasher : IPasswordHasher
{
    // design.md Decision 1: m = 19456 KiB, t = 2, p = 1, 16-byte salt, 32-byte output.
    private const int MemoryKib = 19456;
    private const int Iterations = 2;
    private const int Parallelism = 1;
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int PhcVersion = 19; // v=19, Argon2's own version marker, not this package's.

    /// <summary>
    /// design.md's Login sequence step 1: a random-byte PHC string, generated ONCE at process
    /// start with the SAME parameters as real hashes -- the username-enumeration timing defense.
    /// A real static field (not a property) guarantees single computation for the process
    /// lifetime, matching "generated once at process start" literally.
    /// </summary>
    public static readonly string DecoyHash = ComputeHash(GenerateRandomPassword());

    public string Hash(string clave) => ComputeHash(clave);

    public PasswordVerification Verify(string clave, string phc)
    {
        var parsed = PhcCodec.Parse(phc);
        if (!parsed.IsSuccess)
        {
            return PasswordVerification.StoredHashUnreadable;
        }

        var stored = parsed.Hash!;
        var computed = DeriveRawHash(
            clave, stored.Salt, stored.MemoryKib, stored.Iterations, stored.Parallelism, stored.Hash.Length);

        return CryptographicOperations.FixedTimeEquals(computed, stored.Hash)
            ? PasswordVerification.Correct
            : PasswordVerification.Incorrect;
    }

    private static string ComputeHash(string clave)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var rawHash = DeriveRawHash(clave, salt, MemoryKib, Iterations, Parallelism, HashSizeBytes);

        return PhcCodec.Encode(new PhcHash("argon2id", PhcVersion, MemoryKib, Iterations, Parallelism, salt, rawHash));
    }

    private static byte[] DeriveRawHash(
        string clave, byte[] salt, int memoryKib, int iterations, int parallelism, int hashSizeBytes)
    {
        using var argon2 = new Argon2id(System.Text.Encoding.UTF8.GetBytes(clave))
        {
            Salt = salt,
            DegreeOfParallelism = parallelism,
            Iterations = iterations,
            MemorySize = memoryKib,
        };

        return argon2.GetBytes(hashSizeBytes);
    }

    // Random-byte password: nobody can ever present it, which is the point -- the decoy exists
    // only so Verify() has a real, well-formed Argon2id row to run the transform against.
    private static string GenerateRandomPassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
