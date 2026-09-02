using System.Globalization;
using SmartNet.Db.TestBootstrap;

namespace SmartNet.Api.Tests;

/// <summary>
/// BACKLOG #24 (tasks.md 4.5/4.6) — seeds the external <c>dbo.*</c> catalog rows and the
/// <c>fact.*</c> satellite rows that <c>ServicioDeSugerencia</c> + <c>ComposicionDeAsiento.Componer</c>
/// need to compose a real REGLAS §10 asiento through <c>POST /api/facturas/{id}/abrir</c>.
///
/// Mirrors <c>SmartNet.Catalogos.Infrastructure.Tests.DboCatalogSeedHelper</c> (internal to that
/// assembly, so it cannot be reused here) and <see cref="FacturaTestDataHelper"/>'s own precedent
/// of duplicating a fixture helper across test assemblies rather than exposing test-only internals.
/// <c>SesionEndpointsTestBase</c> already runs <c>CreateExternalDboCatalogsAsync()</c> +
/// <c>SeedDboMotivoFixtureRowsAsync()</c>, so the <c>dbo.*</c> tables exist and <c>dbo.Motivo</c>
/// carries the 28-row fixture subset; this helper only adds the specific rows each example needs.
/// </summary>
internal static class CatalogoTestDataHelper
{
    /// <summary>A <c>dbo.Motivo</c> row whose <c>cuenta</c> column holds the prefix(es) the
    /// suggestion cascade resolves against (REGLAS §3 "el motivo declara prefijos, no cuentas").</summary>
    public static Task SeedMotivoAsync(
        this TestDatabaseFixture db, int codigo, string motivo, string cuentaPrefijos) =>
        db.ExecuteNonQueryAsync(
            $"INSERT INTO dbo.Motivo (codigo, motivo, cuenta) VALUES ({codigo}, N'{motivo}', '{cuentaPrefijos}');");

    /// <summary>A <c>dbo.CuentaContable</c> leaf (<c>nivel IS NULL</c> ⇒ <c>EsHojaImputable</c>).
    /// <paramref name="ctaRefleja"/>/<paramref name="ctaPuente"/> populated ⇒ <c>Componer</c>
    /// emits the DESTINO block for a cargo on this account.</summary>
    public static Task SeedCuentaContableAsync(
        this TestDatabaseFixture db, string cuenta, string descripcion,
        string? ctaRefleja = null, string? ctaPuente = null) =>
        db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO dbo.CuentaContable (cuenta, descripcion, nivel, ctarefleja, ctapuente)
             VALUES ('{cuenta}', N'{descripcion}', NULL,
                     {(ctaRefleja is null ? "NULL" : $"'{ctaRefleja}'")},
                     {(ctaPuente is null ? "NULL" : $"'{ctaPuente}'")});
             """);

    /// <summary>Marks the seeded provider (<c>P00123</c>, the code
    /// <see cref="FacturaTestDataHelper.InsertarFacturaAsync"/> hard-codes) as a related party so
    /// <c>CuentaDeProveedor.Codigo</c> resolves to the 4312xx family (REGLAS §10.3).</summary>
    public static Task SeedProveedorRelacionadoAsync(this TestDatabaseFixture db, string proveedorCodigo = "P00123") =>
        db.ExecuteNonQueryAsync(
            $"INSERT INTO fact.ProveedorAtributo (ProveedorCodigo, EsRelacionada) VALUES ('{proveedorCodigo}', 1);");

    /// <summary>Points a seeded factura at a motivo (the column
    /// <see cref="FacturaTestDataHelper.InsertarFacturaAsync"/> leaves NULL).</summary>
    public static Task AsignarMotivoAsync(this TestDatabaseFixture db, long facturaId, int motivoCodigo) =>
        db.ExecuteNonQueryAsync($"UPDATE fact.Factura SET Motivo = {motivoCodigo} WHERE FacturaId = {facturaId};");
}
