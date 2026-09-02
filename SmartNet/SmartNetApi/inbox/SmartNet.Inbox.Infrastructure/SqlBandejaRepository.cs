using System.Data;
using Microsoft.Data.SqlClient;
using SmartNet.Inbox.Core;

namespace SmartNet.Inbox.Infrastructure;

/// <summary>
/// SQL adapter backing <c>GET /api/bandeja?estado=&amp;desde=&amp;hasta=&amp;proveedor=&amp;pagina=&amp;orden=</c>
/// (BACKLOG #13, design.md D2-D5, D7) for <see cref="IBandejaRepository"/>. <c>BandejaEndpoints.cs</c>
/// (Phase 4) is a thin delegator over this repository -- never a second query surface.
///
/// One <see cref="SqlCommand"/> batch:
/// 1. <c>INSERT @pagina</c> -- the filtered/ordered/paged key set (design D4: <c>OFFSET/FETCH</c> with
///    an <c>InboxEventId</c> tiebreaker, <c>COUNT(*) OVER()</c> captured per row before paging).
/// 2. Result set 1 -- one row per key in <c>@pagina</c>, joined back to
///    <c>fact.InboxEvent</c>/<c>fact.Factura</c> plus <c>dbo.Proveedor</c> for the display name
///    (BACKLOG #21, design D3: the join is on the page projection only, never on
///    <c>FiltroWhere</c>), plus the <see cref="ErrorProcesamiento"/>-window-derived
///    <c>ReprocesarDisponibleEn</c> (design D5, from <c>fact.CommandQueue</c>).
/// 3. Result set 2 -- the error rows for exactly those keys, from <c>fact.ProcesamientoError</c>
///    (ADR 0003 revision 6, asymmetric-read grant), keyed by <c>ProcesamientoId</c> (design D3: a
///    second result set, never a <c>LEFT JOIN</c>, so it cannot multiply/break the paging).
/// 4. Result set 3 -- the global estado aggregate feeding the dashboard cards (BACKLOG #21,
///    design D2): one row, computed over every <c>fact.InboxEvent</c> row EXCEPT a
///    <see cref="PromocionSecundaria"/> one (a #25 associated-doc merge that reuses another event's
///    factura -- never a distinct bandeja item), so the counts stay independent of the request's
///    filter and paging parameters while never double-counting a factura. Its <c>ERROR</c>
///    bucket uses an unfiltered <c>EXISTS</c> on <c>fact.ProcesamientoError</c> to match the row
///    Estado chip exactly (design D2b -- the <c>OBSOLETO</c> asymmetry: <c>FiltroWhere</c> filters
///    <c>&lt;&gt; 'OBSOLETO'</c> but the chip does not; a future change to one must move both).
/// A final, conditional statement (an <c>IF</c>, not always a result set) runs the design D4
/// fallback <c>COUNT(*)</c> only when the page came back empty and <c>pagina &gt; 1</c>, so the
/// envelope's <c>totalRegistros</c> is never a lie for an out-of-range page.
/// </summary>
public sealed class SqlBandejaRepository : IBandejaRepository
{
    private readonly string _connectionString;

    public SqlBandejaRepository(string connectionString) => _connectionString = connectionString;

    public async Task<PaginaBandeja<BandejaItem>> ListarAsync(FiltrosBandeja filtros, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        return await ListarConConexionAsync(connection, filtros, ct);
    }

