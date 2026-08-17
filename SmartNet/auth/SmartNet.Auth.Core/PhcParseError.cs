namespace SmartNet.Auth.Core;

/// <summary>
/// Typed parse failures for <see cref="PhcCodec.Parse"/>. This runs on every login attempt,
/// including against a potentially corrupted or foreign-format row — an unhandled exception on a
/// crafted input here is a DoS/information-disclosure vector, so failures are always typed,
/// never thrown (design.md, task 2.14/2.15).
/// </summary>
public enum PhcParseError
{
    /// <summary>The string does not have the PHC shape at all (wrong field count, non-numeric
    /// parameter, missing salt/hash segment, null/empty input, ...).</summary>
    Malformed,

    /// <summary>The string has the PHC shape but names an algorithm this codec does not support
    /// (e.g. a legacy/foreign format such as bcrypt or argon2i).</summary>
    UnknownAlgorithm,
}
