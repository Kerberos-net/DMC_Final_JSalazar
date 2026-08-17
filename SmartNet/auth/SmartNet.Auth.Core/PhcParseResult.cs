namespace SmartNet.Auth.Core;

/// <summary>
/// Typed result of <see cref="PhcCodec.Parse"/> — a success carries <see cref="Hash"/>, a
/// failure carries <see cref="Error"/>, never both, never an exception.
/// </summary>
public sealed class PhcParseResult
{
    public bool IsSuccess { get; }
    public PhcHash? Hash { get; }
    public PhcParseError? Error { get; }

    private PhcParseResult(bool isSuccess, PhcHash? hash, PhcParseError? error)
    {
        IsSuccess = isSuccess;
        Hash = hash;
        Error = error;
    }

    public static PhcParseResult Ok(PhcHash hash) => new(true, hash, null);

    public static PhcParseResult Fail(PhcParseError error) => new(false, null, error);
}
