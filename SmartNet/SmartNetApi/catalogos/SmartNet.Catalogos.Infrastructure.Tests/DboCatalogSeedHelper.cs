using SmartNet.Db.TestBootstrap;

namespace SmartNet.Catalogos.Infrastructure.Tests;

/// <summary>
/// Local seed helper (design.md Decision 3): <c>TestDatabaseFixture.CreateExternalDboCatalogsAsync</c>
/// creates the 5 <c>dbo.*</c> tables empty and only seeds <c>dbo.Motivo</c>
/// (<c>SeedDboMotivoFixtureRowsAsync</c>). This project seeds the remaining 4 —
/// <c>CuentaContable</c>, <c>Proveedor</c>, <c>Origen</c>, <c>DocumentoIdentidad</c> — through the
/// already-public <see cref="TestDatabaseFixture.ExecuteNonQueryAsync"/>, kept local to this test
/// project so the shared fixture used by the other six test projects stays untouched.
/// </summary>
internal static class DboCatalogSeedHelper
{
    public static Task SeedCuentaContableAsync(
        this TestDatabaseFixture db,
        string cuenta,
        string descripcion,
        byte? nivel = null,
        string? ctaRefleja = null,
        string? ctaPuente = null,
        CancellationToken ct = default)
    {
        var nivelLiteral = nivel is null ? "NULL" : nivel.Value.ToString();
        var ctaReflejaLiteral = ctaRefleja is null ? "NULL" : $"'{ctaRefleja}'";
        var ctaPuenteLiteral = ctaPuente is null ? "NULL" : $"'{ctaPuente}'";
        return db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO dbo.CuentaContable (cuenta, descripcion, nivel, ctarefleja, ctapuente)
             VALUES ('{cuenta}', N'{descripcion}', {nivelLiteral}, {ctaReflejaLiteral}, {ctaPuenteLiteral});
             """,
            ct);
    }

    public static Task SeedProveedorAsync(
        this TestDatabaseFixture db,
        string codpro,
        string proveedor,
        string? coddocide = null,
        string? rucpro = null,
        CancellationToken ct = default)
    {
        var coddocideLiteral = coddocide is null ? "NULL" : $"'{coddocide}'";
        var rucproLiteral = rucpro is null ? "NULL" : $"'{rucpro}'";
        return db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO dbo.Proveedor (codpro, proveedor, coddocide, rucpro)
             VALUES ('{codpro}', N'{proveedor}', {coddocideLiteral}, {rucproLiteral});
             """,
            ct);
    }

    public static Task SeedOrigenAsync(
        this TestDatabaseFixture db, string codigo, string origen, CancellationToken ct = default) =>
        db.ExecuteNonQueryAsync(
            $"INSERT INTO dbo.Origen (codigo, origen) VALUES ('{codigo}', N'{origen}');",
            ct);

    public static Task SeedDocumentoIdentidadAsync(
        this TestDatabaseFixture db, string coddocide, string nomdocide, CancellationToken ct = default) =>
        db.ExecuteNonQueryAsync(
            $"INSERT INTO dbo.DocumentoIdentidad (coddocide, nomdocide) VALUES ('{coddocide}', N'{nomdocide}');",
            ct);
}
