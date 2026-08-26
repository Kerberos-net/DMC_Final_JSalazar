namespace SmartNet.Db.Runner;

/// <summary>
/// Command-line / environment options for the runner. No credential ever has a default value or
/// a fallback baked into source: the connection string must come from the caller (CLI flag or
/// environment variable), never from a committed file (CONVENTIONS.md, design.md Decision 3).
/// </summary>
public sealed record RunnerOptions(string ConnectionString, string ScriptsPath)
{
    public const string Usage =
        "Usage: SmartNet.Db.Runner --connection <connection-string> [--scripts-path <path>]\n" +
        "  --connection    Required. Or set the SMARTNET_DB_CONNECTION environment variable.\n" +
        "  --scripts-path  Optional. Defaults to SmartNet/SmartNetBD/schema/ resolved from the repo root.";

    public static RunnerOptions? Parse(string[] args)
    {
        string? connectionString = Environment.GetEnvironmentVariable("SMARTNET_DB_CONNECTION");
        string? scriptsPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--connection" when i + 1 < args.Length:
                    connectionString = args[++i];
                    break;
                case "--scripts-path" when i + 1 < args.Length:
                    scriptsPath = args[++i];
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        scriptsPath ??= ResolveDefaultScriptsPath();

        return new RunnerOptions(connectionString, scriptsPath);
    }

    /// <summary>
    /// Walks up from the executing assembly's directory looking for the repo-root marker
    /// (a `.git` directory) and returns `&lt;repo-root&gt;/SmartNet/SmartNetBD/schema`. Kept independent of
    /// the current working directory so the runner behaves the same whether invoked via
    /// `dotnet run` from the repo root or as a published executable from anywhere.
    /// </summary>
    private static string ResolveDefaultScriptsPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }

        var repoRoot = dir?.FullName
            ?? throw new InvalidOperationException(
                "Could not locate the repository root (.git) to resolve the default scripts path. " +
                "Pass --scripts-path explicitly.");

        return Path.Combine(repoRoot, "SmartNet", "SmartNetBD", "schema");
    }
}
