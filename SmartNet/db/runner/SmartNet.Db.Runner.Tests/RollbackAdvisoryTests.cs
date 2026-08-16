using SmartNet.Db.TestBootstrap;

namespace SmartNet.Db.Runner.Tests;

/// <summary>
/// Task 5.4 — `SmartNet/db/schema/rollback/NNN_down.sql` scripts must be advisory: authored,
/// reviewed, and structurally invisible to the runner (design.md, Decision 4: "the tool never
/// runs" them). This is verified behaviorally, not just by convention: DbUp's
/// `WithScriptsFromFileSystem` defaults to `SearchOption.TopDirectoryOnly` and the runner
/// (`Program.cs`) never overrides that, so a `rollback/` subdirectory is never enumerated — but a
/// claim about a dependency's default behavior is exactly the kind of thing that silently breaks
/// on an upgrade, so it is pinned down here with a real script that would fail loudly if it were
/// ever executed.
/// </summary>
public sealed class RollbackAdvisoryTests
{
    [Fact]
    public async Task Runner_NeverExecutes_ScriptsUnderARollbackSubdirectory()
    {
        await using var db = await TestDatabaseFixture.CreateAsync();

        // The top-level script is real and succeeds. The rollback/-nested script THROWs
        // unconditionally: if the runner ever picked it up, PerformUpgrade would fail and exit
        // non-zero. A green exit code is the proof the subdirectory was never enumerated at all —
        // not merely that its content happened not to run.
        var scriptsPath = CreateScriptsDirectory(
            ("001_smoke.sql", "IF SCHEMA_ID('fact') IS NULL EXEC('CREATE SCHEMA fact');"),
            (Path.Combine("rollback", "001_down.sql"),
                "THROW 59999, 'rollback/ must never be executed by the runner', 1;"));

        try
        {
            var exitCode = db.RunMigrations(scriptsPath);
            Assert.Equal(0, exitCode);
        }
        finally
        {
            Directory.Delete(scriptsPath, recursive: true);
        }
    }

    // The real guardian: every forward script under SmartNet/db/schema/ has a same-numbered
    // rollback/NNN_down.sql companion (design.md: "each numbered migration ships a companion").
    [Fact]
    public void EveryForwardScript_HasACompanionRollbackScript()
    {
        var schemaPath = RealSchemaPath();
        var rollbackPath = Path.Combine(schemaPath, "rollback");
        Assert.True(Directory.Exists(rollbackPath), $"Expected {rollbackPath} to exist.");

        var forwardScripts = Directory.GetFiles(schemaPath, "*.sql", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(forwardScripts);

        foreach (var forward in forwardScripts)
        {
            var number = Path.GetFileName(forward).Split('_')[0];
            var expectedDown = Path.Combine(rollbackPath, $"{number}_down.sql");
            Assert.True(File.Exists(expectedDown),
                $"Expected a companion rollback script at {expectedDown} for {Path.GetFileName(forward)}.");
        }
    }

    // Real execution, not just a text scan: applies the full forward migration set to a throwaway
    // fact_test_<id> database (never BDSmartNet/master), then runs every rollback/NNN_down.sql in
    // descending numeric order — exactly the promotion order design.md documents — against that
    // same database. Proves the scripts are not just present but syntactically valid, dependency-
    // ordered T-SQL that actually tears fact down to nothing. This is how the row-value
    // `IN ((a,b),(c,d))` syntax error in the first draft of 009_down.sql (not valid T-SQL — SQL
    // Server has no row constructor there) was caught before it could ship as an untested "advisory"
    // script nobody could actually have promoted.
    [Fact]
    public async Task RollbackScripts_ExecuteSuccessfully_InDescendingOrder_AndFullyTearDownFact()
    {
        var db = await TestDatabaseFixture.CreateAsync();
        try
        {
            await db.CreateWithoutLoginUserAsync("usr_api");
            await db.CreateWithoutLoginUserAsync("usr_worker");
            await db.CreateExternalDboCatalogsAsync();
            await db.SeedDboMotivoFixtureRowsAsync();
            var exitCode = db.RunMigrations();
            Assert.Equal(0, exitCode);

            var rollbackPath = Path.Combine(RealSchemaPath(), "rollback");
            var downScripts = Directory.GetFiles(rollbackPath, "*_down.sql")
                .OrderByDescending(path => Path.GetFileName(path))
                .ToList();
            Assert.NotEmpty(downScripts);

            foreach (var script in downScripts)
            {
                var sql = await File.ReadAllTextAsync(script);
                var exception = await Record.ExceptionAsync(() => db.ExecuteNonQueryAsync(sql));
                Assert.True(exception is null, $"{Path.GetFileName(script)} failed to execute: {exception}");
            }

            var factSchemaId = await db.ExecuteScalarAsync<int?>("SELECT SCHEMA_ID('fact');");
            Assert.Null(factSchemaId);
        }
        finally
        {
            await db.DisposeAsync();
        }
    }

    private static string RealSchemaPath() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "schema"));

    private static string CreateScriptsDirectory(params (string RelativePath, string Content)[] scripts)
    {
        var root = Path.Combine(Path.GetTempPath(), $"smartnet-rollback-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        foreach (var (relativePath, content) in scripts)
        {
            var fullPath = Path.Combine(root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(fullPath, content);
        }

        return root;
    }
}
