using Microsoft.Data.SqlClient;
using SmartNet.Db.TestBootstrap;

namespace SmartNet.Db.Runner.Tests;

/// <summary>
/// BACKLOG #19, Phase 1 (tasks 1.1–1.5) — 021_glosa_y_campos_no_extraidos.sql adds two additive
/// nullable columns to fact.Factura (Glosa NVARCHAR(250), CamposNoExtraidos NVARCHAR(500)) and NO
/// new GRANT (ADR 0003). Verified against the real script applied by the real runner over a
/// throwaway fact_test_&lt;id&gt; database. ChecksumManifestTests / RollbackAdvisoryTests cover the
/// manifest + companion-rollback requirements against the real files separately.
/// </summary>
public sealed class Schema021GlosaCamposNoExtraidosTests
{
    private const string UsrApi = "usr_api";
    private const string UsrWorker = "usr_worker";

    // Task 1.3 — column shape: both nullable, expected nvarchar widths (max_length is bytes: 250*2,
    // 500*2).
    [Fact]
    public async Task FacturaGlosaAndCamposNoExtraidos_AreNullableNvarcharWithExpectedWidths()
    {
        await using var db = await MigratedDatabaseWithUsers();

        var shape = await db.ExecuteScalarAsync<string>(
            """
            SELECT STRING_AGG(c.name + ':' + ty.name + '(' + CAST(c.max_length AS VARCHAR) + '):'
                              + CAST(c.is_nullable AS VARCHAR), ',')
                   WITHIN GROUP (ORDER BY c.name)
            FROM sys.columns c
            JOIN sys.types ty ON c.user_type_id = ty.user_type_id
            WHERE c.object_id = OBJECT_ID('fact.Factura')
              AND c.name IN ('Glosa', 'CamposNoExtraidos');
            """);

        Assert.Equal("CamposNoExtraidos:nvarchar(1000):1,Glosa:nvarchar(500):1", shape);
    }

    // Task 1.1 — no new GRANT: 021 adds no column-level permission, fact_api keeps its object-level
    // SELECT/INSERT/UPDATE (which covers the new columns), fact_worker stays denied.
    [Fact]
    public async Task Schema021_AddsNoGrantOnFactura_ColumnsInheritObjectLevelPermissions()
    {
        await using var db = await MigratedDatabaseWithUsers();

        var columnLevelPermissions = await db.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.database_permissions perm
            JOIN sys.columns c ON perm.major_id = c.object_id AND perm.minor_id = c.column_id
            WHERE perm.class = 1
              AND c.object_id = OBJECT_ID('fact.Factura')
              AND c.name IN ('Glosa', 'CamposNoExtraidos');
            """);
        Assert.Equal(0, columnLevelPermissions);

        await db.ExecuteNonQueryAsync(
            "INSERT INTO fact.Factura (ProveedorCodigo, TipoComprobante, TotalOrig, Moneda, FechaEmision) " +
            "VALUES ('P00000', '01', 100.00, 'PEN', '2026-01-01');");
        var facturaId = await db.ExecuteScalarAsync<long>("SELECT MAX(FacturaId) FROM fact.Factura;");

        await AssertSucceeds(db, UsrApi,
            $"SELECT Glosa, CamposNoExtraidos FROM fact.Factura WHERE FacturaId = {facturaId};");
        await AssertSucceeds(db, UsrApi,
            $"UPDATE fact.Factura SET Glosa = 'g', CamposNoExtraidos = 'igv,total' WHERE FacturaId = {facturaId};");

        await AssertDenied(db, UsrWorker,
            $"SELECT Glosa FROM fact.Factura WHERE FacturaId = {facturaId};");
        await AssertDenied(db, UsrWorker,
            $"UPDATE fact.Factura SET CamposNoExtraidos = 'x' WHERE FacturaId = {facturaId};");
    }

    // Task 1.5 — re-applying 021 against an already-migrated database is a no-op, not an error.
    [Fact]
    public async Task Reapplying021_IsANoOp()
    {
        await using var db = await MigratedDatabaseWithUsers();

        var script = await File.ReadAllTextAsync(
            Path.Combine(RealSchemaPath(), "021_glosa_y_campos_no_extraidos.sql"));

        var exception = await Record.ExceptionAsync(() => db.ExecuteNonQueryAsync(script));
        Assert.Null(exception);

        var columnCount = await db.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM sys.columns
            WHERE object_id = OBJECT_ID('fact.Factura')
              AND name IN ('Glosa', 'CamposNoExtraidos');
            """);
        Assert.Equal(2, columnCount);
    }

    private static string RealSchemaPath() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "..", "SmartNetBD", "schema"));

    private static async Task<TestDatabaseFixture> MigratedDatabaseWithUsers()
    {
        var db = await TestDatabaseFixture.CreateAsync();
        try
        {
            await db.CreateWithoutLoginUserAsync(UsrApi);
            await db.CreateWithoutLoginUserAsync(UsrWorker);
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

    private static async Task AssertDenied(TestDatabaseFixture db, string user, string sql)
    {
        var exception = await Record.ExceptionAsync(() => db.ExecuteAsUserAsync(user, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
            return 0;
        }));

        var sqlException = Assert.IsType<SqlException>(exception);
        Assert.Equal(229, sqlException.Number);
    }

    private static async Task AssertSucceeds(TestDatabaseFixture db, string user, string sql)
    {
        var exception = await Record.ExceptionAsync(() => db.ExecuteAsUserAsync(user, async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
            return 0;
        }));

        Assert.Null(exception);
    }
}
