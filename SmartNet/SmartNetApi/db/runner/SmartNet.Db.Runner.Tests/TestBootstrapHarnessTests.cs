using SmartNet.Db.TestBootstrap;

namespace SmartNet.Db.Runner.Tests;

/// <summary>
/// Task 1.4 / 1.5 — verifies the test-bootstrap harness that every later permission-matrix test
/// (Phase 3, Unit 3) depends on: an empty `fact_test_&lt;id&gt;` database, and `WITHOUT LOGIN`
/// database users usable via `EXECUTE AS USER` without any instance-level login (design.md,
/// Decision 3 and the ADR 0019 section).
///
/// `008_usuarios_y_permisos.sql` itself does not exist yet (Phase 3 is out of scope for this work
/// unit). What is verified here — and is genuinely this unit's responsibility — is that the
/// create-if-absent mechanism the harness offers, which 008 is designed to rely on, is itself
/// idempotent: creating a WITHOUT LOGIN user that already exists must not fail.
/// </summary>
public sealed class TestBootstrapHarnessTests
{
    [Fact]
    public async Task CreateTestDatabase_ProducesAnEmptyDatabase()
    {
        await using var db = await TestDatabaseFixture.CreateAsync();

        var tableCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.tables;");

        Assert.Equal(0, tableCount);
    }

    [Fact]
    public async Task CreateWithoutLoginUser_IsIdempotent_ReapplyingAgainstExistingUsersDoesNotFail()
    {
        await using var db = await TestDatabaseFixture.CreateAsync();

        await db.CreateWithoutLoginUserAsync("usr_api");
        await db.CreateWithoutLoginUserAsync("usr_worker");

        // The users already exist at this point — this is exactly the state 008 must tolerate
        // (design.md: "create-if-absent, always-grant" is what makes a re-applied environment
        // converge).
        var exception = await Record.ExceptionAsync(async () =>
        {
            await db.CreateWithoutLoginUserAsync("usr_api");
            await db.CreateWithoutLoginUserAsync("usr_worker");
        });

        Assert.Null(exception);

        var userCount = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.database_principals WHERE name IN ('usr_api', 'usr_worker') AND type = 'S';");
        Assert.Equal(2, userCount);
    }

    [Fact]
    public async Task ExecuteAsUser_ImpersonatesTheWithoutLoginUser()
    {
        await using var db = await TestDatabaseFixture.CreateAsync();
        await db.CreateWithoutLoginUserAsync("usr_api");

        var currentUser = await db.ExecuteAsUserAsync("usr_api", async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT USER_NAME();";
            return (string)(await command.ExecuteScalarAsync())!;
        });

        Assert.Equal("usr_api", currentUser);
    }

    /// <summary>
    /// Task 1.5's owed assertion, settled here now that `008_usuarios_y_permisos.sql` exists
    /// (Phase 3 / Unit 3): applying `008` against a database where `usr_api`/`usr_worker` already
    /// exist as WITHOUT LOGIN users (the harness's own setup order — see design.md, "How the ADR
    /// 0019 level-2 tests reach a database") must succeed, and applying it a second time — this
    /// time with the roles, membership and grants of the first application already in place — must
    /// also succeed without error.
    /// </summary>
    [Fact]
    public async Task Script008_IsCreateIfAbsent_ReapplyingAgainstAlreadyMigratedDatabaseSucceeds()
    {
        await using var db = await TestDatabaseFixture.CreateAsync();
        await db.CreateWithoutLoginUserAsync("usr_api");
        await db.CreateWithoutLoginUserAsync("usr_worker");
        await db.CreateExternalDboCatalogsAsync();
        await db.SeedDboMotivoFixtureRowsAsync();

        var firstRun = db.RunMigrations();
        Assert.Equal(0, firstRun);

        // DbUp's own journal (fact.SchemaVersions) would otherwise skip 008 on a second call,
        // because it already recorded 008 as applied — proving nothing about 008's own idempotency.
        // Deleting only 008's journal row forces DbUp to re-execute 008's actual SQL text against a
        // database where usr_api/usr_worker are already real users, already members of
        // fact_api/fact_worker, with every GRANT/DENY already in place — exactly the re-apply
        // scenario task 1.5/3.4 require.
        await db.ExecuteNonQueryAsync(
            "DELETE FROM fact.SchemaVersions WHERE ScriptName LIKE '%008_usuarios_y_permisos%';");

        var secondRun = db.RunMigrations();
        Assert.Equal(0, secondRun);
    }
}
