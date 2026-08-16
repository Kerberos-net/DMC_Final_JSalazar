using Microsoft.Data.SqlClient;
using SmartNet.Db.Runner;

namespace SmartNet.Db.TestBootstrap;

/// <summary>
/// Test-only harness (never referenced by SmartNet.Db.Runner, never shipped, never run in
/// production — see design.md, "How the ADR 0019 level-2 tests reach a database").
///
/// Per run: creates an empty `fact_test_&lt;id&gt;` database on the target instance, exposes it for
/// the runner to migrate, and can create `WITHOUT LOGIN` database principals so the permission
/// matrix tests carry no instance-level login/CREATE LOGIN dependency (design.md, Decision 3 and
/// the ADR 0019 section).
/// </summary>
public sealed class TestDatabaseFixture : IAsyncDisposable
{
    private readonly string _masterConnectionString;

    public string DatabaseName { get; }
    public string ConnectionString { get; }

    private TestDatabaseFixture(string databaseName, string connectionString, string masterConnectionString)
    {
        DatabaseName = databaseName;
        ConnectionString = connectionString;
        _masterConnectionString = masterConnectionString;
    }

    /// <summary>
    /// Resolves the connection string used to reach the SQL Server instance that will host the
    /// throwaway test databases. Defaults to local Windows-integrated auth against the default
    /// instance — no password, so nothing here is a credential (CONVENTIONS.md). Overridable via
    /// SMARTNET_TEST_MASTER_CONNECTION for CI environments with a differently-named instance.
    /// </summary>
    public static string ResolveMasterConnectionString() =>
        Environment.GetEnvironmentVariable("SMARTNET_TEST_MASTER_CONNECTION")
        ?? "Server=localhost;Database=master;Integrated Security=True;TrustServerCertificate=True;Encrypt=False";

    public static async Task<TestDatabaseFixture> CreateAsync(CancellationToken ct = default)
    {
        var masterConnectionString = ResolveMasterConnectionString();
        var databaseName = $"fact_test_{Guid.NewGuid():N}";

        await using (var connection = new SqlConnection(masterConnectionString))
        {
            await connection.OpenAsync(ct);
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{databaseName}];";
            await command.ExecuteNonQueryAsync(ct);
        }

        var testConnectionString = new SqlConnectionStringBuilder(masterConnectionString)
        {
            InitialCatalog = databaseName
        }.ConnectionString;

        return new TestDatabaseFixture(databaseName, testConnectionString, masterConnectionString);
    }

    /// <summary>
    /// Runs SmartNet.Db.Runner against this database. Returns the runner's process-style exit
    /// code: 0 on success, non-zero on failure (design.md Decision 1, ADR 0012 order).
    /// </summary>
    public int RunMigrations(string? scriptsPath = null)
    {
        var cliArgs = scriptsPath is null
            ? new[] { "--connection", ConnectionString }
            : new[] { "--connection", ConnectionString, "--scripts-path", scriptsPath };

        var options = RunnerOptions.Parse(cliArgs)
            ?? throw new InvalidOperationException("Failed to build runner options for the test database.");

        return Program.Run(options);
    }

    /// <summary>
    /// Creates a contained, loginless database user — the mechanism that lets the permission
    /// matrix tests run as `EXECUTE AS USER` without any instance-level login (design.md,
    /// Decision 3 and the ADR 0019 section). Idempotent, matching 008's own create-if-absent rule.
    /// </summary>
    public Task CreateWithoutLoginUserAsync(string userName, CancellationToken ct = default) =>
        ExecuteNonQueryAsync(
            $"""
             IF DATABASE_PRINCIPAL_ID('{userName}') IS NULL
                 CREATE USER [{userName}] WITHOUT LOGIN;
             """,
            ct);

    /// <summary>
    /// Runs <paramref name="action"/> impersonating <paramref name="userName"/> for the lifetime
    /// of one connection, via `EXECUTE AS USER` / `REVERT` — the documented way to evaluate
    /// database-level permissions without a real login (design.md).
    /// </summary>
    public async Task<T> ExecuteAsUserAsync<T>(
        string userName,
        Func<SqlConnection, Task<T>> action,
        CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);

        await using (var impersonate = connection.CreateCommand())
        {
            impersonate.CommandText = "EXECUTE AS USER = @userName;";
            impersonate.Parameters.AddWithValue("@userName", userName);
            await impersonate.ExecuteNonQueryAsync(ct);
        }

        try
        {
            return await action(connection);
        }
        finally
        {
            await using var revert = connection.CreateCommand();
            revert.CommandText = "REVERT;";
            await revert.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task ExecuteNonQueryAsync(string sql, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<T?> ExecuteScalarAsync<T>(string sql, CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync(ct);
        return result is null or DBNull ? default : (T)result;
    }

    public async ValueTask DisposeAsync()
    {
        await using var connection = new SqlConnection(_masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
             DROP DATABASE [{DatabaseName}];
             """;
        await command.ExecuteNonQueryAsync();
    }
}
