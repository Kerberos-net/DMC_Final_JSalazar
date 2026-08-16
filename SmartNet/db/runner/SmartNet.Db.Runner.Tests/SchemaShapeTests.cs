using SmartNet.Db.TestBootstrap;

namespace SmartNet.Db.Runner.Tests;

/// <summary>
/// Work Unit 2 (Phase 2, tasks 2.1, 2.7, 2.11, 2.12) — schema-shape assertions against the real
/// scripts under SmartNet/db/schema/ (001-007), applied by the real runner against a throwaway
/// fact_test_&lt;id&gt; database. No mocked catalog views: sys.tables/sys.columns/sys.indexes read
/// the actual engine metadata (design.md, "How the ADR 0019 level-2 tests reach a database").
/// </summary>
public sealed class SchemaShapeTests
{
    // Task 2.1 — full table inventory, all in schema fact, external catalogs absent.
    [Fact]
    public async Task AllExpectedTables_ExistInSchemaFact_AndExternalCatalogsAreAbsent()
    {
        await using var db = await MigratedDatabase();

        string[] expected =
        [
            "Email", "DocumentoRecibido", "Procesamiento", "DatosExtraidos", "ProcesamientoError",
            "ProcesamientoIntentos", "Factura", "AsientoContable", "AsientoContableDetalle",
            "AdjuntoManual", "AuditoriaCorreccion", "FacturaExtraccion", "CorrelativoAsiento",
            "ProveedorAtributo", "MotivoAtributo", "SugerenciaCuenta", "Usuario", "OutboxEvent",
            "OutboxEventIntegracion", "CommandQueue", "InboxEvent", "TipoCambio", "Configuracion",
            "EstadoIntegracion"
        ];

        foreach (var table in expected)
        {
            var count = await db.ExecuteScalarAsync<int>(
                $"""
                 SELECT COUNT(*) FROM sys.tables t
                 JOIN sys.schemas s ON t.schema_id = s.schema_id
                 WHERE s.name = 'fact' AND t.name = '{table}';
                 """);
            Assert.True(count == 1, $"Expected exactly one fact.{table}, found {count}.");
        }

        foreach (var external in new[] { "Proveedor", "CuentaContable", "Motivo", "Origen" })
        {
            var count = await db.ExecuteScalarAsync<int>(
                $"""
                 SELECT COUNT(*) FROM sys.tables t
                 JOIN sys.schemas s ON t.schema_id = s.schema_id
                 WHERE s.name = 'fact' AND t.name = '{external}';
                 """);
            Assert.Equal(0, count);
        }
    }

    [Fact]
    public async Task NoTableCreatedByThisProject_ExistsOutsideSchemaFact()
    {
        await using var db = await MigratedDatabase();

        // Only fact and the built-in schemas should own tables created BY THIS PROJECT after
        // migration; the journal itself lives in fact too (RunnerJournalTests already covers that
        // specifically). `dbo` is excluded here, not because a versioned script could ever write
        // there (spec.md's Non-Goals forbid it outright, and 008's own GRANT-only statements are
        // the only mention of `dbo` in any script) but because MigratedDatabase() now creates the
        // four external dbo.* catalogs as test-only fixtures — 008's GRANT SELECT ON OBJECT::dbo.*
        // needs an object to grant on. Those tables exist in every real deployment already (the
        // accounting system owns them); they are not evidence of this project writing outside fact.
        var stray = await db.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM sys.tables t
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name NOT IN ('fact', 'dbo');
            """);
        Assert.Equal(0, stray);
    }

    // Task 2.7 — IX_Factura_Identidad is filtered and non-unique.
    [Fact]
    public async Task IXFacturaIdentidad_IsNonUnique()
    {
        await using var db = await MigratedDatabase();

        var isUnique = await db.ExecuteScalarAsync<int>(
            """
            SELECT CAST(is_unique AS INT) FROM sys.indexes
            WHERE object_id = OBJECT_ID('fact.Factura') AND name = 'IX_Factura_Identidad';
            """);
        Assert.Equal(0, isUnique);
    }

    [Fact]
    public async Task TwoPendienteValidacion_WithSameIdentity_BothInsertable()
    {
        await using var db = await MigratedDatabase();

        for (var i = 0; i < 2; i++)
        {
            await db.ExecuteNonQueryAsync(
                """
                INSERT INTO fact.Factura (ProveedorCodigo, RucProveedor, TipoComprobante, Numero,
                    TotalOrig, Moneda, FechaEmision, Estado)
                VALUES ('P00000', '20123456789', '01', 'F001-00001', 100.00, 'PEN', '2026-08-01',
                    'PENDIENTE_VALIDACION');
                """);
        }

        var count = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.Factura WHERE Numero = 'F001-00001';");
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task TwoInvoices_WithNullNumero_BothInsertable()
    {
        await using var db = await MigratedDatabase();

        for (var i = 0; i < 2; i++)
        {
            await db.ExecuteNonQueryAsync(
                """
                INSERT INTO fact.Factura (ProveedorCodigo, RucProveedor, TipoComprobante, Numero,
                    TotalOrig, Moneda, FechaEmision, Estado)
                VALUES ('P00000', '20123456789', '01', NULL, 100.00, 'PEN', '2026-08-01',
                    'PENDIENTE_VALIDACION');
                """);
        }

        var count = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.Factura WHERE RucProveedor = '20123456789' AND Numero IS NULL;");
        Assert.Equal(2, count);
    }

    // Task 2.7 — UQ_Factura_Procesamiento
    [Fact]
    public async Task UQFacturaProcesamiento_RejectsSecondPromotionOfSameProcesamiento()
    {
        await using var db = await MigratedDatabase();

        var procesamientoId = await CreateProcesamiento(db);

        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.Factura (ProcesamientoId, ProveedorCodigo, TipoComprobante, TotalOrig,
                 Moneda, FechaEmision, Estado)
             VALUES ({procesamientoId}, 'P00000', '01', 100.00, 'PEN', '2026-08-01',
                 'PENDIENTE_VALIDACION');
             """);

