using SmartNet.Db.TestBootstrap;

namespace SmartNet.Db.Runner.Tests;

/// <summary>
/// `Factura.RucProveedor` and `DatosExtraidos.RucProveedor` hold the emitter's tax identifier, and
/// that identifier is **not always a RUC**. Of the 6600 rows in `dbo.Proveedor`, 6476 carry an
/// 11-digit RUC, 118 an 8-digit DNI and 6 a 9-or-10-digit carne de extranjeria. The original
/// `CHAR(11)` plus an exactly-eleven-digits CHECK would have rejected an invoice from any of those
/// 124 suppliers outright — the column widened to 8-11 digits by accounting decision.
///
/// The type matters as much as the length. `CHAR` pads to its declared width, so an 8-digit DNI
/// would be stored as `'12345678   '` and would never equal the `VARCHAR` value in
/// `dbo.Proveedor.rucpro`; it would also enter `IX_Factura_Identidad` padded, so duplicate
/// detection would silently miss. That is the same class of defect as the trailing `\r` that the
/// catalog load once carried: invisible whitespace inside a key.
/// </summary>
public sealed class RucProveedorIdentifierTests
{
    [Theory]
    [InlineData("20123456789", true)]  // RUC, 11 digits
    [InlineData("12345678", true)]     // DNI, 8 digits
    [InlineData("123456789", true)]    // carne de extranjeria, 9
    [InlineData("1234567890", true)]   // carne de extranjeria, 10
    [InlineData(null, true)]           // not extracted yet — normative
    [InlineData("1234567", false)]     // 7 digits, shorter than any real identifier
    [InlineData("1234567A", false)]    // non-numeric
    [InlineData("1234 5678", false)]   // embedded space
    public async Task Factura_AcceptsEveryRealIdentifierLength_AndRejectsTheRest(string? valor, bool shouldSucceed)
    {
        await using var db = await MigratedDatabase();

        var literal = valor is null ? "NULL" : $"'{valor}'";
        var ex = await Record.ExceptionAsync(() => db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.Factura (ProveedorCodigo, RucProveedor, TipoComprobante, TotalOrig, Moneda, FechaEmision)
             VALUES ('P00000', {literal}, '01', 100.00, 'PEN', '2026-01-01');
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

    [Theory]
    [InlineData("20123456789", true)]
    [InlineData("12345678", true)]
    [InlineData("1234567", false)]
    [InlineData("1234567A", false)]
    public async Task DatosExtraidos_EnforcesTheSameRule(string valor, bool shouldSucceed)
    {
        await using var db = await MigratedDatabase();

        var procesamientoId = await CreateProcesamiento(db);

        var ex = await Record.ExceptionAsync(() => db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.DatosExtraidos (ProcesamientoId, RucProveedor)
             VALUES ({procesamientoId}, '{valor}');
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

    /// <summary>
    /// The type itself, not only the constraint: a fixed-length column would pad and the padding
    /// would travel into every comparison and into `IX_Factura_Identidad`.
    /// </summary>
    [Fact]
    public async Task RucProveedor_IsVariableLength_AndStoresAShortIdentifierWithoutPadding()
    {
        await using var db = await MigratedDatabase();

        var fixedLengthColumns = await db.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.columns c
            JOIN sys.tables t ON t.object_id = c.object_id
            JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE c.name = 'RucProveedor' AND ty.name = 'char';
            """);
        Assert.Equal(0, fixedLengthColumns);

        await db.ExecuteNonQueryAsync(
            """
            INSERT INTO fact.Factura (ProveedorCodigo, RucProveedor, TipoComprobante, TotalOrig, Moneda, FechaEmision)
            VALUES ('P00000', '12345678', '01', 100.00, 'PEN', '2026-01-01');
            """);

        var storedLength = await db.ExecuteScalarAsync<int>(
            "SELECT DATALENGTH(RucProveedor) FROM fact.Factura WHERE RucProveedor = '12345678';");
        Assert.Equal(8, storedLength);
    }


    /// <summary>
    /// `DatosExtraidos` hangs off a real `Procesamiento`, which hangs off a `DocumentoRecibido` and
    /// an `Email`. Built here rather than relaxed in the schema: the chain is the point.
    /// </summary>
    private static async Task<long> CreateProcesamiento(TestDatabaseFixture db)
    {
        await db.ExecuteNonQueryAsync(
            """
            INSERT INTO fact.Email (GmailMessageId, Remitente, Asunto, FechaRecepcion, FechaDeteccion, Estado)
            VALUES ('msg-ruc', 'a@b.com', 'Factura', SYSUTCDATETIME(), SYSUTCDATETIME(), 'CANDIDATO');
            """);
        var emailId = await db.ExecuteScalarAsync<long>(
            "SELECT EmailId FROM fact.Email WHERE GmailMessageId = 'msg-ruc';");

        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.DocumentoRecibido (EmailId, GmailMessageId, NombreArchivo, Extension, MimeType,
                                                TamanoBytes, HashContenido, RutaRelativa, Estado)
             VALUES ({emailId}, 'msg-ruc', 'f.pdf', 'pdf', 'application/pdf', 10, REPLICATE('a', 64), '/f.pdf', 'DESCARGADO');
             """);
        var documentoId = await db.ExecuteScalarAsync<long>(
            "SELECT DocumentoRecibidoId FROM fact.DocumentoRecibido WHERE GmailMessageId = 'msg-ruc';");

        await db.ExecuteNonQueryAsync(
            $"INSERT INTO fact.Procesamiento (DocumentoRecibidoId, Estado) VALUES ({documentoId}, 'PENDIENTE');");
        return await db.ExecuteScalarAsync<long>(
            $"SELECT ProcesamientoId FROM fact.Procesamiento WHERE DocumentoRecibidoId = {documentoId};");
    }
    private static async Task<TestDatabaseFixture> MigratedDatabase()
    {
        var db = await TestDatabaseFixture.CreateAsync();
        try
        {
            await db.CreateWithoutLoginUserAsync("usr_api");
            await db.CreateWithoutLoginUserAsync("usr_worker");
            await db.CreateExternalDboCatalogsAsync();
            await db.SeedDboMotivoFixtureRowsAsync();
            Assert.Equal(0, db.RunMigrations());
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }
}
