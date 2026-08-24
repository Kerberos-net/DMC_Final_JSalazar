using SmartNet.Db.TestBootstrap;

namespace SmartNet.Inbox.Infrastructure.Tests;

/// <summary>
/// Shared setup for every WU3 integration test: a migrated <c>fact_test_&lt;id&gt;</c> database with
/// the loginless <c>usr_api</c>/<c>usr_worker</c> principals (design.md "How the ADR 0019 level-2
/// tests reach a database"), plus the FK chain <c>Email -&gt; DocumentoRecibido -&gt; Procesamiento
/// -&gt; InboxEvent</c> a real <c>fact.InboxEvent</c> row needs (mirrors
/// SmartNet.Db.Runner.Tests.PermissionMatrixTests' own fixture chain).
/// </summary>
internal static class InboxTestDatabaseFixtureHelper
{
    public static async Task<TestDatabaseFixture> MigratedDatabaseAsync()
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

    /// <summary>Inserts one committed <c>fact.Procesamiento</c> row (via its FK chain) and returns
    /// its <c>ProcesamientoId</c> -- the id every <c>fact.InboxEvent</c> fixture row needs.</summary>
    public static async Task<long> InsertarProcesamientoAsync(
        this TestDatabaseFixture db, string estado = "COMPLETADO", string gmailMessageId = "msg-inbox-1")
    {
        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.Email (GmailMessageId, Remitente, Asunto, FechaRecepcion, FechaDeteccion, Estado)
             VALUES ('{gmailMessageId}', 'a@b.com', 'Factura', SYSUTCDATETIME(), SYSUTCDATETIME(), 'CANDIDATO');
             """);
        var emailId = await db.ExecuteScalarAsync<long>(
            $"SELECT MAX(EmailId) FROM fact.Email WHERE GmailMessageId = '{gmailMessageId}';");

        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.DocumentoRecibido
                 (EmailId, GmailMessageId, NombreArchivo, Extension, MimeType, TamanoBytes, HashContenido, RutaRelativa, Estado)
             VALUES
                 ({emailId}, '{gmailMessageId}', 'f.pdf', 'pdf', 'application/pdf', 10, REPLICATE('a', 64), '/f.pdf', 'PROCESADO');
             """);
        var documentoId = await db.ExecuteScalarAsync<long>(
            $"SELECT MAX(DocumentoRecibidoId) FROM fact.DocumentoRecibido WHERE EmailId = {emailId};");

        await db.ExecuteNonQueryAsync(
            $"INSERT INTO fact.Procesamiento (DocumentoRecibidoId, Estado) VALUES ({documentoId}, '{estado}');");
        return await db.ExecuteScalarAsync<long>(
            $"SELECT MAX(ProcesamientoId) FROM fact.Procesamiento WHERE DocumentoRecibidoId = {documentoId};");
    }

    /// <summary>Inserts one <c>PENDIENTE</c> <c>fact.InboxEvent</c> row and returns its id.
    /// <paramref name="creadoEn"/> lets Phase 3 tests force a specific/duplicate timestamp (task
    /// 3.1's <c>OFFSET/FETCH</c> tiebreaker, task 3.2's <c>desde</c>/<c>hasta</c> boundary) -- the
    /// column default is <c>SYSUTCDATETIME()</c>, too coarse-grained to control from the test.</summary>
    public static async Task<long> InsertarInboxEventAsync(
        this TestDatabaseFixture db, long procesamientoId, string payloadJson, DateTime? creadoEn = null)
    {
        var payloadEscaped = payloadJson.Replace("'", "''");
        var creadoEnSql = creadoEn is null
            ? "DEFAULT"
            : $"'{creadoEn.Value:yyyy-MM-ddTHH:mm:ss.fff}'";
        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.InboxEvent (Tipo, ProcesamientoId, Payload, CreadoEn)
             VALUES ('PROCESAMIENTO_FINALIZADO', {procesamientoId}, N'{payloadEscaped}', {creadoEnSql});
             """);
        return await db.ExecuteScalarAsync<long>(
            $"SELECT MAX(InboxEventId) FROM fact.InboxEvent WHERE ProcesamientoId = {procesamientoId};");
    }

    /// <summary>Inserts one <c>fact.ProcesamientoError</c> row (ADR 0003 revision 6, task 3.4/3.7's
    /// second-result-set/permission fixtures) and returns its id.</summary>
    public static async Task<long> InsertarProcesamientoErrorAsync(
        this TestDatabaseFixture db, long procesamientoId, string clasificacion = "TRANSITORIO",
        string integracion = "GMAIL", string mensaje = "Fallo transitorio", DateTime? ocurridoEn = null)
    {
        var fecha = ocurridoEn ?? DateTime.UtcNow;
        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.ProcesamientoError (ProcesamientoId, Integracion, Mensaje, Clasificacion, OcurridoEn)
             VALUES ({procesamientoId}, '{integracion}', N'{mensaje}', '{clasificacion}', '{fecha:yyyy-MM-ddTHH:mm:ss.fff}');
             """);
        return await db.ExecuteScalarAsync<long>(
            $"SELECT MAX(ProcesamientoErrorId) FROM fact.ProcesamientoError WHERE ProcesamientoId = {procesamientoId};");
    }

    /// <summary>Inserts one <c>fact.CommandQueue</c> row for a <c>REPROCESAR_DOCUMENTO</c> command
    /// (task 3.5's <c>reprocesarDisponibleEn</c> fixtures) and returns its id.</summary>
    public static async Task<long> InsertarCommandQueueReprocesarAsync(
        this TestDatabaseFixture db, long procesamientoId, string estado = "PENDIENTE", DateTime? creadoEn = null)
    {
        var fecha = creadoEn ?? DateTime.UtcNow;
        const string payloadVacio = "{}";
        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.CommandQueue (Tipo, Referencia, Payload, Estado, CorrelationId, CreadoEn)
             VALUES ('REPROCESAR_DOCUMENTO', {procesamientoId}, N'{payloadVacio}', '{estado}', NEWID(), '{fecha:yyyy-MM-ddTHH:mm:ss.fff}');
             """);
        return await db.ExecuteScalarAsync<long>(
            $"SELECT MAX(CommandQueueId) FROM fact.CommandQueue WHERE Referencia = {procesamientoId};");
    }
}
