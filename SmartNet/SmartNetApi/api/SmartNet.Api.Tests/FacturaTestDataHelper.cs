using System.Globalization;
using SmartNet.Db.TestBootstrap;

namespace SmartNet.Api.Tests;

/// <summary>
/// Local copy of the fixture-insert pattern already used by
/// <c>SmartNet.Facturacion.Infrastructure.Tests.FacturacionTestDatabaseFixtureHelper</c> (internal
/// to that assembly, so it cannot be reused here) — mirrors
/// <c>BandejaTestDataHelper</c>'s own precedent of duplicating a fixture helper across test
/// assemblies rather than exposing test-only internals publicly.
/// </summary>
internal static class FacturaTestDataHelper
{
    public static async Task<long> InsertarFacturaAsync(
        this TestDatabaseFixture db,
        string tipoComprobante = "01",
        string? numero = "F001-1",
        string? rucProveedor = "20100000001",
        string estado = "PENDIENTE_VALIDACION",
        string moneda = "PEN",
        string fechaEmision = "2026-08-10",
        string afectacion = "GRAVADA",
        decimal totalOrig = 118.00m,
        decimal? igvOrig = 18.00m)
    {
        // BACKLOG #24: a GRAVADA factura MUST carry a positive IgvOrig — the seed engine emits a
        // 401111 IGV line and CK_Linea_Tipo rejects a zero-amount 'D' line. A boleta / no-gravada
        // factura passes igvOrig: null.
        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.Factura
                 (ProveedorCodigo, RucProveedor, TipoComprobante, Numero, TotalOrig, IgvOrig, Moneda, FechaEmision, Afectacion, Estado)
             VALUES
                 ('P00123', {(rucProveedor is null ? "NULL" : $"'{rucProveedor}'")}, '{tipoComprobante}',
                  {(numero is null ? "NULL" : $"'{numero}'")}, {totalOrig.ToString(CultureInfo.InvariantCulture)},
                  {(igvOrig is null ? "NULL" : igvOrig.Value.ToString(CultureInfo.InvariantCulture))},
                  '{moneda}', '{fechaEmision}', '{afectacion}', '{estado}');
             """);
        return await db.ExecuteScalarAsync<long>("SELECT MAX(FacturaId) FROM fact.Factura;");
    }

    /// <summary>PR 5 (Phase 5) — inserts a MANUAL <c>fact.TipoCambio</c> row for the given date, the
    /// minimum <c>ITipoCambioRepository</c> needs for <c>ResultadoTipoCambio.Vigente</c>.</summary>
    public static Task InsertarTipoCambioAsync(
        this TestDatabaseFixture db, string fecha = "2026-08-10", decimal compra = 3.70m, decimal venta = 3.75m) =>
        db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.TipoCambio (Fecha, Origen, Compra, Venta, FechaConsulta)
             VALUES ('{fecha}', 'MANUAL', {compra.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                     {venta.ToString(System.Globalization.CultureInfo.InvariantCulture)}, SYSUTCDATETIME());
             """);

    public static Task<byte[]> ObtenerVersionFacturaAsync(this TestDatabaseFixture db, long facturaId) =>
        db.ExecuteScalarAsync<byte[]>($"SELECT Version FROM fact.Factura WHERE FacturaId = {facturaId};")!;

    /// <summary>diseno-visual-spa-item-12 (design D9) — fija las 4 columnas indicadoras directamente
    /// (mismas que <c>SqlBandejaRepository.ListarAsync</c> ya lee), sin pasar por la promoción del
    /// inbox: <c>null</c> deja la columna sin tocar (para <c>AfectacionMixta</c>, cuyo valor por
    /// defecto ya es <c>NULL</c>).</summary>
    public static Task FijarIndicadoresFacturaAsync(
        this TestDatabaseFixture db, long facturaId, bool esProveedorGenerico = false,
        bool posibleDuplicado = false, bool tieneCamposNoExtraidos = false, bool? afectacionMixta = null) =>
        db.ExecuteNonQueryAsync(
            $"""
             UPDATE fact.Factura
             SET EsProveedorGenerico = {(esProveedorGenerico ? 1 : 0)},
                 PosibleDuplicado = {(posibleDuplicado ? 1 : 0)},
                 TieneCamposNoExtraidos = {(tieneCamposNoExtraidos ? 1 : 0)},
                 AfectacionMixta = {(afectacionMixta is null ? "NULL" : afectacionMixta.Value ? "1" : "0")}
             WHERE FacturaId = {facturaId};
             """);

