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
        string fechaEmision = "2026-08-10")
    {
        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.Factura
                 (ProveedorCodigo, RucProveedor, TipoComprobante, Numero, TotalOrig, Moneda, FechaEmision, Afectacion, Estado)
             VALUES
                 ('P00123', {(rucProveedor is null ? "NULL" : $"'{rucProveedor}'")}, '{tipoComprobante}',
                  {(numero is null ? "NULL" : $"'{numero}'")}, 118.00, '{moneda}', '{fechaEmision}', 'GRAVADA', '{estado}');
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
}
