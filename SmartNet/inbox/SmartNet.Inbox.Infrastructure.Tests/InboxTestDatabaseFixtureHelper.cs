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

    /// <summary>Inserts one <c>PENDIENTE</c> <c>fact.InboxEvent</c> row and returns its id.</summary>
    public static async Task<long> InsertarInboxEventAsync(
        this TestDatabaseFixture db, long procesamientoId, string payloadJson)
    {
        var payloadEscaped = payloadJson.Replace("'", "''");
        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.InboxEvent (Tipo, ProcesamientoId, Payload)
             VALUES ('PROCESAMIENTO_FINALIZADO', {procesamientoId}, N'{payloadEscaped}');
             """);
        return await db.ExecuteScalarAsync<long>(
            $"SELECT MAX(InboxEventId) FROM fact.InboxEvent WHERE ProcesamientoId = {procesamientoId};");
    }
}
