using DbUp;
using Microsoft.Data.SqlClient;

namespace SmartNet.Db.Runner;

/// <summary>
/// Applies the versioned SQL scripts in SmartNet/db/schema/ to the target database, in lexical
/// (== numeric, because of zero-padding) order. See design.md, Decision 1.
///
/// The DbUp journal is forced into fact.SchemaVersions (never dbo.SchemaVersions, DbUp's default)
/// because this project must never create an object outside schema `fact` on a database it shares
/// with the accounting system.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        var options = RunnerOptions.Parse(args);

        if (options is null)
        {
            Console.Error.WriteLine(RunnerOptions.Usage);
            return 1;
        }

        return Run(options);
    }

    public static int Run(RunnerOptions options)
    {
        // DbUp's journal table lives in schema `fact`, and 001_esquema_fact.sql is the script that
        // creates that schema. On a brand-new database DbUp would try to CREATE TABLE
        // fact.SchemaVersions inside the *same* transaction that runs 001 — but the schema created
        // by 001 is not yet visible to that CREATE TABLE call (SQL Server error 2760), because the
        // journal check is issued on a separate connection/round-trip than the one that ran the
        // script. Ensuring the schema exists ahead of time, outside DbUp's own transaction, is a
        // pure infrastructure step (idempotent, no domain object) — the same role
        // EnsureDatabase.For.SqlDatabase plays for the database itself.
        EnsureJournalSchemaExists(options.ConnectionString);

        var upgrader =
            DeployChanges.To
                .SqlDatabase(options.ConnectionString)
                .WithScriptsFromFileSystem(options.ScriptsPath)
                .JournalToSqlTable("fact", "SchemaVersions")
                .WithTransactionPerScript()
                .LogToConsole()
                .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(result.Error);
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Migraciones aplicadas correctamente.");
        Console.ResetColor();
        return 0;
    }

    private static void EnsureJournalSchemaExists(string connectionString)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "IF SCHEMA_ID('fact') IS NULL EXEC('CREATE SCHEMA fact');";
        command.ExecuteNonQuery();
    }
}