    /// <summary>
    /// Runs the whole batch over an ALREADY OPEN <paramref name="connection"/> -- the seam task 3.7
    /// uses to prove the query succeeds impersonating <c>usr_api</c> via
    /// <c>TestDatabaseFixture.ExecuteAsUserAsync</c> (that helper needs a connection it already
    /// opened and impersonated on, not a fresh one from a plain connection string).
    /// </summary>
    internal static async Task<PaginaBandeja<BandejaItem>> ListarConConexionAsync(
        SqlConnection connection, FiltrosBandeja filtros, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();

        // `orden` only chooses ASC/DESC on two fixed columns -- never string-concatenated as an
        // identifier, so there is no injection surface here despite the interpolation below.
        var direction = string.Equals(filtros.Orden, "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        var offset = (filtros.Pagina - 1) * filtros.TamanioPagina;

        command.CommandText =
            $"""
             DECLARE @pagina TABLE (InboxEventId BIGINT PRIMARY KEY, ProcesamientoId BIGINT NOT NULL, TotalRegistros INT NOT NULL);

             INSERT INTO @pagina (InboxEventId, ProcesamientoId, TotalRegistros)
             SELECT ie.InboxEventId, ie.ProcesamientoId, CAST(COUNT(*) OVER() AS INT)
             FROM fact.InboxEvent ie
             LEFT JOIN fact.Factura f ON f.FacturaId = ie.FacturaId
             WHERE {FiltroWhere}
             ORDER BY ie.CreadoEn {direction}, ie.InboxEventId {direction}
             OFFSET @offset ROWS FETCH NEXT @tamanioPagina ROWS ONLY;

             SELECT ie.InboxEventId, ie.EstadoConsumo, ie.ProcesamientoId, ie.CreadoEn, ie.FacturaId,
                    ie.MotivoDescarte, f.ProveedorCodigo, f.RucProveedor,
                    pr.proveedor AS ProveedorNombre, f.TipoComprobante, f.Numero, f.TotalOrig, f.Moneda, f.FechaEmision,
                    f.EsProveedorGenerico, f.PosibleDuplicado, f.TieneCamposNoExtraidos, f.FechaEnDomingo, f.AfectacionMixta,
                    (
                        SELECT DATEADD(MINUTE, @ventanaMinutos, MAX(cq.CreadoEn))
                        FROM fact.CommandQueue cq
                        WHERE cq.Tipo = 'REPROCESAR_DOCUMENTO' AND cq.Referencia = ie.ProcesamientoId
                          AND cq.Estado IN ('PENDIENTE', 'EN_PROCESO')
                          AND cq.CreadoEn > DATEADD(MINUTE, -@ventanaMinutos, SYSUTCDATETIME())
                    ) AS ReprocesarDisponibleEn,
                    p.TotalRegistros
             FROM @pagina p
             JOIN fact.InboxEvent ie ON ie.InboxEventId = p.InboxEventId
             LEFT JOIN fact.Factura f ON f.FacturaId = ie.FacturaId
             LEFT JOIN dbo.Proveedor pr ON pr.codpro = f.ProveedorCodigo
             ORDER BY ie.CreadoEn {direction}, ie.InboxEventId {direction};

             SELECT pe.ProcesamientoId, pe.ProcesamientoErrorId, pe.Integracion, pe.Mensaje, pe.Clasificacion, pe.OcurridoEn
             FROM fact.ProcesamientoError pe
             JOIN @pagina p ON p.ProcesamientoId = pe.ProcesamientoId
             ORDER BY pe.OcurridoEn DESC;

             SELECT
                 SUM(CASE WHEN b.Bucket = 'PENDIENTE'  THEN 1 ELSE 0 END) AS Pendientes,
                 SUM(CASE WHEN b.Bucket = 'VALIDADA'   THEN 1 ELSE 0 END) AS Validadas,
                 SUM(CASE WHEN b.Bucket = 'ERROR'      THEN 1 ELSE 0 END) AS ConError,
                 SUM(CASE WHEN b.Bucket = 'ALERTA'     THEN 1 ELSE 0 END) AS Alertas,
                 SUM(CASE WHEN b.Bucket = 'DESCARTADA' THEN 1 ELSE 0 END) AS Descartadas,
                 COUNT(*) AS Total
             FROM (
                 SELECT {BucketDerivado} AS Bucket
                 FROM fact.InboxEvent ie
                 LEFT JOIN fact.Factura f ON f.FacturaId = ie.FacturaId
                 WHERE NOT ({PromocionSecundaria})
             ) b;

             IF NOT EXISTS (SELECT 1 FROM @pagina) AND @nroPagina > 1
             BEGIN
                 SELECT COUNT(*) AS TotalRegistros
                 FROM fact.InboxEvent ie
                 LEFT JOIN fact.Factura f ON f.FacturaId = ie.FacturaId
                 WHERE {FiltroWhere};
             END
             """;

        command.Parameters.AddWithValue("@estado", (object?)filtros.Estado ?? DBNull.Value);
        command.Parameters.AddWithValue("@estadoDerivado", (object?)filtros.EstadoDerivado ?? DBNull.Value);
        AgregarParametroFecha(command, "@desde", filtros.Desde);
        AgregarParametroFecha(command, "@hasta", filtros.Hasta);
        command.Parameters.AddWithValue("@proveedor", (object?)filtros.Proveedor ?? DBNull.Value);
        command.Parameters.AddWithValue("@offset", offset);
        command.Parameters.AddWithValue("@tamanioPagina", filtros.TamanioPagina);
        command.Parameters.AddWithValue("@nroPagina", filtros.Pagina);
        command.Parameters.AddWithValue("@ventanaMinutos", PoliticaDeReprocesamiento.VentanaMinutos);

        var filas = new List<FilaCruda>();
        var totalRegistrosDesdePagina = 0;

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            totalRegistrosDesdePagina = reader.GetInt32(20);
            filas.Add(new FilaCruda(
                InboxEventId: reader.GetInt64(0),
                EstadoConsumo: reader.GetString(1),
                ProcesamientoId: reader.GetInt64(2),
                CreadoEn: reader.GetDateTime(3),
                FacturaId: reader.IsDBNull(4) ? null : reader.GetInt64(4),
                MotivoDescarte: reader.IsDBNull(5) ? null : reader.GetString(5),
                ProveedorCodigo: reader.IsDBNull(6) ? null : reader.GetString(6).TrimEnd(),
                RucProveedor: reader.IsDBNull(7) ? null : reader.GetString(7),
                ProveedorNombre: reader.IsDBNull(8) ? null : reader.GetString(8),
                TipoComprobante: reader.IsDBNull(9) ? null : reader.GetString(9).TrimEnd(),
                Numero: reader.IsDBNull(10) ? null : reader.GetString(10),
                TotalOrig: reader.IsDBNull(11) ? null : reader.GetDecimal(11),
                Moneda: reader.IsDBNull(12) ? null : reader.GetString(12).TrimEnd(),
                FechaEmision: reader.IsDBNull(13) ? null : DateOnly.FromDateTime(reader.GetDateTime(13)),
                Indicadores: reader.IsDBNull(14)
                    ? null
                    : new IndicadoresFactura(
                        EsProveedorGenerico: reader.GetBoolean(14),
                        PosibleDuplicado: reader.GetBoolean(15),
                        TieneCamposNoExtraidos: reader.GetBoolean(16),
                        FechaEnDomingo: reader.GetBoolean(17),
                        AfectacionMixta: reader.IsDBNull(18) ? null : reader.GetBoolean(18),
                        // BACKLOG #19: the bandeja projection carries only the coarse boolean badge;
                        // the per-field OCR list is a detalle-screen concern read straight from
                        // fact.Factura.CamposNoExtraidos by FacturaRespuesta, not reconstructed here.
                        CamposNoExtraidos: Array.Empty<string>()),
                ReprocesarDisponibleEn: reader.IsDBNull(19) ? null : reader.GetDateTime(19)));
        }

        await reader.NextResultAsync(ct);
        var erroresPorProcesamiento = new Dictionary<long, List<ErrorProcesamiento>>();
        while (await reader.ReadAsync(ct))
        {
            var procesamientoId = reader.GetInt64(0);
            if (!erroresPorProcesamiento.TryGetValue(procesamientoId, out var lista))
            {
                lista = new List<ErrorProcesamiento>();
                erroresPorProcesamiento[procesamientoId] = lista;
            }

            lista.Add(new ErrorProcesamiento(
                ProcesamientoErrorId: reader.GetInt64(1),
                Integracion: reader.GetString(2),
                Mensaje: reader.GetString(3),
                Clasificacion: reader.GetString(4),
                OcurridoEn: reader.GetDateTime(5)));
        }

        // Result set 3 -- the global estado aggregate (BACKLOG #21, design D2). Always exactly one
        // row; unaffected by `@estado`/`@desde`/`@hasta`/`@proveedor`/paging (it has no WHERE).
        await reader.NextResultAsync(ct);
        var resumen = await reader.ReadAsync(ct)
            ? new ResumenBandeja(
                Pendientes: reader.GetInt32(0),
                Validadas: reader.GetInt32(1),
                ConError: reader.GetInt32(2),
                Alertas: reader.GetInt32(3),
                Descartadas: reader.GetInt32(4),
                Total: reader.GetInt32(5))
            : new ResumenBandeja(0, 0, 0, 0, 0, 0);

        var totalRegistros = totalRegistrosDesdePagina;
        if (filas.Count == 0)
        {
            var huboFallback = await reader.NextResultAsync(ct);
            totalRegistros = huboFallback && await reader.ReadAsync(ct) ? reader.GetInt32(0) : 0;
        }

        var items = filas
            .Select(fila => new BandejaItem(
                InboxEventId: fila.InboxEventId,
                Origen: OrigenBandeja.Derivar(fila.EstadoConsumo, fila.FacturaId),
                ProcesamientoId: fila.ProcesamientoId,
                EstadoConsumo: fila.EstadoConsumo,
                CreadoEn: fila.CreadoEn,
                FacturaId: fila.FacturaId,
                ProveedorCodigo: fila.ProveedorCodigo,
                RucProveedor: fila.RucProveedor,
                ProveedorNombre: fila.ProveedorNombre,
                TipoComprobante: fila.TipoComprobante,
                Numero: fila.Numero,
                TotalOrig: fila.TotalOrig,
                Moneda: fila.Moneda,
                FechaEmision: fila.FechaEmision,
                Indicadores: fila.Indicadores,
                MotivoDescarte: fila.MotivoDescarte,
                Errores: erroresPorProcesamiento.TryGetValue(fila.ProcesamientoId, out var errores)
                    ? errores
                    : Array.Empty<ErrorProcesamiento>(),
                ReprocesarDisponibleEn: fila.ReprocesarDisponibleEn))
            .ToList();

        var totalPaginas = EnvelopeBandeja.CalcularTotalPaginas(totalRegistros, filtros.TamanioPagina);

        return new PaginaBandeja<BandejaItem>(
            items, filtros.Pagina, filtros.TamanioPagina, totalRegistros, totalPaginas, resumen);
    }

