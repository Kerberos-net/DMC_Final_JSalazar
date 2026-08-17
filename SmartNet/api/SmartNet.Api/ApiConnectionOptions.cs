namespace SmartNet.Api;

/// <summary>
/// Connection-string resolution for the API host (design.md Decision 6). Parsed the same way as
/// <c>SmartNet.Db.Runner.RunnerOptions</c> — environment variable (surfaced through
/// <see cref="IConfiguration"/>, which the default <c>WebApplicationBuilder</c> already sources
/// from process environment variables), no default, no committed fallback, startup failure with a
/// usage message if absent.
///
/// Deliberately a DIFFERENT variable name from the runner's <c>SMARTNET_DB_CONNECTION</c>
/// (task 4.4): those two hold different principals (deploy vs. <c>usr_api</c>), and reusing the
/// name would let an operator who exported the deploy credential for a migration silently hand
/// the API host deploy-level rights on a shared database — exactly the failure the permission
/// matrix exists to prevent.
/// </summary>
public static class ApiConnectionOptions
{
    public const string ConnectionStringKey = "SMARTNET_API_DB_CONNECTION";

    public const string Usage =
        $"Missing required configuration '{ConnectionStringKey}'. Set the " +
        $"{ConnectionStringKey} environment variable to the connection string SmartNet.Api " +
        "should use to reach the already-migrated database as usr_api.";

    /// <summary>
    /// Resolves the connection string from <paramref name="configuration"/>. Throws
    /// <see cref="InvalidOperationException"/> with <see cref="Usage"/> if absent — a startup
    /// failure, never a silent default.
    /// </summary>
    public static string Resolve(IConfiguration configuration)
    {
        var value = configuration[ConnectionStringKey];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(Usage);
        }

        return value;
    }
}
