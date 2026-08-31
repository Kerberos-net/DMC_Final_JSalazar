using Microsoft.Data.SqlClient;
using SmartNet.Facturacion.Core.RegistroCompra;

namespace SmartNet.Facturacion.Infrastructure.RegistroCompra;

/// <summary>
/// spec registro-compra-api req 1/2/4 — the SQL adapter for <see cref="IRegistroCompraRepository"/>.
/// ADO puro, connection-per-call, one <see langword="readonly"/> connection-string field (so the
/// registration is a safe <c>AddSingleton</c>). Same shape as <c>SqlBandejaRepository</c> /
/// <c>SqlProveedorRepository</c>.
///
/// Only <c>SELECT</c>, only under the existing <c>008</c> <c>fact_api</c> grants on
/// <c>fact.AsientoContable</c>, <c>fact.AsientoContableDetalle</c>, <c>fact.Factura</c> and
/// <c>dbo.Proveedor</c> — no <c>dbo.*</c> write, no new GRANT, no versioned SQL (ADR 0003).
/// Every filter is a <see cref="SqlParameter"/>; there is no user-chosen sort, so — unlike
/// <c>SqlBandejaRepository</c> — nothing is interpolated into the command text.
/// </summary>
public sealed class SqlRegistroCompraRepository : IRegistroCompraRepository
{
    private readonly string _connectionString;

    public SqlRegistroCompraRepository(string connectionString) => _connectionString = connectionString;

    // The 12 cabecera columns, in the order MapCabecera reads them. `a`=fact.AsientoContable,
    // `f`=fact.Factura, `pr`=dbo.Proveedor. OrigenLibro is echoed VERBATIM from the column
    // (never the "02" default constant).
    private const string ColumnasCabecera =
        "a.AsientoContableId, a.NumeroComprobante, a.NumeroAsiento, a.OrigenLibro, " +
        "a.ProveedorCodigo, pr.proveedor AS ProveedorNombre, a.Glosa, a.FechaContable, " +
        "a.TipoCambioVenta, a.BasePEN, a.IgvPEN, a.NetoPEN";

    // The row predicate shared by listing, export and detail. UQ_Asiento_Vigente guarantees at most
    // one non-ANULADO asiento per factura, so the JOIN to fact.Factura is 1:1 — no dedup needed.
    private const string DesdeYPredicado =
        """
        FROM fact.AsientoContable a
        JOIN fact.Factura f        ON f.FacturaId = a.FacturaId
        LEFT JOIN dbo.Proveedor pr ON pr.codpro = a.ProveedorCodigo
        WHERE f.Estado = 'VALIDADA' AND a.Estado <> 'ANULADO'
        """;

    public async Task<PaginaRegistroCompra<RegistroCompraCabecera>> ListarPeriodoAsync(
        PeriodoContable periodo, int pagina, int tamanioPagina, CancellationToken ct)
    {
        var paginaNormalizada = pagina < 1 ? 1 : pagina;
        var tamanioNormalizado = tamanioPagina < 1 ? 1 : tamanioPagina;
        var salto = (paginaNormalizada - 1) * tamanioNormalizado;
        var (desde, hasta) = RangoMedioAbierto(periodo);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();

        // `CAST(COUNT(*) OVER() AS INT)` yields the full filtered count in the same scan that pages
        // (OFFSET/FETCH applies logically after window functions). ORDER BY ends in AsientoContableId
        // because NumeroAsiento is nullable — without a unique tiebreak OFFSET/FETCH can repeat or
        // skip rows across pages. No fallback COUNT(*): the SPA never asks for a page past totalPaginas.
        command.CommandText =
            $"""
             SELECT CAST(COUNT(*) OVER() AS INT) AS TotalRegistros, {ColumnasCabecera}
             {DesdeYPredicado}
               AND a.FechaContable >= @desde AND a.FechaContable < @hasta
             ORDER BY a.FechaContable, a.NumeroAsiento, a.AsientoContableId
             OFFSET @salto ROWS FETCH NEXT @tamanio ROWS ONLY;
             """;

        command.Parameters.Add(ParametroFecha("@desde", desde));
        command.Parameters.Add(ParametroFecha("@hasta", hasta));
        command.Parameters.AddWithValue("@salto", salto);
        command.Parameters.AddWithValue("@tamanio", tamanioNormalizado);

        var filas = new List<RegistroCompraCabecera>();
        var totalRegistros = 0;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            totalRegistros = reader.GetInt32(0);
            filas.Add(MapCabecera(reader, desplazamiento: 1));
        }

