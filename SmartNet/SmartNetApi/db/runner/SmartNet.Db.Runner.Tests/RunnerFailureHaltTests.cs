using SmartNet.Db.TestBootstrap;

namespace SmartNet.Db.Runner.Tests;

/// <summary>
/// Task 6.2 — what "the runner halts before any downstream artifact" actually reduces to, in
/// terms this repository can prove today (design.md, Decision 1, rollback tier 1).
///
/// The deploy chain of ADR 0012 puts the migration runner ahead of the API and the worker: if the
/// schema does not apply, nothing downstream is deployed. There is no deploy pipeline in this
/// repository yet, so the *ordering* claim has nothing to assert against. What is assertable, and
/// is the whole basis of that ordering, is the runner's own failure contract:
///
///   1. a failing script exits non-zero, so any caller — a shell chain, a CI job, a deploy step —
///      stops on it rather than continuing;
///   2. `WithTransactionPerScript` rolls the failing script back *entirely*, so a half-applied
///      script never reaches a database;
///   3. scripts that already succeeded stay journalled, so re-running after a fix resumes instead
///      of re-applying — which is what makes "fix forward and re-run" a real recovery and not a
///      hopeful one.
///
/// These pass without new production code: Phase 6 verifies the contract already built, it does
/// not add behaviour. Recorded as such rather than dressed up as RED-first.
/// </summary>
public sealed class RunnerFailureHaltTests
{
    [Fact]
    public async Task FailingScript_ExitsNonZero_LeavesNoPartialObject_AndKeepsEarlierScriptsJournalled()
    {
        await using var db = await TestDatabaseFixture.CreateAsync();

        // 001 succeeds. 002 creates a table and *then* fails, inside one script: if the runner did
        // not wrap each script in its own transaction, fact.Sobreviviente would outlive the failure
        // and the database would hold an object no script in the repository accounts for.
        var scriptsPath = CreateScriptsDirectory(
            ("001_ok.sql", "IF SCHEMA_ID('fact') IS NULL EXEC('CREATE SCHEMA fact');"),
            ("002_falla_a_media.sql",
                """
                CREATE TABLE fact.Sobreviviente (Id INT NOT NULL);
                GO
                SELECT * FROM esta.tabla.no.existe;
                """));

        try
        {
            var exitCode = db.RunMigrations(scriptsPath);
            Assert.NotEqual(0, exitCode);

            var partialObject = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE name = 'Sobreviviente';");
            Assert.Equal(0, partialObject);

            var journalled = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM fact.SchemaVersions WHERE ScriptName LIKE '%001_ok%';");
            Assert.Equal(1, journalled);

            var failedJournalled = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM fact.SchemaVersions WHERE ScriptName LIKE '%002_falla%';");
            Assert.Equal(0, failedJournalled);
        }
        finally
        {
            Directory.Delete(scriptsPath, recursive: true);
        }
    }

    [Fact]
    public async Task AfterFixingTheFailedScript_ReRunning_ResumesInsteadOfReapplying()
    {
        await using var db = await TestDatabaseFixture.CreateAsync();
        var scriptsPath = CreateScriptsDirectory(
            ("001_ok.sql", "IF SCHEMA_ID('fact') IS NULL EXEC('CREATE SCHEMA fact');"),
            ("002_falla_a_media.sql", "SELECT * FROM esta.tabla.no.existe;"));

        try
        {
            Assert.NotEqual(0, db.RunMigrations(scriptsPath));

            // Fix forward: the deployer corrects 002 and re-runs. 001 must not be applied twice.
            File.WriteAllText(
                Path.Combine(scriptsPath, "002_falla_a_media.sql"),
                "CREATE TABLE fact.Corregida (Id INT NOT NULL);");

            Assert.Equal(0, db.RunMigrations(scriptsPath));

            var journalEntries = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM fact.SchemaVersions WHERE ScriptName LIKE '%001_ok%';");
            Assert.Equal(1, journalEntries);

            var fixedTable = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM sys.tables WHERE name = 'Corregida';");
            Assert.Equal(1, fixedTable);
        }
        finally
        {
            Directory.Delete(scriptsPath, recursive: true);
        }
    }

    private static string CreateScriptsDirectory(params (string FileName, string Content)[] scripts)
    {
        var path = Path.Combine(Path.GetTempPath(), $"smartnet-db-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        foreach (var (fileName, content) in scripts)
        {
            File.WriteAllText(Path.Combine(path, fileName), content);
        }

        return path;
    }
}
