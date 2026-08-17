namespace SmartNet.Auth.Core;

/// <summary>
/// Outcome of <see cref="IPasswordHasher.Verify"/>. Deliberately not a bare <see cref="bool"/>:
/// leaves room for the adapter to report a parse failure on a corrupted stored hash distinctly
/// from a genuine wrong-password result, without ever throwing (mirrors <see cref="PhcParseResult"/>'s
/// typed-failure discipline at this boundary).
/// </summary>
public enum PasswordVerification
{
    Correct,
    Incorrect,
    StoredHashUnreadable,
}
