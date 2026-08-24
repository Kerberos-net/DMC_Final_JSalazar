using SmartNet.Db.TestBootstrap;
using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Infrastructure.Tests;

/// <summary>
/// tasks.md 1.3 (RED first) — design D7: <see cref="SqlAuditoriaRepository"/> must union
/// FACTURA + ASIENTO (including ANULADO) + ADJUNTO entries for one factura, newest-first, using
/// parameterized SQL only (no string interpolation of the caller-supplied id).
/// </summary>
public sealed class SqlAuditoriaRepositoryTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;

    public async Task InitializeAsync() => _db = await FacturacionTestDatabaseFixtureHelper.MigratedDatabaseAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task ListarPorFacturaAsync_UnionsFacturaAsientoIncludingAnuladoAndAdjunto_NewestFirst()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var usuarioId = await _db.InsertarUsuarioAsync();
        var asientoVigenteId = await _db.InsertarAsientoBorradorAsync(facturaId);
        var asientoAnuladoId = await InsertarAsientoAnuladoAsync(facturaId);
        var adjuntoId = await InsertarAdjuntoAsync(facturaId, usuarioId);

        await InsertarAuditoriaAsync(
            EntradaAuditoria.EntidadTipos.Factura, facturaId, EntradaAuditoria.Acciones.Correccion,
            usuarioId, "2026-01-01T10:00:00");
        await InsertarAuditoriaAsync(
            EntradaAuditoria.EntidadTipos.Asiento, asientoAnuladoId, EntradaAuditoria.Acciones.Anulacion,
            usuarioId, "2026-01-02T10:00:00");
        await InsertarAuditoriaAsync(
            EntradaAuditoria.EntidadTipos.Asiento, asientoVigenteId, EntradaAuditoria.Acciones.Correccion,
            usuarioId, "2026-01-03T10:00:00");
        await InsertarAuditoriaAsync(
            EntradaAuditoria.EntidadTipos.Adjunto, adjuntoId, EntradaAuditoria.Acciones.EliminacionAdjunto,
            usuarioId, "2026-01-04T10:00:00");

        var sut = new SqlAuditoriaRepository(_db.ConnectionString);

        var resultado = await sut.ListarPorFacturaAsync(facturaId, CancellationToken.None);

        Assert.Equal(4, resultado.Count);
        Assert.Equal(EntradaAuditoria.EntidadTipos.Adjunto, resultado[0].EntidadTipo);
        Assert.Equal(EntradaAuditoria.EntidadTipos.Asiento, resultado[1].EntidadTipo);
        Assert.Equal(asientoVigenteId, resultado[1].EntidadId);
        Assert.Equal(EntradaAuditoria.EntidadTipos.Asiento, resultado[2].EntidadTipo);
        Assert.Equal(asientoAnuladoId, resultado[2].EntidadId);
        Assert.Equal(EntradaAuditoria.EntidadTipos.Factura, resultado[3].EntidadTipo);
    }

    [Fact]
    public async Task ListarPorFacturaAsync_ReturnsEmpty_WhenTheFacturaHasNoAuditoriaRows()
    {
        var facturaId = await _db.InsertarFacturaAsync();
        var sut = new SqlAuditoriaRepository(_db.ConnectionString);

        var resultado = await sut.ListarPorFacturaAsync(facturaId, CancellationToken.None);

        Assert.Empty(resultado);
    }

    [Fact]
    public async Task ListarPorFacturaAsync_DoesNotLeakEntriesFromAnotherFactura()
    {
        var facturaId = await _db.InsertarFacturaAsync(numero: "F001-A");
        var otraFacturaId = await _db.InsertarFacturaAsync(numero: "F001-B");
        var usuarioId = await _db.InsertarUsuarioAsync();
        await InsertarAuditoriaAsync(
            EntradaAuditoria.EntidadTipos.Factura, otraFacturaId, EntradaAuditoria.Acciones.Correccion,
            usuarioId, "2026-01-01T10:00:00");
        var sut = new SqlAuditoriaRepository(_db.ConnectionString);

        var resultado = await sut.ListarPorFacturaAsync(facturaId, CancellationToken.None);

        Assert.Empty(resultado);
    }

    private Task InsertarAuditoriaAsync(
        string entidadTipo, long entidadId, string accion, long usuarioId, string ocurridoEn) =>
        _db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.AuditoriaCorreccion
                 (EntidadTipo, EntidadId, Accion, Campo, ValorOriginal, ValorNuevo, Motivo, UsuarioId, OcurridoEn)
             VALUES
                 ('{entidadTipo}', {entidadId}, '{accion}', 'Estado', 'A', 'B', NULL, {usuarioId}, '{ocurridoEn}');
             """);

    private async Task<long> InsertarAsientoAnuladoAsync(long facturaId)
    {
        await _db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.AsientoContable (FacturaId, OrigenLibro, ProveedorCodigo, FechaContable, Estado)
             VALUES ({facturaId}, '02', 'P00123', '2026-08-10', 'ANULADO');
             """);
        return await _db.ExecuteScalarAsync<long>("SELECT MAX(AsientoContableId) FROM fact.AsientoContable;");
    }

    private async Task<long> InsertarAdjuntoAsync(long facturaId, long usuarioId)
    {
        await _db.ExecuteNonQueryAsync(
            $"""
             INSERT INTO fact.AdjuntoManual
                 (FacturaId, NombreArchivo, RutaRelativa, MimeType, TamanoBytes, SubidoPorUsuarioId, SubidoEn)
             VALUES
                 ({facturaId}, 'f.pdf', '/adjuntos/f.pdf', 'application/pdf', 10, {usuarioId}, SYSUTCDATETIME());
             """);
        return await _db.ExecuteScalarAsync<long>("SELECT MAX(AdjuntoManualId) FROM fact.AdjuntoManual;");
    }
}