        var totalPaginas = totalRegistros == 0
            ? 0
            : (int)Math.Ceiling(totalRegistros / (double)tamanioNormalizado);

        return new PaginaRegistroCompra<RegistroCompraCabecera>(
            filas, paginaNormalizada, tamanioNormalizado, totalRegistros, totalPaginas);
    }

    public async Task<IReadOnlyList<RegistroCompraCabecera>> ListarPeriodoCompletoAsync(
        PeriodoContable periodo, CancellationToken ct)
    {
        var (desde, hasta) = RangoMedioAbierto(periodo);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             SELECT {ColumnasCabecera}
             {DesdeYPredicado}
               AND a.FechaContable >= @desde AND a.FechaContable < @hasta
             ORDER BY a.FechaContable, a.NumeroAsiento, a.AsientoContableId;
             """;
        command.Parameters.Add(ParametroFecha("@desde", desde));
        command.Parameters.Add(ParametroFecha("@hasta", hasta));

        var filas = new List<RegistroCompraCabecera>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            filas.Add(MapCabecera(reader, desplazamiento: 0));
        }

        return filas;
    }

    public async Task<RegistroCompraDetalle?> ObtenerAsync(long asientoId, CancellationToken ct)
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();

        // design D3: the cabecera query RE-APPLIES the same VALIDADA / non-ANULADO predicate. An
        // asiento that is filtered out is indistinguishable from a nonexistent one — both come back
        // with zero cabecera rows and become a 404. The detail route cannot be used as a side
        // channel to read an ANULADO / non-VALIDADA asiento.
        command.CommandText =
            $"""
             SELECT {ColumnasCabecera}
             {DesdeYPredicado}
               AND a.AsientoContableId = @id;

             SELECT d.Orden, d.Bloque, d.Tipo, d.Debe, d.Haber, d.CuentaCodigo, d.CuentaDescripcion
             FROM fact.AsientoContableDetalle d
             WHERE d.AsientoContableId = @id
             ORDER BY d.Orden;
             """;
        command.Parameters.AddWithValue("@id", asientoId);

        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        var cabecera = MapCabecera(reader, desplazamiento: 0);

        var lineas = new List<LineaRegistro>();
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            lineas.Add(new LineaRegistro(
                Orden: reader.GetInt16(0),
                Bloque: reader.GetString(1),
                Tipo: reader.GetString(2),
                Debe: reader.GetDecimal(3),
                Haber: reader.GetDecimal(4),
                CuentaCodigo: reader.IsDBNull(5) ? null : reader.GetString(5),
                CuentaDescripcion: reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return new RegistroCompraDetalle(cabecera, lineas);
    }

    // design D2: the half-open [primerDia, primerDiaMesSiguiente) range is derived HERE, in the
    // adapter — the Core value only parses. DateTime arithmetic in infra is fine (no PurityScan).
    private static (DateOnly Desde, DateOnly Hasta) RangoMedioAbierto(PeriodoContable periodo)
    {
        var desde = new DateOnly(periodo.Anio, periodo.Mes, 1);
        return (desde, desde.AddMonths(1));
    }

    private static SqlParameter ParametroFecha(string nombre, DateOnly valor) =>
        new(nombre, System.Data.SqlDbType.Date) { Value = valor.ToDateTime(TimeOnly.MinValue) };

    private static RegistroCompraCabecera MapCabecera(SqlDataReader reader, int desplazamiento)
    {
        var o = desplazamiento;
        return new RegistroCompraCabecera(
            AsientoContableId: reader.GetInt64(o + 0),
            NumeroComprobante: reader.IsDBNull(o + 1) ? null : reader.GetString(o + 1),
            NumeroAsiento: reader.IsDBNull(o + 2) ? null : reader.GetString(o + 2),
            OrigenLibro: reader.GetString(o + 3).TrimEnd(),
            ProveedorCodigo: reader.GetString(o + 4).TrimEnd(),
            ProveedorNombre: reader.IsDBNull(o + 5) ? null : reader.GetString(o + 5),
            Glosa: reader.IsDBNull(o + 6) ? null : reader.GetString(o + 6),
            FechaContable: DateOnly.FromDateTime(reader.GetDateTime(o + 7)),
            TipoCambioVenta: reader.IsDBNull(o + 8) ? null : reader.GetDecimal(o + 8),
            BasePEN: reader.IsDBNull(o + 9) ? null : reader.GetDecimal(o + 9),
            IgvPEN: reader.IsDBNull(o + 10) ? null : reader.GetDecimal(o + 10),
            NetoPEN: reader.IsDBNull(o + 11) ? null : reader.GetDecimal(o + 11));
    }
}
