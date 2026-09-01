using Microsoft.Data.SqlClient;
using SmartNet.Inbox.Core;

namespace SmartNet.Inbox.Infrastructure;

/// <summary>
/// SQL adapter over the write side of promotion for <see cref="IPromocionRepository"/> (design.md
/// data flow: one <see cref="SqlTransaction"/> per event, design D2). Also implements the port's
/// two read-only "fact" helpers the background service calls BEFORE invoking Core
/// (<see cref="ResolverProveedorAsync"/> reads <c>dbo.Proveedor</c>, the one ADR 0003 "clase
/// externa" this project is granted SELECT on; <see cref="ExisteIdentidadPreviaAsync"/> reads this
/// project's own <c>fact.Factura</c> via <c>IX_Factura_Identidad</c>).
/// </summary>
public sealed class SqlPromocionRepository : IPromocionRepository
{
    private readonly string _connectionString;

    public SqlPromocionRepository(string connectionString) => _connectionString = connectionString;

    public async Task<ResultadoPromocion> PromoverAsync(
        long inboxEventId, long procesamientoId, FacturaPromovida factura, DocumentoPromovido documento,
        CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        try
        {
            long facturaId;
            var yaExistia = false;

            try
            {
                facturaId = await InsertarFacturaAsync(connection, transaction, procesamientoId, factura, ct);
                await InsertarExtraccionesAsync(connection, transaction, facturaId, factura.Extracciones, ct);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                // UQ_Factura_Procesamiento violation -- design D2: an engine invariant, not a
                // pre-check (anti-TOCTOU, same rule items #4-#6 adopted in upsert_procesamiento).
                // Resolve the row that already exists instead of ever inserting a second one.
                facturaId = await ResolverFacturaIdExistenteAsync(connection, transaction, procesamientoId, ct);
                yaExistia = true;
            }

            try
            {
                await InsertarDocumentoFacturaAsync(connection, transaction, facturaId, documento, ct);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                // UQ_DocumentoFactura_DocumentoRecibidoId violation (schema 016) -- same anti-TOCTOU
                // idempotency discipline as fact.Factura above: a re-processed InboxEvent for the
                // same ingested document already projected this row, skip it.
            }

            await MarcarPromovidoAsync(connection, transaction, inboxEventId, facturaId, ct);
            await transaction.CommitAsync(ct);
            return new ResultadoPromocion(facturaId, yaExistia);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    public async Task DescartarAsync(long inboxEventId, string motivoDescarte, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE fact.InboxEvent
            SET EstadoConsumo = 'DESCARTADO', MotivoDescarte = @motivoDescarte, ConsumidoEn = SYSUTCDATETIME()
            WHERE InboxEventId = @inboxEventId;
            """;
        command.Parameters.AddWithValue("@motivoDescarte", motivoDescarte);
        command.Parameters.AddWithValue("@inboxEventId", inboxEventId);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<ProveedorResuelto> ResolverProveedorAsync(string? rucProveedor, CancellationToken ct)
    {
        const string codigoGenerico = "P00000";
        if (string.IsNullOrEmpty(rucProveedor))
        {
            return new ProveedorResuelto(Existe: false, Codigo: codigoGenerico);
        }

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT codpro FROM dbo.Proveedor WHERE rucpro = @rucProveedor;";
        command.Parameters.AddWithValue("@rucProveedor", rucProveedor);

        var codigo = await command.ExecuteScalarAsync(ct);
        return codigo is string codigoTexto
            ? new ProveedorResuelto(Existe: true, Codigo: codigoTexto.TrimEnd())
            : new ProveedorResuelto(Existe: false, Codigo: codigoGenerico);
    }

    public async Task<bool> ExisteIdentidadPreviaAsync(
        string? rucProveedor, string tipoComprobante, string? numero, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM fact.Factura
            WHERE Estado <> 'DESCARTADA'
              AND TipoComprobante = @tipoComprobante
              AND ((@rucProveedor IS NULL AND RucProveedor IS NULL) OR RucProveedor = @rucProveedor)
              AND ((@numero IS NULL AND Numero IS NULL) OR Numero = @numero);
            """;
        command.Parameters.AddWithValue("@tipoComprobante", tipoComprobante);
        command.Parameters.AddWithValue("@rucProveedor", (object?)rucProveedor ?? DBNull.Value);
        command.Parameters.AddWithValue("@numero", (object?)numero ?? DBNull.Value);

        var count = (int)(await command.ExecuteScalarAsync(ct))!;
        return count > 0;
    }

    private static async Task<long> InsertarFacturaAsync(
        SqlConnection connection, SqlTransaction transaction, long procesamientoId, FacturaPromovida factura, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO fact.Factura
                (ProcesamientoId, ProveedorCodigo, RucProveedor, TipoComprobante, Numero, TotalOrig,
                 Moneda, FechaEmision, AfectacionMixta, EsProveedorGenerico, PosibleDuplicado,
                 TieneCamposNoExtraidos, CamposNoExtraidos, FechaEnDomingo, Estado)
            OUTPUT INSERTED.FacturaId
            VALUES
                (@procesamientoId, @proveedorCodigo, @rucProveedor, @tipoComprobante, @numero, @totalOrig,
                 @moneda, @fechaEmision, @afectacionMixta, @esProveedorGenerico, @posibleDuplicado,
                 @tieneCamposNoExtraidos, @camposNoExtraidos, @fechaEnDomingo, @estado);
            """;
        command.Parameters.AddWithValue("@procesamientoId", procesamientoId);
        command.Parameters.AddWithValue("@proveedorCodigo", factura.ProveedorCodigo);
        command.Parameters.AddWithValue("@rucProveedor", (object?)factura.RucProveedor ?? DBNull.Value);
        command.Parameters.AddWithValue("@tipoComprobante", factura.TipoComprobante);
        command.Parameters.AddWithValue("@numero", (object?)factura.Numero ?? DBNull.Value);
        command.Parameters.AddWithValue("@totalOrig", factura.TotalOrig);
        command.Parameters.AddWithValue("@moneda", factura.Moneda);
        command.Parameters.AddWithValue("@fechaEmision", factura.FechaEmision.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@afectacionMixta", (object?)factura.Indicadores.AfectacionMixta ?? DBNull.Value);
        command.Parameters.AddWithValue("@esProveedorGenerico", factura.Indicadores.EsProveedorGenerico);
        command.Parameters.AddWithValue("@posibleDuplicado", factura.Indicadores.PosibleDuplicado);
        command.Parameters.AddWithValue("@tieneCamposNoExtraidos", factura.Indicadores.TieneCamposNoExtraidos);
        // BACKLOG #19 (D8): the worker's per-field list is persisted verbatim as a CSV, an immutable
        // extraction fact. Empty list -> NULL (a factura whose every field came from the document);
        // the SPA reads NULL as "pre-021, fall back to the coarse TieneCamposNoExtraidos badge".
        command.Parameters.AddWithValue("@camposNoExtraidos",
            factura.Indicadores.CamposNoExtraidos.Count > 0
                ? string.Join(",", factura.Indicadores.CamposNoExtraidos)
                : (object)DBNull.Value);
        command.Parameters.AddWithValue("@fechaEnDomingo", factura.Indicadores.FechaEnDomingo);
        command.Parameters.AddWithValue("@estado", factura.Estado);

        var result = await command.ExecuteScalarAsync(ct);
        return (long)result!;
    }

    private static async Task InsertarExtraccionesAsync(
        SqlConnection connection, SqlTransaction transaction, long facturaId,
        IReadOnlyList<FacturaExtraccionPromovida> extracciones, CancellationToken ct)
    {
        foreach (var extraccion in extracciones)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO fact.FacturaExtraccion (FacturaId, CampoNombre, ValorExtraido, Fuente)
                VALUES (@facturaId, @campoNombre, @valorExtraido, @fuente);
                """;
            command.Parameters.AddWithValue("@facturaId", facturaId);
            command.Parameters.AddWithValue("@campoNombre", extraccion.CampoNombre);
            command.Parameters.AddWithValue("@valorExtraido", extraccion.ValorExtraido);
            command.Parameters.AddWithValue("@fuente", extraccion.Fuente);
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>BACKLOG #12 (design D1, schema 016) -- projects <paramref name="documento"/> (built
    /// entirely from the InboxEvent payload, never a SELECT against fact.DocumentoRecibido) into
    /// fact.DocumentoFactura, in the same transaction as the Factura row it references.</summary>
    private static async Task InsertarDocumentoFacturaAsync(
        SqlConnection connection, SqlTransaction transaction, long facturaId, DocumentoPromovido documento, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO fact.DocumentoFactura
                (FacturaId, DocumentoRecibidoId, NombreArchivo, MimeType, RutaRelativa, TamanoBytes)
            VALUES
                (@facturaId, @documentoRecibidoId, @nombreArchivo, @mimeType, @rutaRelativa, @tamanoBytes);
            """;
        command.Parameters.AddWithValue("@facturaId", facturaId);
        command.Parameters.AddWithValue("@documentoRecibidoId", documento.DocumentoRecibidoId);
        command.Parameters.AddWithValue("@nombreArchivo", documento.NombreArchivo);
        command.Parameters.AddWithValue("@mimeType", documento.MimeType);
        command.Parameters.AddWithValue("@rutaRelativa", documento.RutaRelativa);
        command.Parameters.AddWithValue("@tamanoBytes", documento.TamanoBytes);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// design.md Decision 2 -- Query A (the merge target, both grants inside <c>usr_api</c>), then
    /// Query B only when A is empty (distinguishes "not yet" from "never"). Never queries
    /// <c>fact.Procesamiento</c> or <c>fact.DocumentoRecibido</c> (ADR 0003 DENY).
    /// </summary>
    public async Task<ResolucionPar> ResolverParAsync(long documentoAsociadoId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var facturaId = await ResolverFacturaIdDelParAsync(connection, documentoAsociadoId, ct);
        if (facturaId is not null)
        {
            return new ResolucionPar.Fusionable(facturaId.Value);
        }

        var estadoConsumoPar = await ResolverEstadoConsumoDelParAsync(connection, documentoAsociadoId, ct);
        return estadoConsumoPar switch
        {
            "DESCARTADO" => new ResolucionPar.ParNoPromovible("El evento asociado fue descartado"),
            "PROMOVIDO" => new ResolucionPar.ParNoPromovible("La factura del evento asociado ya no está vigente"),
            _ => new ResolucionPar.NoDisponible(),
        };
    }

    private static async Task<long?> ResolverFacturaIdDelParAsync(
        SqlConnection connection, long documentoAsociadoId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT TOP(1) f.FacturaId
            FROM fact.DocumentoFactura df
            JOIN fact.Factura f ON f.FacturaId = df.FacturaId
            WHERE df.DocumentoRecibidoId = @documentoAsociadoId AND f.Estado <> 'DESCARTADA';
            """;
        command.Parameters.AddWithValue("@documentoAsociadoId", documentoAsociadoId);
        var result = await command.ExecuteScalarAsync(ct);
        return result is long facturaId ? facturaId : null;
    }

    private static async Task<string?> ResolverEstadoConsumoDelParAsync(
        SqlConnection connection, long documentoAsociadoId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT TOP(1) EstadoConsumo
            FROM fact.InboxEvent
            WHERE TRY_CAST(JSON_VALUE(Payload, '$.documento.documentoRecibidoId') AS BIGINT) = @documentoAsociadoId;
            """;
        command.Parameters.AddWithValue("@documentoAsociadoId", documentoAsociadoId);
        var result = await command.ExecuteScalarAsync(ct);
        return result as string;
    }

    /// <summary>
    /// design.md Decision 4 -- one transaction reusing <see cref="InsertarDocumentoFacturaAsync"/>
    /// (with its existing 2601/2627 idempotency catch) + <see cref="MarcarPromovidoAsync"/>. Never
    /// calls <see cref="InsertarFacturaAsync"/>/<see cref="InsertarExtraccionesAsync"/> -- no second
    /// <c>fact.Factura</c> row is ever created on this path.
    /// </summary>
    public async Task FusionarDocumentoAsync(
        long inboxEventId, long facturaId, DocumentoPromovido documento, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        try
        {
            try
            {
                await InsertarDocumentoFacturaAsync(connection, transaction, facturaId, documento, ct);
            }
            catch (SqlException ex) when (ex.Number is 2601 or 2627)
            {
                // UQ_DocumentoFactura_DocumentoRecibidoId violation -- a re-processed (reprocesar)
                // associated event already projected this row; same anti-TOCTOU idempotency
                // discipline as PromoverAsync's own catch above.
            }

            await MarcarPromovidoAsync(connection, transaction, inboxEventId, facturaId, ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            await transaction.RollbackAsync(ct);
            throw;
        }
    }

    private static async Task<long> ResolverFacturaIdExistenteAsync(
        SqlConnection connection, SqlTransaction transaction, long procesamientoId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT FacturaId FROM fact.Factura WHERE ProcesamientoId = @procesamientoId;";
        command.Parameters.AddWithValue("@procesamientoId", procesamientoId);
        var result = await command.ExecuteScalarAsync(ct);
        return (long)result!;
    }

    private static async Task MarcarPromovidoAsync(
        SqlConnection connection, SqlTransaction transaction, long inboxEventId, long facturaId, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE fact.InboxEvent
            SET EstadoConsumo = 'PROMOVIDO', FacturaId = @facturaId, ConsumidoEn = SYSUTCDATETIME()
            WHERE InboxEventId = @inboxEventId;
            """;
        command.Parameters.AddWithValue("@facturaId", facturaId);
        command.Parameters.AddWithValue("@inboxEventId", inboxEventId);
        await command.ExecuteNonQueryAsync(ct);
    }
}
