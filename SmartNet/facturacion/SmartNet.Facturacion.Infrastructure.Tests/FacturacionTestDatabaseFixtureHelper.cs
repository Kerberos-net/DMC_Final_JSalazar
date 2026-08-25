using SmartNet.Db.TestBootstrap;

namespace SmartNet.Facturacion.Infrastructure.Tests;

/// <summary>
/// tasks.md 1.10/1.11 — arnés compartido para las pruebas de integración de
/// <see cref="SqlUnidadDeTrabajo"/>: una base <c>fact_test_&lt;id&gt;</c> migrada, más los inserts
/// mínimos de <c>fact.Factura</c>/<c>fact.AsientoContable</c>/<c>fact.AsientoContableDetalle</c> que
/// un caso CAS/correlativo necesita (mismo patrón que
/// <c>SmartNet.Inbox.Infrastructure.Tests.InboxTestDatabaseFixtureHelper</c>).
/// </summary>
internal static class FacturacionTestDatabaseFixtureHelper
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

    /// <summary>Inserts one <c>PENDIENTE_VALIDACION</c> <c>fact.Factura</c> row and returns its id.</summary>
    public static async Task<long> InsertarFacturaAsync(
        this TestDatabaseFixture db,
        string tipoComprobante = "01",
        string? numero = "F001-1",
        string? rucProveedor = "20100000001",
        decimal totalOrig = 118.00m,
        string moneda = "PEN",
        string fechaEmision = "2026-08-10")
    {
        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.Factura
                 (ProveedorCodigo, RucProveedor, TipoComprobante, Numero, TotalOrig, Moneda, FechaEmision, Afectacion, Estado)
             VALUES
                 ('P00123', '{rucProveedor}', '{tipoComprobante}', {(numero is null ? "NULL" : $"'{numero}'")},
                  {totalOrig.ToString(System.Globalization.CultureInfo.InvariantCulture)}, '{moneda}', '{fechaEmision}',
                  'GRAVADA', 'PENDIENTE_VALIDACION');
             """);
        return await db.ExecuteScalarAsync<long>("SELECT MAX(FacturaId) FROM fact.Factura;");
    }

    /// <summary>PR 5 (Phase 5) — inserts a MANUAL <c>fact.TipoCambio</c> row for the given date, the
    /// minimum <c>ITipoCambioRepository</c> needs for <see cref="ResultadoTipoCambio.Vigente"/>.</summary>
    public static Task InsertarTipoCambioAsync(
        this TestDatabaseFixture db, string fecha = "2026-08-10", decimal compra = 3.70m, decimal venta = 3.75m) =>
        db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.TipoCambio (Fecha, Origen, Compra, Venta, FechaConsulta)
             VALUES ('{fecha}', 'MANUAL', {compra.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                     {venta.ToString(System.Globalization.CultureInfo.InvariantCulture)}, SYSUTCDATETIME());
             """);

    /// <summary>Inserts one <c>BORRADOR</c> <c>fact.AsientoContable</c> row (with a single balanced
    /// line so <c>InvariantesDeConfirmacion.Evaluar</c> can pass in a full validar test later) and
    /// returns its id.</summary>
    public static async Task<long> InsertarAsientoBorradorAsync(
        this TestDatabaseFixture db, long facturaId, decimal basePen = 100m, decimal igvPen = 18m)
    {
        await db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.AsientoContable
                 (FacturaId, OrigenLibro, ProveedorCodigo, FechaContable, BasePEN, IgvPEN, NetoPEN, Estado)
             VALUES
                 ({facturaId}, '02', 'P00123', '2026-08-10', {basePen.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                  {igvPen.ToString(System.Globalization.CultureInfo.InvariantCulture)},
                  {(basePen + igvPen).ToString(System.Globalization.CultureInfo.InvariantCulture)}, 'BORRADOR');
             """);
        return await db.ExecuteScalarAsync<long>("SELECT MAX(AsientoContableId) FROM fact.AsientoContable;");
    }

    /// <summary>Inserts one <c>fact.Usuario</c> row — the FK <c>fact.AuditoriaCorreccion.UsuarioId</c> needs.</summary>
    public static async Task<long> InsertarUsuarioAsync(this TestDatabaseFixture db, string nombreUsuario = "usuario.prueba")
    {
        await db.ExecuteNonQueryAsync(
            $"INSERT INTO fact.Usuario (NombreUsuario, ClaveHash) VALUES ('{nombreUsuario}', '$argon2id$fixture');");
        return await db.ExecuteScalarAsync<long>($"SELECT UsuarioId FROM fact.Usuario WHERE NombreUsuario = '{nombreUsuario}';");
    }

    public static Task<byte[]> ObtenerVersionAsync(this TestDatabaseFixture db, long asientoContableId) =>
        db.ExecuteScalarAsync<byte[]>(
            $"SELECT Version FROM fact.AsientoContable WHERE AsientoContableId = {asientoContableId};")!;

    // --- PR 2 additions ---

    public static Task<byte[]> ObtenerVersionFacturaAsync(this TestDatabaseFixture db, long facturaId) =>
        db.ExecuteScalarAsync<byte[]>($"SELECT Version FROM fact.Factura WHERE FacturaId = {facturaId};")!;
}
