namespace SmartNet.Admin;

/// <summary>
/// Connection-string resolution for <c>SmartNet.Admin</c> (design.md Decision 7). The SAME
/// variable as <c>SmartNet.Api</c>'s own <c>SMARTNET_API_DB_CONNECTION</c> (deliberately, not a
/// new name) -- Decision 7 is explicit that this CLI is "un comando de la propia aplicación" and
/// therefore runs with <c>usr_api</c>'s own grants, never the deploy principal's
/// <c>SmartNet.Db.Runner</c> reaches for. No default, no committed fallback: the same
/// "no credential ever has a default value" discipline as <c>RunnerOptions</c> and
/// <c>ApiConnectionOptions</c> (CONVENTIONS.md).
/// </summary>
public static class AdminConnectionOptions
{
    public const string ConnectionStringKey = "SMARTNET_API_DB_CONNECTION";

    public const string Usage =
        $"Falta la variable de entorno requerida '{ConnectionStringKey}'. Establezca " +
        $"{ConnectionStringKey} con la cadena de conexión que smartnet-admin debe usar para " +
        "llegar a la base de datos ya migrada, como usr_api.";

    /// <summary>
    /// Resolves the connection string from the process environment. Throws
    /// <see cref="InvalidOperationException"/> with <see cref="Usage"/> if absent -- a fail-fast
    /// startup error, never a silent default.
    /// </summary>
    public static string Resolve()
    {
        var value = Environment.GetEnvironmentVariable(ConnectionStringKey);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(Usage);
        }

        return value;
    }
}
