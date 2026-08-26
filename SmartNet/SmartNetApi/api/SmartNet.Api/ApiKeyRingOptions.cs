namespace SmartNet.Api;

/// <summary>
/// Data Protection key-ring path resolution (design.md Decision 4, gate task 0.2, CLOSED). Same
/// resolution pattern as <see cref="ApiConnectionOptions"/> — an environment variable, no default,
/// no committed fallback, startup failure with a usage message if absent. Recommended concrete
/// value for this project's single-instance Windows deployment (ADR 0012):
/// <c>C:\ProgramData\SmartNet\dataprotection-keys</c> — never inside the Git checkout, never
/// hardcoded here (that value lives in deployment configuration, not source).
/// </summary>
public static class ApiKeyRingOptions
{
    public const string KeyRingPathKey = "SMARTNET_API_KEYRING_PATH";

    public const string Usage =
        $"Missing required configuration '{KeyRingPathKey}'. Set the {KeyRingPathKey} " +
        "environment variable to a directory the Kestrel process account can read and write, " +
        "persisted outside the Git checkout (design.md Decision 4). Losing this directory " +
        "invalidates every live session cookie on restart.";

    public static string Resolve(IConfiguration configuration)
    {
        var value = configuration[KeyRingPathKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(Usage);
        }

        return value;
    }
}