    /// <summary>
    /// Shared by the paging <c>INSERT</c> and the design D4 fallback <c>COUNT(*)</c> -- both must
    /// agree on exactly which rows match, or the fallback total could disagree with the page that
    /// was actually built. `estado` empty = design.md's default-view predicate (`OrigenBandeja.EsVistaPorDefecto`,
    /// evaluated here in SQL over the same two facts: `EstadoConsumo` and non-`OBSOLETO` error
    /// existence -- the pure predicate in Core documents this same rule for unit coverage).
    /// `hasta` is inclusive of the whole day (`CreadoEn` is `DATETIME2`, `@hasta` a `DATE`).
    /// `proveedor` matches identity (`RucProveedor`/`ProveedorCodigo`) or falls back to the
    /// `InboxEvent.Payload` JSON for rows not yet promoted (design D7 -- `DatosExtraidos` stays
    /// DENY'd).
    /// </summary>
    /// <summary>
    /// The derived-estado bucket — first match wins, byte-identical to the resumen aggregate's CASE
    /// (design D2b: chip, card and <c>estadoDerivado</c> filter MUST agree). References <c>ie</c>/
    /// <c>f</c>, so it is only valid where both are in scope (the paging INSERT, the D4 fallback
    /// COUNT, and result set 3 — all three <c>FROM fact.InboxEvent ie LEFT JOIN fact.Factura f</c>).
    /// </summary>
    private const string BucketDerivado =
        """
        CASE
            WHEN ie.EstadoConsumo = 'DESCARTADO' THEN 'DESCARTADA'
            WHEN EXISTS (
                SELECT 1 FROM fact.ProcesamientoError pe WHERE pe.ProcesamientoId = ie.ProcesamientoId
            ) THEN 'ERROR'
            WHEN f.FacturaId IS NOT NULL AND (f.EsProveedorGenerico = 1 OR f.PosibleDuplicado = 1) THEN 'ALERTA'
            WHEN ie.EstadoConsumo = 'PROMOVIDO' THEN 'VALIDADA'
            ELSE 'PENDIENTE'
        END
        """;

