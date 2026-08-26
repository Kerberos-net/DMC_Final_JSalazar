using SmartNet.Db.TestBootstrap;

namespace SmartNet.Db.Runner.Tests;

/// <summary>
/// Task 3.4 — spec.md "Effective permissions are reproducible from the versioned scripts alone".
/// Applies the versioned migrations (including `008`) to two independently created databases and
/// compares the effective `sys.database_permissions` set for both roles. No hand-applied grant
/// exists anywhere in this test: everything both databases have comes from
/// `SmartNet/SmartNetBD/schema/008_usuarios_y_permisos.sql` alone.
/// </summary>
public sealed class PermissionReproducibilityTests
{
    [Fact]
    public async Task EffectivePermissions_AreIdentical_AcrossTwoIndependentlyMigratedDatabases()
    {
        await using var dbA = await MigratedDatabaseWithUsers();
        await using var dbB = await MigratedDatabaseWithUsers();

        var permissionsA = await EffectivePermissions(dbA);
        var permissionsB = await EffectivePermissions(dbB);

        Assert.NotEmpty(permissionsA);
        Assert.Equal(permissionsA, permissionsB);
    }

    private static async Task<TestDatabaseFixture> MigratedDatabaseWithUsers()
    {
        var db = await TestDatabaseFixture.CreateAsync();
        // See SchemaShapeTests.MigratedDatabase() for why this must be try/catch, not a bare local:
        // a throw before `return db;` here would otherwise leak the already-created database (the
        // confirmed root cause of the Work Unit 3 test-database leak).
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

    /// <summary>
    /// The permission set for both roles (`fact_api`, `fact_worker`), expressed as a
    /// role|permission|state|schema|object tuple set — independent of any database-specific id
    /// (object_id, principal_id) that would legitimately differ between two independently created
    /// databases even with identical grants.
    /// </summary>
    private static async Task<List<string>> EffectivePermissions(TestDatabaseFixture db)
    {
        const string sql =
            """
            SELECT CAST(prin.name AS NVARCHAR(128)) COLLATE DATABASE_DEFAULT + '|' +
                   CAST(perm.permission_name AS NVARCHAR(128)) COLLATE DATABASE_DEFAULT + '|' +
                   CAST(perm.state_desc AS NVARCHAR(60)) COLLATE DATABASE_DEFAULT + '|' +
                   ISNULL(CAST(sch.name AS NVARCHAR(128)) COLLATE DATABASE_DEFAULT, '') + '.' +
                   ISNULL(CAST(obj.name AS NVARCHAR(128)) COLLATE DATABASE_DEFAULT, '')
            FROM sys.database_permissions perm
            JOIN sys.database_principals prin ON perm.grantee_principal_id = prin.principal_id
            LEFT JOIN sys.objects obj ON perm.major_id = obj.object_id AND perm.class = 1
            LEFT JOIN sys.schemas sch ON obj.schema_id = sch.schema_id
            WHERE prin.name IN ('fact_api', 'fact_worker')
            ORDER BY 1;
            """;

        var rows = new List<string>();
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(db.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }
}
