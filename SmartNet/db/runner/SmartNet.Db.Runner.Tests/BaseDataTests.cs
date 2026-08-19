using SmartNet.Db.TestBootstrap;

namespace SmartNet.Db.Runner.Tests;

/// <summary>
/// Phase 4 (Work Unit 4) — tasks 4.1-4.4, 4.7. Base-data assertions against the real
/// `009_datos_base.sql`/`010_motivo_atributo_demo.sql`, applied by the real runner over a
/// throwaway `fact_test_&lt;id&gt;` database seeded with a `dbo.Motivo` test fixture
/// (`SeedDboMotivoFixtureRowsAsync`, itself test-only — see its own doc comment).
///
/// Task 4.7 ("every INSERT in 4.5/4.6 targets a fact.-qualified table") is deliberately NOT
/// duplicated here: `DboWriteLintTests.RealSchemaScripts_HaveNoDisallowedDboMentions` already scans
/// every script under `SmartNet/db/schema/` — 009 and 010 included, automatically, the moment they
/// exist — for exactly this property (any INSERT/UPDATE/DELETE/etc. whose own target is `dbo.*`).
/// A second test asserting the same fact from the same files would be redundant, not defense in
/// depth; see tasks.md's note under 4.7 for the fuller reasoning.
/// </summary>
public sealed class BaseDataTests
{
    // Task 4.1 — EstadoIntegracion: exactly the five names spec.md's own Scenario states verbatim
    // ("five rows, no more, no fewer") — not the seven design.md's still-open Open Question
    // speculated before spec.md settled it; see tasks.md's note under 4.5 for that discrepancy.
    [Fact]
    public async Task EstadoIntegracion_IsSeededWithExactlyTheFiveKnownIntegrationNames()
    {
        await using var db = await MigratedDatabaseWithBaseData();

        var names = await QueryStrings(db, "SELECT Nombre FROM fact.EstadoIntegracion ORDER BY Nombre;");
        Assert.Equal(new[] { "DRIVE", "GMAIL", "SBS", "SHEETS", "WORKER" }, names);
    }

    [Fact]
    public async Task EstadoIntegracion_WorkerRow_StartsWithZeroFallosSeguidos()
    {
        await using var db = await MigratedDatabaseWithBaseData();

        var fallos = await db.ExecuteScalarAsync<int>(
            "SELECT FallosSeguidos FROM fact.EstadoIntegracion WHERE Nombre = 'WORKER';");
        Assert.Equal(0, fallos);
    }

    // Task 4.2 — Configuracion: every TECH-DESIGN section has >=1 row; pendiente keys are
    // Valor/ValorPorDefecto both NULL; the two keys a document actually decides a value for are not
    // NULL.
    [Theory]
    [InlineData("INGESTA")]
    [InlineData("ADJUNTOS")]
    [InlineData("TELEGRAM")]
    [InlineData("NOTIFICACIONES")]
    [InlineData("INTEGRACIONES")]
    [InlineData("CONTABILIDAD")]
    public async Task Configuracion_EverySection_HasAtLeastOneRow(string seccion)
    {
        await using var db = await MigratedDatabaseWithBaseData();

        var count = await db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.Configuracion WHERE Seccion = '{seccion}';");
        Assert.True(count >= 1, $"Expected at least one Configuracion row for section {seccion}.");
    }

