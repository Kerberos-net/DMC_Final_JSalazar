using SmartNet.Auth.Core;
using SmartNet.Auth.Infrastructure;
using SmartNet.Db.TestBootstrap;

namespace SmartNet.Admin.Tests;

/// <summary>
/// Shared migrated-database setup for <see cref="AdminOperations"/> tests, against a real
/// <c>fact_test_&lt;id&gt;</c> database (mirrors SmartNet.Auth.Infrastructure.Tests' own
/// <c>MigratedDatabase</c> helper -- same reasoning, task 5.4/5.6/5.8's RED needs the real
/// migrated schema, not a mock repository, to prove the CLI reaches the actual columns).
/// </summary>
public abstract class AdminOperationsTestBase : IAsyncLifetime
{
    protected TestDatabaseFixture Db { get; private set; } = null!;
    protected IUsuarioRepository Usuarios { get; private set; } = null!;
    protected ISesionRepository Sesiones { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Db = await MigratedDatabase();
        Usuarios = new SqlUsuarioRepository(Db.ConnectionString);
        Sesiones = new SqlSesionRepository(Db.ConnectionString);
    }

    public async Task DisposeAsync() => await Db.DisposeAsync();

    private static async Task<TestDatabaseFixture> MigratedDatabase()
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
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }
}
