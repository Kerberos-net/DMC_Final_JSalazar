using SmartNet.Db.TestBootstrap;

namespace SmartNet.Api.Tests;

/// <summary>
/// tasks.md Phase 3 (PR 3, BACKLOG #12) -- inserts a <c>fact.DocumentoFactura</c> row directly by
/// SQL, mirroring <c>FacturaTestDataHelper</c>'s own precedent: nothing in <c>SmartNet.Api</c> POSTs
/// this table (it is populated only at promoción, out of this PR's scope, PR 2) so a test needing a
/// pre-existing projection row inserts it the same way <c>fact.AsientoContable</c> fixture rows are
/// seeded.
/// </summary>
internal static class DocumentoTestDataHelper
{
    public static async Task<long> InsertarDocumentoFacturaAsync(
        this TestDatabaseFixture db,
        long facturaId,
        long documentoRecibidoId,
        string nombreArchivo = "factura-escaneada.pdf",
        string mimeType = "application/pdf",
        string rutaRelativa = "2026/08/factura-escaneada.pdf",
        long tamanoBytes = 2048)
    {
        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.DocumentoFactura (FacturaId, DocumentoRecibidoId, NombreArchivo, MimeType, RutaRelativa, TamanoBytes)
             VALUES ({facturaId}, {documentoRecibidoId}, '{nombreArchivo}', '{mimeType}', '{rutaRelativa}', {tamanoBytes});
             """);
        return await db.ExecuteScalarAsync<long>(
            $"SELECT MAX(DocumentoFacturaId) FROM fact.DocumentoFactura WHERE FacturaId = {facturaId};");
    }
}
