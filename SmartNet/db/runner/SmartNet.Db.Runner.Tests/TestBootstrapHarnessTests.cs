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
}
