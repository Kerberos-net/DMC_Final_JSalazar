using System.Data;
using Microsoft.Data.SqlClient;
using SmartNet.Inbox.Core;

namespace SmartNet.Inbox.Infrastructure;

/// <summary>
/// SQL adapter backing <c>GET /api/bandeja?estado=&amp;desde=&amp;hasta=&amp;proveedor=&amp;pagina=&amp;orden=</c>
/// (BACKLOG #13, design.md D2-D5, D7) for <see cref="IBandejaRepository"/>. <c>BandejaEndpoints.cs</c>
/// (Phase 4) is a thin delegator over this repository -- never a second query surface.
///
/// One <see cref="SqlCommand"/> batch, three statements:
/// 1. <c>INSERT @pagina</c> -- the filtered/ordered/paged key set (design D4: <c>OFFSET/FETCH</c> with
///    an <c>InboxEventId</c> tiebreaker, <c>COUNT(*) OVER()</c> captured per row before paging).
/// 2. Result set 1 -- one row per key in <c>@pagina</c>, joined back to
///    <c>fact.InboxEvent</c>/<c>fact.Factura</c>, plus the <see cref="ErrorProcesamiento"/>-window-derived
///    <c>ReprocesarDisponibleEn</c> (design D5, from <c>fact.CommandQueue</c>).
/// 3. Result set 2 -- the error rows for exactly those keys, from <c>fact.ProcesamientoError</c>
///    (ADR 0003 revision 6, asymmetric-read grant), keyed by <c>ProcesamientoId</c> (design D3: a
///    second result set, never a <c>LEFT JOIN</c>, so it cannot multiply/break the paging).
/// A fourth, conditional statement (an <c>IF</c>, not always a result set) runs the design D4
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
             ORDER BY ie.CreadoEn {direction}, ie.InboxEventId {direction};

             SELECT pe.ProcesamientoId, pe.ProcesamientoErrorId, pe.Integracion, pe.Mensaje, pe.Clasificacion, pe.OcurridoEn
             FROM fact.ProcesamientoError pe
             JOIN @pagina p ON p.ProcesamientoId = pe.ProcesamientoId
             ORDER BY pe.OcurridoEn DESC;

             IF NOT EXISTS (SELECT 1 FROM @pagina) AND @nroPagina > 1
             BEGIN
                 SELECT COUNT(*) AS TotalRegistros
                 FROM fact.InboxEvent ie
                 LEFT JOIN fact.Factura f ON f.FacturaId = ie.FacturaId
                 WHERE {FiltroWhere};
             END
             """;

        command.Parameters.AddWithValue("@estado", (object?)filtros.Estado ?? DBNull.Value);
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
            totalRegistrosDesdePagina = reader.GetInt32(14);
            filas.Add(new FilaCruda(
                InboxEventId: reader.GetInt64(0),
                EstadoConsumo: reader.GetString(1),
                ProcesamientoId: reader.GetInt64(2),
                CreadoEn: reader.GetDateTime(3),
                FacturaId: reader.IsDBNull(4) ? null : reader.GetInt64(4),
                MotivoDescarte: reader.IsDBNull(5) ? null : reader.GetString(5),
                ProveedorCodigo: reader.IsDBNull(6) ? null : reader.GetString(6).TrimEnd(),
                RucProveedor: reader.IsDBNull(7) ? null : reader.GetString(7),
                Indicadores: reader.IsDBNull(8)
                    ? null
                    : new IndicadoresFactura(
                        EsProveedorGenerico: reader.GetBoolean(8),
                        PosibleDuplicado: reader.GetBoolean(9),
                        TieneCamposNoExtraidos: reader.GetBoolean(10),
                        FechaEnDomingo: reader.GetBoolean(11),
                        AfectacionMixta: reader.IsDBNull(12) ? null : reader.GetBoolean(12)),
                ReprocesarDisponibleEn: reader.IsDBNull(13) ? null : reader.GetDateTime(13)));
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
                Indicadores: fila.Indicadores,
                MotivoDescarte: fila.MotivoDescarte,
                Errores: erroresPorProcesamiento.TryGetValue(fila.ProcesamientoId, out var errores)
                    ? errores
                    : Array.Empty<ErrorProcesamiento>(),
                ReprocesarDisponibleEn: fila.ReprocesarDisponibleEn))
            .ToList();

        var totalPaginas = EnvelopeBandeja.CalcularTotalPaginas(totalRegistros, filtros.TamanioPagina);

        return new PaginaBandeja<BandejaItem>(items, filtros.Pagina, filtros.TamanioPagina, totalRegistros, totalPaginas);
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
    private const string FiltroWhere =
        """
        (
            (@estado IS NOT NULL AND ie.EstadoConsumo = @estado)
            OR (@estado IS NULL AND (
                ie.EstadoConsumo = 'PENDIENTE'
                OR EXISTS (
                    SELECT 1 FROM fact.ProcesamientoError pe
                    WHERE pe.ProcesamientoId = ie.ProcesamientoId AND pe.Clasificacion <> 'OBSOLETO'
                )
            ))
        )
        AND (@desde IS NULL OR ie.CreadoEn >= @desde)
        AND (@hasta IS NULL OR ie.CreadoEn < DATEADD(DAY, 1, @hasta))
        AND (
            @proveedor IS NULL
            OR f.RucProveedor = @proveedor
            OR f.ProveedorCodigo = @proveedor
            OR JSON_VALUE(ie.Payload, '$.comprobante.rucProveedor') = @proveedor
        )
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
        IndicadoresFactura? Indicadores,
        DateTime? ReprocesarDisponibleEn);
}