    [Theory]
    [InlineData("INGESTA", "ETIQUETA_ORIGEN")]
    [InlineData("INGESTA", "EXTENSIONES_PERMITIDAS")]
    [InlineData("INGESTA", "FRECUENCIA_SONDEO_MINUTOS")]
    [InlineData("INGESTA", "FECHA_INICIO")]
    [InlineData("INGESTA", "ETIQUETA_PROCESADO")]
    [InlineData("ADJUNTOS", "TIPOS_PERMITIDOS")]
    [InlineData("ADJUNTOS", "TAMANO_MAXIMO_BYTES")]
    [InlineData("TELEGRAM", "DESTINO_CHAT_ID")]
    [InlineData("NOTIFICACIONES", "PREFERENCIA_PRESENTACION")]
    [InlineData("INTEGRACIONES", "INTERVALO_ESPERADO_GMAIL")]
    [InlineData("INTEGRACIONES", "INTERVALO_ESPERADO_DRIVE")]
    [InlineData("INTEGRACIONES", "INTERVALO_ESPERADO_SHEETS")]
    [InlineData("INTEGRACIONES", "INTERVALO_ESPERADO_SBS")]
    [InlineData("CONTABILIDAD", "FECHA_CORTE_CONTABLE")]
    // BACKLOG #6 (migration 014): EMPRESA.RUC is NULL-seeded like every other undecided key --
    // used to exclude the company's own RUC when a PDF-only document shows two RUCs and no XML is
    // present to disambiguate (design.md, Open Question 1).
    [InlineData("EMPRESA", "RUC")]
    public async Task Configuracion_PendienteKeys_HaveValorAndValorPorDefectoBothNull(string seccion, string clave)
    {
        await using var db = await MigratedDatabaseWithBaseData();

        var row = await db.ExecuteAsUserAsync("usr_api", async connection =>
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT Valor, ValorPorDefecto FROM fact.Configuracion WHERE Seccion = @s AND Clave = @c;";
            command.Parameters.AddWithValue("@s", seccion);
            command.Parameters.AddWithValue("@c", clave);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), $"Expected a Configuracion row for {seccion}.{clave}.");
            return (Valor: reader.IsDBNull(0) ? null : reader.GetString(0),
                    ValorPorDefecto: reader.IsDBNull(1) ? null : reader.GetString(1));
        });

        Assert.Null(row.Valor);
        Assert.Null(row.ValorPorDefecto);
    }

    // The two documented, non-pendiente defaults — traced to a specific source each (see
    // 009_datos_base.sql's own comments).
    [Fact]
    public async Task Configuracion_CanalAlertaFallback_DefaultsToCorreo()
    {
        await using var db = await MigratedDatabaseWithBaseData();

        var valorPorDefecto = await db.ExecuteScalarAsync<string>(
            "SELECT ValorPorDefecto FROM fact.Configuracion WHERE Seccion = 'NOTIFICACIONES' AND Clave = 'CANAL_ALERTA_FALLBACK';");
        Assert.Equal("CORREO", valorPorDefecto);
    }

    [Fact]
    public async Task Configuracion_IntervaloEsperadoWorker_DefaultsToThirtyMinutes()
    {
        await using var db = await MigratedDatabaseWithBaseData();

        var valorPorDefecto = await db.ExecuteScalarAsync<string>(
            "SELECT ValorPorDefecto FROM fact.Configuracion WHERE Seccion = 'INTEGRACIONES' AND Clave = 'INTERVALO_ESPERADO_WORKER';");
        Assert.Equal("30", valorPorDefecto);
    }

    // Task 4.3 — MotivoAtributo: exactly the 23 dagger-marked motives, all OrigenLibro='02',
    // Activo=1; no other motive from the dbo.Motivo test fixture is reclassified.
    [Fact]
    public async Task MotivoAtributo_ContainsExactlyTheTwentyThreeReclassifiedMotives()
    {
        await using var db = await MigratedDatabaseWithBaseData();

        var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fact.MotivoAtributo;");
        Assert.Equal(23, count);

        int[] expected =
        [
            5, 13, 16, 17, 18, 19, 20, 21, 30, 38, 40, 42, 46, 48, 49, 53, 56, 59, 60, 77, 81, 88, 90
        ];

        foreach (var motivo in expected)
        {
            var row = await db.ExecuteAsUserAsync("usr_api", async connection =>
            {
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "SELECT OrigenLibro, Activo FROM fact.MotivoAtributo WHERE Motivo = @m;";
                command.Parameters.AddWithValue("@m", motivo);
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync(), $"Expected fact.MotivoAtributo to contain motivo {motivo}.");
                return (OrigenLibro: reader.GetString(0), Activo: reader.GetBoolean(1));
            });

            Assert.Equal("02", row.OrigenLibro);
            Assert.True(row.Activo, $"Expected motivo {motivo} to be Activo.");
        }
    }

    [Theory]
    [InlineData(11)] // plain '02', never dagger-marked
    [InlineData(12)]
    [InlineData(22)]
    [InlineData(1)] // BAJA
    [InlineData(28)] // BAJA
    public async Task MotivoAtributo_DoesNotReclassify_MotivesNotMarkedInTheSourceDocument(int motivo)
    {
        await using var db = await MigratedDatabaseWithBaseData();

        var count = await db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.MotivoAtributo WHERE Motivo = {motivo};");
        Assert.Equal(0, count);
    }

    // Task 4.4 — Usuario stays empty; no INSERT targeting it anywhere in the versioned SQL.
    [Fact]
    public async Task Usuario_IsEmpty_AfterMigration()
    {
        await using var db = await MigratedDatabaseWithBaseData();

        var count = await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM fact.Usuario;");
        Assert.Equal(0, count);
    }

    [Fact]
    public void NoVersionedScript_InsertsIntoFactUsuario()
    {
        var schemaPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "schema"));
        Assert.True(Directory.Exists(schemaPath), $"Expected to find SmartNet/db/schema/ at {schemaPath}.");

        var scripts = Directory.GetFiles(schemaPath, "*.sql");
        Assert.NotEmpty(scripts);

        foreach (var script in scripts)
        {
            var content = File.ReadAllText(script);
            var insertIntoUsuario = new System.Text.RegularExpressions.Regex(
                @"INSERT\s+(INTO\s+)?fact\.Usuario\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            Assert.DoesNotMatch(insertIntoUsuario, content);
        }
    }

    private static async Task<TestDatabaseFixture> MigratedDatabaseWithBaseData()
    {
        var db = await TestDatabaseFixture.CreateAsync();
        // See PermissionMatrixTests.MigratedDatabaseWithUsers() (Work Unit 3) for why this must be
        // try/catch, not a bare local: a throw before `return db;` would otherwise leak the
        // already-created database.
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

    private static async Task<List<string>> QueryStrings(TestDatabaseFixture db, string sql)
    {
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
