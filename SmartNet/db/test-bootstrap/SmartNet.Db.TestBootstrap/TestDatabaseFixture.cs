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

    // Runs exactly once per test process (Lazy<Task> with the default LazyThreadSafetyMode.
    // ExecutionAndPublication guarantees single execution even under xUnit's cross-class
    // parallelism): drops any `fact_test_%` database left behind by a prior run whose disposal did
    // not complete. See design.md/apply-progress "Coordinator-directed follow-up, item 3" for the
    // diagnosed root cause — this sweep is the safety net, not the fix for that root cause.
    private static readonly Lazy<Task> OrphanSweep = new(SweepOrphanedTestDatabasesAsync);

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

    /// <summary>
    /// Drops every database whose name matches exactly `fact_test_%` (the literal underscore is
    /// escaped so it is not treated as a SQL `LIKE` single-character wildcard) — never anything
    /// else. `master` and `BDSmartNet` are structurally unreachable: neither name matches this
    /// pattern, and the query never enumerates by anything other than name. Best-effort: a database
    /// still legitimately in use by a concurrently running test (unlikely — `fact_test_<guid>`
    /// names never collide) would simply fail this DROP and be left for a later sweep or manual
    /// cleanup, never treated as fatal to the current test run.
    /// </summary>
    private static async Task SweepOrphanedTestDatabasesAsync()
    {
        var masterConnectionString = ResolveMasterConnectionString();

        List<string> orphaned;
        await using (var connection = new SqlConnection(masterConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sys.databases WHERE name LIKE 'fact\\_test\\_%' ESCAPE '\\';";
            await using var reader = await command.ExecuteReaderAsync();
            orphaned = new List<string>();
            while (await reader.ReadAsync())
            {
                orphaned.Add(reader.GetString(0));
            }
        }

        foreach (var name in orphaned)
        {
            try
            {
                await using var connection = new SqlConnection(masterConnectionString);
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    $"""
                     ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                     DROP DATABASE [{name}];
                     """;
                await command.ExecuteNonQueryAsync();
            }
            catch (SqlException)
            {
                // Best-effort cleanup of a PRIOR run's leak; never block the current test run over
                // a database this sweep could not remove (e.g. genuinely still in use).
            }
        }
    }

    public static async Task<TestDatabaseFixture> CreateAsync(CancellationToken ct = default)
    {
        await OrphanSweep.Value;

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
    /// Creates the four `dbo.*` external catalogs named by ADR 0003's "externa" class
    /// (`Proveedor`, `CuentaContable`, `Motivo`, `Origen`), as bare structure only — no data, no
    /// `DocumentoIdentidad`, no FK to it. `008_usuarios_y_permisos.sql`'s
    /// `GRANT SELECT ON OBJECT::dbo.<table>` statements need these objects to exist to succeed;
    /// this is test-only infrastructure, never applied to the shared database (that is
    /// `SmartNet/db/fixtures/010_dbo_catalogos_ddl.sql`'s job, run once by hand against a real
    /// environment, README.md). Idempotent (`IF OBJECT_ID(...) IS NULL`), consistent with the rest
    /// of this harness.
    /// </summary>
    public Task CreateExternalDboCatalogsAsync(CancellationToken ct = default) =>
        ExecuteNonQueryAsync(
            """
            IF OBJECT_ID('dbo.DocumentoIdentidad', 'U') IS NULL
                CREATE TABLE dbo.DocumentoIdentidad (coddocide CHAR(2) NOT NULL, nomdocide NVARCHAR(60) NOT NULL,
                    CONSTRAINT PK_DocumentoIdentidad PRIMARY KEY CLUSTERED (coddocide));
            IF OBJECT_ID('dbo.Origen', 'U') IS NULL
                CREATE TABLE dbo.Origen (codigo CHAR(2) NOT NULL, origen NVARCHAR(40) NOT NULL,
                    CONSTRAINT PK_Origen PRIMARY KEY CLUSTERED (codigo));
            IF OBJECT_ID('dbo.Motivo', 'U') IS NULL
                CREATE TABLE dbo.Motivo (codigo INT NOT NULL, motivo NVARCHAR(60) NOT NULL,
                    cuenta VARCHAR(120) NULL, CONSTRAINT PK_Motivo PRIMARY KEY CLUSTERED (codigo));
            IF OBJECT_ID('dbo.CuentaContable', 'U') IS NULL
                CREATE TABLE dbo.CuentaContable (cuenta VARCHAR(10) NOT NULL,
                    descripcion NVARCHAR(60) NOT NULL, nivel TINYINT NULL, ctarefleja VARCHAR(10) NULL,
                    ctapuente VARCHAR(10) NULL, CONSTRAINT PK_CuentaContable PRIMARY KEY CLUSTERED (cuenta));
            IF OBJECT_ID('dbo.Proveedor', 'U') IS NULL
                CREATE TABLE dbo.Proveedor (codpro CHAR(6) NOT NULL, proveedor NVARCHAR(80) NOT NULL,
                    coddocide CHAR(2) NULL, rucpro VARCHAR(11) NULL,
                    CONSTRAINT PK_Proveedor PRIMARY KEY CLUSTERED (codpro));
            """,
            ct);

    /// <summary>
    /// TEST FIXTURE — NOT data this project owns. `dbo.Motivo` belongs entirely to the accounting
    /// system (ADR 0003, clase "externa"); this seeds a small, representative subset into the
    /// empty test-fixture table `CreateExternalDboCatalogsAsync()` created, so that
    /// `010_motivo_atributo_demo.sql`'s `INSERT ... SELECT ... FROM dbo.Motivo` has rows to select
    /// when it runs against a throwaway `fact_test_&lt;id&gt;` database. The names, prefixes and
    /// origin markers below are copied verbatim from `MOTIVOS-CLASIFICACION.md`'s own table for
    /// traceability, not invented — but the SET is deliberately partial (the real catalog has 90
    /// rows; this seeds 28): the 23 `†`-marked motives the reclassification touches (5, 13, 16, 17,
    /// 18, 19, 20, 21, 30, 38, 40, 42, 46, 48, 49, 53, 56, 59, 60, 77, 81, 88, 90 — every one of
    /// them, at minimum, per the coordinator's instruction), plus five more (11, 12, 22 — plain `02`
    /// motives never reclassified; 1, 28 — `BAJA` motives) so the "no other motive is reclassified"
    /// scenario has something real to check against.
    /// </summary>
    public Task SeedDboMotivoFixtureRowsAsync(CancellationToken ct = default) =>
        ExecuteNonQueryAsync(
            """
            INSERT INTO dbo.Motivo (codigo, motivo, cuenta) VALUES
                (5, 'Transferencia a Caja chica', '1013,1021,1022'),
                (13, 'Movilidad', '631123'),
                (16, 'Parqueo o cochera', '6393'),
                (17, 'Tasas de contratos', '644311'),
                (18, 'Peaje', '639915'),
                (19, 'Utiles de escritorio menores', '656111'),
                (20, 'Utiles de Limpieza menores', '656211'),
                (21, 'Botiquin menores', '656212'),
                (30, 'Mantenimiento local menores', '634311'),
                (38, 'Copia Literal o vigencia poder', '636913'),
                (40, 'Legalizaciones', '632211'),
                (42, 'Recarga de nextel menor a 100', '636412'),
                (46, 'Repuesto soporte tecnico menor a 50', '656511'),
                (48, N'Gastos de representación menor a 100', '6373'),
                (49, N'Servicio Reparación equipo menor a 50', '634314'),
                (53, 'Recarga de tarjetas peruanas', '169901'),
                (56, 'Reniec', '636912'),
                (59, 'Tasas Judiciales y Policiales', '644311'),
                (60, 'Arreglo Floral', '659913'),
                (77, N'Periódico', '659914'),
                (81, 'Movilidad-Taxi por viaje', '631124'),
                (88, N'Devolución Comprobante CChica', '169105'),
                (90, 'Mantenimiento rep muebles y eq', '634313'),
                (11, 'Servicio custodia mercaderia SS', '639922'),
                (12, N'Fotocopia-Impresión', '639914'),
                (22, 'Fletes traslado de mercaderia', '631111'),
                (1, 'Pago a Cuenta de Proveedores', '656412'),
                (28, 'NO USAR', '1424');
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

    /// <summary>
    /// Retries a few times with backoff: under xUnit's cross-class parallelism, many
    /// `fact_test_&lt;id&gt;` databases are created/dropped concurrently against the same instance,
    /// and `DROP DATABASE`/`ALTER DATABASE` can hit transient lock contention. This is defense in
    /// depth, not the fix for the confirmed root cause of the disposal leak (see apply-progress,
    /// "Coordinator-directed follow-up, item 3": callers whose own setup helper throws BEFORE
    /// returning the fixture never reach this method at all — that is a caller-side bug, fixed at
    /// the call sites, not here).
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
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
                return;
            }
            catch (SqlException) when (attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt));
            }
        }
    }
}
