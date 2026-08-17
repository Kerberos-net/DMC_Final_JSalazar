using System.Collections.Concurrent;
using SmartNet.Auth.Core;
using SmartNet.Auth.Infrastructure;

namespace SmartNet.Api.Tests;

/// <summary>
/// Task 4.14's chosen mechanism: rather than a raw wall-clock comparison (flaky under CI load and
/// machine variance -- exactly what the coordinator's scope warned against), this decorator counts
/// <see cref="IPasswordHasher.Verify"/> invocations, split by whether the PHC argument was the
/// real DECOY hash (<see cref="Argon2idPasswordHasher.DecoyHash"/>) or a real stored hash. It
/// delegates every call to a REAL <see cref="Argon2idPasswordHasher"/> underneath, so
/// correct/incorrect-password behavior in the tests using it is completely genuine -- only the
/// call bookkeeping is a test double, never the cryptographic outcome.
/// </summary>
public sealed class CountingPasswordHasher : IPasswordHasher
{
    private readonly Argon2idPasswordHasher _real = new();
    private readonly ConcurrentBag<string> _verifyCallsAgainstPhc = new();

    public int DecoyVerifyCallCount => _verifyCallsAgainstPhc.Count(phc => phc == Argon2idPasswordHasher.DecoyHash);

    public int RealVerifyCallCount => _verifyCallsAgainstPhc.Count(phc => phc != Argon2idPasswordHasher.DecoyHash);

    public int TotalVerifyCallCount => _verifyCallsAgainstPhc.Count;

    public string Hash(string clave) => _real.Hash(clave);

    public PasswordVerification Verify(string clave, string phc)
    {
        _verifyCallsAgainstPhc.Add(phc);
        return _real.Verify(clave, phc);
    }
}
