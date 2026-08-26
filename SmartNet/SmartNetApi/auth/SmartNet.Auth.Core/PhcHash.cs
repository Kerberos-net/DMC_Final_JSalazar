namespace SmartNet.Auth.Core;

/// <summary>
/// The decoded shape of a PHC string (<c>$argon2id$v=19$m=19456,t=2,p=1$&lt;salt&gt;$&lt;hash&gt;</c>).
/// design.md Decision 1: the codec lives in the pure core, not the Argon2 adapter — parsing a
/// text format is not infrastructure.
/// </summary>
public sealed record PhcHash(
    string Algorithm,
    int Version,
    int MemoryKib,
    int Iterations,
    int Parallelism,
    byte[] Salt,
    byte[] Hash);