    /// <summary>
    /// A PROMOVIDO InboxEvent that points at a factura it did NOT create (<c>ie.ProcesamientoId
    /// &lt;&gt; f.ProcesamientoId</c>) is a *secondary* promotion — today only the BACKLOG #25
    /// associated-PDF merge, which reuses the XML's factura instead of creating a second one. It
    /// must never surface as its own bandeja row or resumen count: the factura is already
    /// represented by the InboxEvent whose ProcesamientoId created it (<c>f.ProcesamientoId</c> is
    /// NOT NULL and unique per factura via <c>UQ_Factura_Procesamiento</c>, so exactly one row
    /// survives). References <c>ie</c>/<c>f</c> — only valid where both are in scope.
    /// </summary>
    private const string PromocionSecundaria =
        "ie.EstadoConsumo = 'PROMOVIDO' AND ie.FacturaId IS NOT NULL AND ie.ProcesamientoId <> f.ProcesamientoId";

    /// <summary>
    /// `estado` (raw EstadoConsumo, #13) and `estadoDerivado` (bucket, #21 follow-up) are mutually
    /// exclusive — the endpoint 400s on both. When both are null the design.md default-view
    /// predicate applies (`OrigenBandeja.EsVistaPorDefecto`). `estadoDerivado='TODOS'` widens to
    /// every eligible row; any other value keeps exactly the rows whose <see cref="BucketDerivado"/>
    /// equals it, so the filtered `totalRegistros` matches that bucket's `resumen` count.
    /// `hasta` is inclusive of the whole day. `proveedor` matches identity
    /// (`RucProveedor`/`ProveedorCodigo`) or the `InboxEvent.Payload` JSON for not-yet-promoted rows.
    /// </summary>
    private static readonly string FiltroWhere =
        $$"""
        (
            (@estado IS NOT NULL AND ie.EstadoConsumo = @estado)
            OR (@estado IS NULL AND @estadoDerivado IS NULL AND (
                ie.EstadoConsumo = 'PENDIENTE'
                OR EXISTS (
                    SELECT 1 FROM fact.ProcesamientoError pe
                    WHERE pe.ProcesamientoId = ie.ProcesamientoId AND pe.Clasificacion <> 'OBSOLETO'
                )
            ))
            OR (@estadoDerivado = 'TODOS')
            OR (@estadoDerivado IS NOT NULL AND @estadoDerivado <> 'TODOS' AND ({{BucketDerivado}}) = @estadoDerivado)
        )
        AND (@desde IS NULL OR ie.CreadoEn >= @desde)
        AND (@hasta IS NULL OR ie.CreadoEn < DATEADD(DAY, 1, @hasta))
        AND (
            @proveedor IS NULL
            OR f.RucProveedor = @proveedor
            OR f.ProveedorCodigo = @proveedor
            OR JSON_VALUE(ie.Payload, '$.comprobante.rucProveedor') = @proveedor
        )
        AND NOT ({{PromocionSecundaria}})
        """;

    private static void AgregarParametroFecha(SqlCommand command, string nombre, DateOnly? valor)
    {
        var parametro = command.Parameters.Add(nombre, SqlDbType.Date);
        parametro.Value = valor is null ? DBNull.Value : valor.Value.ToDateTime(TimeOnly.MinValue);
    }

    private sealed record FilaCruda(
        long InboxEventId,
        string EstadoConsumo,
        long ProcesamientoId,
        DateTime CreadoEn,
        long? FacturaId,
        string? MotivoDescarte,
        string? ProveedorCodigo,
        string? RucProveedor,
        string? ProveedorNombre,
        string? TipoComprobante,
        string? Numero,
        decimal? TotalOrig,
        string? Moneda,
        DateOnly? FechaEmision,
        IndicadoresFactura? Indicadores,
        DateTime? ReprocesarDisponibleEn);
}
