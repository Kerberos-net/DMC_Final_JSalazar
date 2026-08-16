using SmartNet.Db.TestBootstrap;

namespace SmartNet.Db.Runner.Tests;

/// <summary>
/// Task 1.2 / 1.3 — the runner smoke test. Asserts the DbUp journal lands in
/// `fact.SchemaVersions`, never `dbo.SchemaVersions` (design.md, Decision 1, cost 5).
///
/// Uses a throwaway idempotent script, not SmartNet/db/schema/001_esquema_fact.sql: Phase 2 (the
/// real schema scripts) is out of scope for this work unit. The throwaway script only needs to
/// create schema `fact` so DbUp has somewhere to put the journal.
/// </summary>
public sealed class RunnerJournalTests
{
    [Fact]
    public async Task JournalTable_LandsInFactSchema_NotDbo()
    {
        await using var db = await TestDatabaseFixture.CreateAsync();
        var scriptsPath = CreateScriptsDirectory(
            ("001_smoke.sql", "IF SCHEMA_ID('fact') IS NULL EXEC('CREATE SCHEMA fact');"));

        try
        {
            var exitCode = db.RunMigrations(scriptsPath);

            Assert.Equal(0, exitCode);

            var journalInFact = await db.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM sys.tables t
                JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE s.name = 'fact' AND t.name = 'SchemaVersions';
                """);
            Assert.Equal(1, journalInFact);

            var journalInDbo = await db.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*) FROM sys.tables t
                JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE s.name = 'dbo' AND t.name = 'SchemaVersions';
                """);
            Assert.Equal(0, journalInDbo);
        }
        finally
        {
            Directory.Delete(scriptsPath, recursive: true);
        }
    }

    [Fact]
    public async Task Runner_IsIdempotent_ReapplyingTheSameScriptsSucceedsWithoutError()
    {
        await using var db = await TestDatabaseFixture.CreateAsync();
        var scriptsPath = CreateScriptsDirectory(
            ("001_smoke.sql", "IF SCHEMA_ID('fact') IS NULL EXEC('CREATE SCHEMA fact');"));

        try
        {
            var firstRun = db.RunMigrations(scriptsPath);
            var secondRun = db.RunMigrations(scriptsPath);

            Assert.Equal(0, firstRun);
            Assert.Equal(0, secondRun);
        }
        finally
        {
            Directory.Delete(scriptsPath, recursive: true);
        }
    }

    [Fact]
    public async Task Runner_ExitsNonZero_WhenAScriptFails()
    {
        await using var db = await TestDatabaseFixture.CreateAsync();
        var scriptsPath = CreateScriptsDirectory(
            ("001_broken.sql", "SELECT * FROM esta.tabla.no.existe;"));

        try
        {
            var exitCode = db.RunMigrations(scriptsPath);

            Assert.NotEqual(0, exitCode);
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