    /// <summary>Un asiento BORRADOR balanceado (misma forma que <c>AsientoValido()</c> de
    /// <c>ServicioDeFacturasTests</c>, PR 1) que <c>InvariantesDeConfirmacion.Evaluar</c> aprueba
    /// enteramente — necesario para un test de <c>validar</c> feliz-camino de extremo a extremo.</summary>
    public static async Task<long> InsertarAsientoBorradorBalanceadoAsync(this TestDatabaseFixture db, long facturaId)
    {
        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.AsientoContable
                 (FacturaId, OrigenLibro, ProveedorCodigo, FechaContable, BasePEN, IgvPEN, NetoPEN, Estado)
             VALUES
                 ({facturaId}, '02', 'P00123', '2026-08-10', 100.00, 18.00, 118.00, 'BORRADOR');
             """);
        var asientoId = await db.ExecuteScalarAsync<long>("SELECT MAX(AsientoContableId) FROM fact.AsientoContable;");

        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.AsientoContableDetalle (AsientoContableId, Orden, Bloque, Tipo, Debe, Haber, CuentaCodigo)
             VALUES
                 ({asientoId}, 1, 'PRINCIPAL', 'D', 100.00, 0, '639915'),
                 ({asientoId}, 2, 'PRINCIPAL', 'D', 18.00, 0, '401111'),
                 ({asientoId}, 3, 'PRINCIPAL', 'H', 0, 118.00, '421001');
             """);

        return asientoId;
    }

    public static Task<byte[]> ObtenerVersionAsientoAsync(this TestDatabaseFixture db, long asientoId) =>
        db.ExecuteScalarAsync<byte[]>($"SELECT Version FROM fact.AsientoContable WHERE AsientoContableId = {asientoId};")!;

    /// <summary>
    /// BACKLOG #23 (registro-compra-api) — inserts an <c>fact.AsientoContable</c> row in a chosen
    /// <paramref name="estado"/> with caller-controlled <c>FechaContable</c>, <c>OrigenLibro</c>,
    /// <c>NumeroAsiento</c> and the three PEN amounts, plus optional detail lines. RAW SQL bypasses
    /// the domain, so an INCONSISTENT asiento (<c>base + igv &lt;&gt; neto</c>, or debe &lt;&gt;
    /// haber) IS persistable — that is deliberate: the contract tests prove the API echoes the
    /// stored amounts verbatim without "fixing" them (the inconsistency badge is a pure SPA concern,
    /// design D6).
    /// </summary>
    public static async Task<long> InsertarAsientoConfirmadoAsync(
        this TestDatabaseFixture db,
        long facturaId,
        string fechaContable = "2026-08-10",
        string estado = "CONFIRMADO",
        string origenLibro = "02",
        string? numeroAsiento = "02-2026-08-000001",
        string? numeroComprobante = "F001-1",
        decimal? basePEN = 100.00m,
        decimal? igvPEN = 18.00m,
        decimal? netoPEN = 118.00m,
        string proveedorCodigo = "P00123",
        string? glosa = "Compra de prueba",
        decimal? tipoCambioVenta = null,
        (short Orden, string Bloque, string Tipo, decimal Debe, decimal Haber, string? Cuenta, string? CuentaDescripcion)[]? lineas = null)
    {
        static string Money(decimal? v) =>
            v is null ? "NULL" : v.Value.ToString(CultureInfo.InvariantCulture);
        static string Texto(string? v) => v is null ? "NULL" : $"N'{v.Replace("'", "''")}'";

        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.AsientoContable
                 (FacturaId, NumeroComprobante, NumeroAsiento, OrigenLibro, ProveedorCodigo, Glosa,
                  FechaContable, TipoCambioVenta, BasePEN, IgvPEN, NetoPEN, Estado)
             VALUES
                 ({facturaId}, {Texto(numeroComprobante)}, {Texto(numeroAsiento)}, '{origenLibro}',
                  '{proveedorCodigo}', {Texto(glosa)}, '{fechaContable}', {Money(tipoCambioVenta)},
                  {Money(basePEN)}, {Money(igvPEN)}, {Money(netoPEN)}, '{estado}');
             """);
        var asientoId = await db.ExecuteScalarAsync<long>("SELECT MAX(AsientoContableId) FROM fact.AsientoContable;");

        if (lineas is { Length: > 0 })
        {
            var valores = string.Join(",\n", lineas.Select(l =>
                $"({asientoId}, {l.Orden}, '{l.Bloque}', '{l.Tipo}', {Money(l.Debe)}, {Money(l.Haber)}, {Texto(l.Cuenta)}, {Texto(l.CuentaDescripcion)})"));
            await db.ExecuteNonQueryAsync(
                "INSERT INTO fact.AsientoContableDetalle " +
                "(AsientoContableId, Orden, Bloque, Tipo, Debe, Haber, CuentaCodigo, CuentaDescripcion) VALUES\n" +
                valores + ";");
        }

        return asientoId;
    }
}