        var ex = await Record.ExceptionAsync(() => db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.Factura (ProcesamientoId, ProveedorCodigo, TipoComprobante, TotalOrig,
                 Moneda, FechaEmision, Estado)
             VALUES ({procesamientoId}, 'P00000', '01', 200.00, 'PEN', '2026-08-01',
                 'PENDIENTE_VALIDACION');
             """));

        Assert.NotNull(ex);
    }

    [Fact]
    public async Task UQFacturaProcesamiento_AllowsTwoNullProcesamientoId()
    {
        await using var db = await MigratedDatabase();

        for (var i = 0; i < 2; i++)
        {
            await db.ExecuteNonQueryAsync(
                """
                INSERT INTO fact.Factura (ProcesamientoId, ProveedorCodigo, TipoComprobante, TotalOrig,
                    Moneda, FechaEmision, Estado)
                VALUES (NULL, 'P00000', '01', 100.00, 'PEN', '2026-08-01', 'PENDIENTE_VALIDACION');
                """);
        }

        var count = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM fact.Factura WHERE ProcesamientoId IS NULL;");
        Assert.Equal(2, count);
    }

    // Task 2.7 — UQ_Asiento_Vigente
    [Fact]
    public async Task FacturaMayAccumulateManyAnuladoAsientos()
    {
        await using var db = await MigratedDatabase();
        var facturaId = await CreateFactura(db);

        for (var i = 0; i < 3; i++)
        {
            await db.ExecuteNonQueryAsync(
                $"""
                 INSERT INTO fact.AsientoContable (FacturaId, OrigenLibro, ProveedorCodigo, FechaContable, Estado)
                 VALUES ({facturaId}, '02', 'P00000', '2026-08-01', 'ANULADO');
                 """);
        }

        var count = await db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM fact.AsientoContable WHERE FacturaId = {facturaId};");
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task SecondNonAnuladoAsiento_ForSameFactura_IsRejected()
    {
        await using var db = await MigratedDatabase();
        var facturaId = await CreateFactura(db);

        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.AsientoContable (FacturaId, OrigenLibro, ProveedorCodigo, FechaContable, Estado)
             VALUES ({facturaId}, '02', 'P00000', '2026-08-01', 'CONFIRMADO');
             """);

