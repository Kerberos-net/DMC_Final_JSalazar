using System.Text.RegularExpressions;

namespace SmartNet.Auth.Core;

/// <summary>
/// Encodes/parses the PHC string format used to persist Argon2id hashes
/// (<c>$argon2id$v=19$m=19456,t=2,p=1$&lt;salt&gt;$&lt;hash&gt;</c>), pure and infrastructure-free
/// (design.md Decision 1 — "the missing PHC codec is a benefit"; Decision 5). Every failure is a
/// typed <see cref="PhcParseResult"/>, never an exception — this runs on every login attempt,
/// including against a potentially corrupted or foreign-format row.
/// </summary>
public static class PhcCodec
{
    private const string SupportedAlgorithm = "argon2id";

    private static readonly Regex ParamsPattern =
        new(@"^m=(\d+),t=(\d+),p=(\d+)$", RegexOptions.Compiled);

    public static string Encode(PhcHash hash) =>
        $"${hash.Algorithm}$v={hash.Version}$m={hash.MemoryKib},t={hash.Iterations},p={hash.Parallelism}" +
        $"${Convert.ToBase64String(hash.Salt)}${Convert.ToBase64String(hash.Hash)}";

    public static PhcParseResult Parse(string? phc)
    {
        if (string.IsNullOrEmpty(phc))
        {
            return PhcParseResult.Fail(PhcParseError.Malformed);
        }

        // Expect exactly: "" $ algorithm $ v=<n> $ m=..,t=..,p=.. $ salt $ hash
        var parts = phc.Split('$');
        if (parts.Length != 6 || parts[0].Length != 0)
        {
            return PhcParseResult.Fail(PhcParseError.Malformed);
        }

        var algorithm = parts[1];
        var versionSegment = parts[2];
        var paramsSegment = parts[3];
        var saltSegment = parts[4];
        var hashSegment = parts[5];

        if (algorithm.Length == 0)
        {
            return PhcParseResult.Fail(PhcParseError.Malformed);
        }

        if (!algorithm.Equals(SupportedAlgorithm, StringComparison.Ordinal))
        {
            return PhcParseResult.Fail(PhcParseError.UnknownAlgorithm);
        }

        if (!versionSegment.StartsWith("v=", StringComparison.Ordinal) ||
            !int.TryParse(versionSegment.AsSpan(2), out var version))
        {
            return PhcParseResult.Fail(PhcParseError.Malformed);
        }

        var paramsMatch = ParamsPattern.Match(paramsSegment);
        if (!paramsMatch.Success)
        {
            return PhcParseResult.Fail(PhcParseError.Malformed);
        }

        var memoriaKib = int.Parse(paramsMatch.Groups[1].Value);
        var iteraciones = int.Parse(paramsMatch.Groups[2].Value);
        var paralelismo = int.Parse(paramsMatch.Groups[3].Value);

        if (!TryDecodeBase64(saltSegment, out var salt) || !TryDecodeBase64(hashSegment, out var hash))
        {
            return PhcParseResult.Fail(PhcParseError.Malformed);
        }

        return PhcParseResult.Ok(new PhcHash(algorithm, version, memoriaKib, iteraciones, paralelismo, salt, hash));
    }

    private static bool TryDecodeBase64(string segment, out byte[] decoded)
    {
        if (segment.Length == 0)
        {
            decoded = [];
            return false;
        }

        try
        {
            decoded = Convert.FromBase64String(segment);
            return true;
        }
        catch (FormatException)
        {
            decoded = [];
            return false;
        }
    }
}