        var ex = await Record.ExceptionAsync(() => db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.AsientoContable (FacturaId, OrigenLibro, ProveedorCodigo, FechaContable, Estado)
             VALUES ({facturaId}, '02', 'P00000', '2026-08-01', 'BORRADOR');
             """));

        Assert.NotNull(ex);
    }

    // Task 2.7 — CK_Linea_Tipo, four accept/reject cases
    [Theory]
    [InlineData("D", 100.00, 0, true)]
    [InlineData("H", 0, 100.00, true)]
    [InlineData("D", 100.00, 50.00, false)]
    [InlineData("H", 100.00, 50.00, false)] // note: Haber carries an amount but so does Debe -> rejected
    public async Task CkLineaTipo_EnforcesDebitCreditShape(string tipo, decimal debe, decimal haber, bool shouldSucceed)
    {
        await using var db = await MigratedDatabase();
        var asientoId = await CreateAsientoContable(db);

        var ex = await Record.ExceptionAsync(() => db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.AsientoContableDetalle (AsientoContableId, Orden, Bloque, Tipo, Debe, Haber)
             VALUES ({asientoId}, 1, 'PRINCIPAL', '{tipo}', {debe.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {haber.ToString(System.Globalization.CultureInfo.InvariantCulture)});
             """));

        if (shouldSucceed)
        {
            Assert.Null(ex);
        }
        else
        {
            Assert.NotNull(ex);
        }
    }

    [Fact]
    public async Task LineWithZeroAmountForItsOwnType_IsRejected()
    {
        await using var db = await MigratedDatabase();
        var asientoId = await CreateAsientoContable(db);

        var ex = await Record.ExceptionAsync(() => db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.AsientoContableDetalle (AsientoContableId, Orden, Bloque, Tipo, Debe, Haber)
             VALUES ({asientoId}, 1, 'PRINCIPAL', 'D', 0, 0);
             """));

        Assert.NotNull(ex);
    }

    // Task 2.7 — CorrelativoAsiento is a plain counter, not SEQUENCE/IDENTITY.
    [Fact]
    public async Task CorrelativoAsiento_RejectsDuplicatePrimaryKey()
    {
        await using var db = await MigratedDatabase();

        await db.ExecuteNonQueryAsync(
            "INSERT INTO fact.CorrelativoAsiento (Anio, Mes, Origen, Ultimo) VALUES (2026, 8, '02', 1);");

        var ex = await Record.ExceptionAsync(() => db.ExecuteNonQueryAsync(
            "INSERT INTO fact.CorrelativoAsiento (Anio, Mes, Origen, Ultimo) VALUES (2026, 8, '02', 2);"));

        Assert.NotNull(ex);
    }

    [Fact]
    public async Task CorrelativoAsiento_IsNotBackedBySequenceOrIdentity()
    {
        await using var db = await MigratedDatabase();

        var identityCount = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.identity_columns WHERE object_id = OBJECT_ID('fact.CorrelativoAsiento');");
        Assert.Equal(0, identityCount);

        var sequenceCount = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sys.sequences WHERE name LIKE '%CorrelativoAsiento%';");
        Assert.Equal(0, sequenceCount);
    }

    // Task 2.12 — rowversion on Factura and AsientoContable only.
    [Fact]
    public async Task FacturaAndAsientoContable_CarryExactlyOneRowversionColumn_NamedVersion()
    {
        await using var db = await MigratedDatabase();

        foreach (var table in new[] { "Factura", "AsientoContable" })
        {
            var count = await db.ExecuteScalarAsync<int>(
                $"""
                 SELECT COUNT(*) FROM sys.columns c
                 JOIN sys.types ty ON c.system_type_id = ty.system_type_id AND ty.name = 'timestamp'
                 WHERE c.object_id = OBJECT_ID('fact.{table}') AND c.name = 'Version';
                 """);
            Assert.True(count == 1, $"Expected exactly one rowversion column named Version on fact.{table}.");
        }
    }

    [Fact]
    public async Task AsientoContableDetalle_DoesNotCarryARowversionColumn()
    {
        await using var db = await MigratedDatabase();

        var count = await db.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM sys.columns c
            JOIN sys.types ty ON c.system_type_id = ty.system_type_id AND ty.name = 'timestamp'
            WHERE c.object_id = OBJECT_ID('fact.AsientoContableDetalle');
            """);
        Assert.Equal(0, count);
    }

    // Task 2.11 — no float/real anywhere in schema fact; DECIMAL(18,2) money, DECIMAL(12,6) rates.
    [Fact]
    public async Task NoColumnInSchemaFact_UsesAFloatingPointType()
    {
        await using var db = await MigratedDatabase();

        var count = await db.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM sys.columns c
            JOIN sys.objects o ON c.object_id = o.object_id
            JOIN sys.schemas s ON o.schema_id = s.schema_id
            JOIN sys.types ty ON c.system_type_id = ty.system_type_id
            WHERE s.name = 'fact' AND ty.name IN ('float', 'real');
            """);
        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData("Factura", "TotalOrig")]
    [InlineData("AsientoContableDetalle", "Debe")]
    [InlineData("AsientoContableDetalle", "Haber")]
    public async Task MonetaryColumns_AreDecimal18_2(string table, string column)
    {
        await using var db = await MigratedDatabase();

        var row = await db.ExecuteScalarAsync<string>(
            $"""
             SELECT CAST(c.precision AS VARCHAR) + ',' + CAST(c.scale AS VARCHAR)
             FROM sys.columns c
             JOIN sys.types ty ON c.system_type_id = ty.system_type_id AND ty.name = 'decimal'
             WHERE c.object_id = OBJECT_ID('fact.{table}') AND c.name = '{column}';
             """);
        Assert.Equal("18,2", row);
    }

    [Theory]
    [InlineData("TipoCambio", "Compra")]
    [InlineData("TipoCambio", "Venta")]
    [InlineData("AsientoContable", "TipoCambioVenta")]
    public async Task ExchangeRateColumns_AreDecimal12_6(string table, string column)
    {
        await using var db = await MigratedDatabase();

        var row = await db.ExecuteScalarAsync<string>(
            $"""
             SELECT CAST(c.precision AS VARCHAR) + ',' + CAST(c.scale AS VARCHAR)
             FROM sys.columns c
             JOIN sys.types ty ON c.system_type_id = ty.system_type_id AND ty.name = 'decimal'
             WHERE c.object_id = OBJECT_ID('fact.{table}') AND c.name = '{column}';
             """);
        Assert.Equal("12,6", row);
    }

    private static async Task<TestDatabaseFixture> MigratedDatabase()
    {
        var db = await TestDatabaseFixture.CreateAsync();
        // The `db` fixture is intentionally wrapped in its own try/catch here, not left as a bare
        // local: if anything below throws before `return db;`, the caller's own
        // `await using var db = await MigratedDatabase();` never runs, because the assignment
        // itself never completes — the already-created database would leak with nothing left to
        // dispose it (the confirmed root cause of the Work Unit 3 test-database leak; see
        // apply-progress "Coordinator-directed follow-up, item 3").
        try
        {
            // Phase 3 (Unit 3) added 008_usuarios_y_permisos.sql to SmartNet/db/schema/, which the
            // runner now applies as part of every migration; 008 THROWs 50001 unless
            // usr_api/usr_worker and the five external dbo.* catalogs already exist (design.md,
            // Decision 3 and the ADR 0019 section). Schema-shape tests do not exercise permissions,
            // but they share the runner and must satisfy 008's premise to reach 001-007's own
            // assertions.
            await db.CreateWithoutLoginUserAsync("usr_api");
            await db.CreateWithoutLoginUserAsync("usr_worker");
            await db.CreateExternalDboCatalogsAsync();
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

    private static async Task<long> CreateProcesamiento(TestDatabaseFixture db)
    {
        await db.ExecuteNonQueryAsync(
            """
            INSERT INTO fact.Email (GmailMessageId, Remitente, Asunto, FechaRecepcion, FechaDeteccion, Estado)
            VALUES ('abc123', 'proveedor@example.com', 'Factura', SYSUTCDATETIME(), SYSUTCDATETIME(), 'CANDIDATO');
            """);
        var emailId = await db.ExecuteScalarAsync<long>("SELECT MAX(EmailId) FROM fact.Email;");

        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.DocumentoRecibido (EmailId, GmailMessageId, NombreArchivo, Extension,
                 MimeType, TamanoBytes, HashContenido, RutaRelativa, Estado)
             VALUES ({emailId}, 'abc123', 'factura.pdf', 'pdf', 'application/pdf', 1024,
                 REPLICATE('a', 64), 'facturas/factura.pdf', 'DESCARGADO');
             """);
        var documentoId = await db.ExecuteScalarAsync<long>("SELECT MAX(DocumentoRecibidoId) FROM fact.DocumentoRecibido;");

        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.Procesamiento (DocumentoRecibidoId, Estado)
             VALUES ({documentoId}, 'PENDIENTE');
             """);
        return await db.ExecuteScalarAsync<long>("SELECT MAX(ProcesamientoId) FROM fact.Procesamiento;");
    }

    private static async Task<long> CreateFactura(TestDatabaseFixture db)
    {
        await db.ExecuteNonQueryAsync(
            """
            INSERT INTO fact.Factura (ProveedorCodigo, TipoComprobante, TotalOrig, Moneda, FechaEmision, Estado)
            VALUES ('P00000', '01', 100.00, 'PEN', '2026-08-01', 'PENDIENTE_VALIDACION');
            """);
        return await db.ExecuteScalarAsync<long>("SELECT MAX(FacturaId) FROM fact.Factura;");
    }

    private static async Task<long> CreateAsientoContable(TestDatabaseFixture db)
    {
        var facturaId = await CreateFactura(db);
        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.AsientoContable (FacturaId, OrigenLibro, ProveedorCodigo, FechaContable, Estado)
             VALUES ({facturaId}, '02', 'P00000', '2026-08-01', 'BORRADOR');
             """);
        return await db.ExecuteScalarAsync<long>("SELECT MAX(AsientoContableId) FROM fact.AsientoContable;");
    }
}
