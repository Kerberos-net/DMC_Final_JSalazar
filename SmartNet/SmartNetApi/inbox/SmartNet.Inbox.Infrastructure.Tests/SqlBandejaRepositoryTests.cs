using SmartNet.Db.TestBootstrap;
using SmartNet.Inbox.Core;

namespace SmartNet.Inbox.Infrastructure.Tests;

/// <summary>
/// BACKLOG #13 Phase 3 (tasks 3.1-3.9) -- <see cref="SqlBandejaRepository"/> backing
/// <c>GET /api/bandeja?estado=&amp;desde=&amp;hasta=&amp;proveedor=&amp;pagina=&amp;orden=</c>
/// (design.md D2-D5, D7). Tests 1-3 are approval tests migrated from the pre-#13 2-arg
/// <c>ListarAsync(string?, string, ct)</c> signature to <see cref="FiltrosBandeja"/> -- same
/// behavior, new contract, captured before the new scenarios below were added.
/// </summary>
public sealed class SqlBandejaRepositoryTests : IAsyncLifetime
{
    private TestDatabaseFixture _db = null!;

    public async Task InitializeAsync() => _db = await InboxTestDatabaseFixtureHelper.MigratedDatabaseAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    private static FiltrosBandeja Filtros(
        string? estado = null, DateOnly? desde = null, DateOnly? hasta = null, string? proveedor = null,
        string orden = "desc", int pagina = 1, int tamanioPagina = 20) =>
        new(estado, desde, hasta, proveedor, orden, pagina, tamanioPagina);

    // --- Approval tests (migrated to FiltrosBandeja) -----------------------------------------

    [Fact]
    public async Task ListarAsync_FiltersByEstadoConsumo()
    {
        var procesamientoId1 = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-a");
        var pendienteId = await _db.InsertarInboxEventAsync(procesamientoId1, "{}");
        var procesamientoId2 = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-b");
        var descartadoId = await _db.InsertarInboxEventAsync(procesamientoId2, "{}");
        await _db.ExecuteNonQueryAsync(
            $"UPDATE fact.InboxEvent SET EstadoConsumo = 'DESCARTADO', MotivoDescarte = 'sin monto' WHERE InboxEventId = {descartadoId};");

        var sut = new SqlBandejaRepository(_db.ConnectionString);

        var soloDescartados = await sut.ListarAsync(Filtros(estado: "DESCARTADO", orden: "asc"), CancellationToken.None);
        var item = Assert.Single(soloDescartados.Items);
        Assert.Equal(descartadoId, item.InboxEventId);
        Assert.Equal("sin monto", item.MotivoDescarte);
        Assert.Equal("INCIDENCIA", item.Origen);

        var pendientes = await sut.ListarAsync(Filtros(estado: "PENDIENTE", orden: "asc"), CancellationToken.None);
        Assert.Contains(pendientes.Items, i => i.InboxEventId == pendienteId);
    }

    [Fact]
    public async Task ListarAsync_IncludesIndicadores_WhenTheEventWasPromoted()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync();
        var inboxEventId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        var promocionRepo = new SqlPromocionRepository(_db.ConnectionString);
        var factura = new Core.FacturaPromovida(
            ProveedorCodigo: "P00000", TipoComprobante: "01", Numero: "F001-1", RucProveedor: "20100000001",
            TotalOrig: 100m, Moneda: "PEN", FechaEmision: new DateOnly(2026, 8, 9),
            Indicadores: new Core.IndicadoresFactura(true, false, false, false, false),
            Extracciones: Array.Empty<Core.FacturaExtraccionPromovida>(), Estado: "PENDIENTE_VALIDACION");
        var documento = new Core.DocumentoPromovido(
            DocumentoRecibidoId: 1, NombreArchivo: "f.pdf", MimeType: "application/pdf", RutaRelativa: "/f.pdf", TamanoBytes: 10);
        await promocionRepo.PromoverAsync(inboxEventId, procesamientoId, factura, documento, CancellationToken.None);

        var sut = new SqlBandejaRepository(_db.ConnectionString);
        var resultado = await sut.ListarAsync(Filtros(estado: "PROMOVIDO", orden: "asc"), CancellationToken.None);

        var item = Assert.Single(resultado.Items);
        Assert.NotNull(item.FacturaId);
        Assert.NotNull(item.Indicadores);
        Assert.True(item.Indicadores!.EsProveedorGenerico);
        Assert.Equal("FACTURA", item.Origen);
        Assert.Equal("20100000001", item.RucProveedor);
        Assert.Equal("P00000", item.ProveedorCodigo);
    }

    [Fact]
    public async Task ListarAsync_OrdersByFecha_Descending()
    {
        var procesamientoId1 = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-c");
        var primeroId = await _db.InsertarInboxEventAsync(procesamientoId1, "{}", creadoEn: new DateTime(2026, 8, 1, 8, 0, 0));
        var procesamientoId2 = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-d");
        var segundoId = await _db.InsertarInboxEventAsync(procesamientoId2, "{}", creadoEn: new DateTime(2026, 8, 2, 8, 0, 0));
        var sut = new SqlBandejaRepository(_db.ConnectionString);

        var resultado = await sut.ListarAsync(Filtros(orden: "desc"), CancellationToken.None);

        Assert.Equal(segundoId, resultado.Items[0].InboxEventId);
        Assert.Equal(primeroId, resultado.Items[1].InboxEventId);
    }

    // --- Task 3.1: OFFSET/FETCH tiebreak stability on duplicate CreadoEn ---------------------

    [Fact]
    public async Task ListarAsync_UsesInboxEventIdAsTiebreaker_WhenCreadoEnDuplicates()
    {
        var mismaFecha = new DateTime(2026, 8, 3, 12, 0, 0);
        var procesamientoId1 = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-tie-1");
        var primeroId = await _db.InsertarInboxEventAsync(procesamientoId1, "{}", creadoEn: mismaFecha);
        var procesamientoId2 = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-tie-2");
        var segundoId = await _db.InsertarInboxEventAsync(procesamientoId2, "{}", creadoEn: mismaFecha);
        var procesamientoId3 = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-tie-3");
        var tercerId = await _db.InsertarInboxEventAsync(procesamientoId3, "{}", creadoEn: mismaFecha);
        var sut = new SqlBandejaRepository(_db.ConnectionString);

        // pagina 1 (2 rows) + pagina 2 (1 row) must partition the 3 same-CreadoEn rows without
        // repeats or drops -- only possible if InboxEventId breaks the CreadoEn tie deterministically.
        var pagina1 = await sut.ListarAsync(Filtros(orden: "asc", pagina: 1, tamanioPagina: 2), CancellationToken.None);
        var pagina2 = await sut.ListarAsync(Filtros(orden: "asc", pagina: 2, tamanioPagina: 2), CancellationToken.None);

        Assert.Equal(new[] { primeroId, segundoId }, pagina1.Items.Select(i => i.InboxEventId));
        Assert.Equal(new[] { tercerId }, pagina2.Items.Select(i => i.InboxEventId));
        Assert.Equal(3, pagina1.TotalRegistros);
        Assert.Equal(3, pagina2.TotalRegistros);
    }

    // --- Task 3.2: desde/hasta filter, hasta inclusive of the whole day ----------------------

    [Fact]
    public async Task ListarAsync_FiltersByDesdeHasta_HastaIsInclusiveOfWholeDay()
    {
        var procesamientoAntes = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-antes");
        var idAntes = await _db.InsertarInboxEventAsync(procesamientoAntes, "{}", creadoEn: new DateTime(2026, 8, 1, 10, 0, 0));
        var procesamientoDentro = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-dentro");
        var idDentro = await _db.InsertarInboxEventAsync(procesamientoDentro, "{}", creadoEn: new DateTime(2026, 8, 3, 23, 30, 0));
        var procesamientoDespues = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-despues");
        var idDespues = await _db.InsertarInboxEventAsync(procesamientoDespues, "{}", creadoEn: new DateTime(2026, 8, 4, 0, 0, 1));
        var sut = new SqlBandejaRepository(_db.ConnectionString);

        var resultado = await sut.ListarAsync(
            Filtros(desde: new DateOnly(2026, 8, 2), hasta: new DateOnly(2026, 8, 3), orden: "asc"),
            CancellationToken.None);

        var idsEnRango = resultado.Items.Select(i => i.InboxEventId).ToList();
        Assert.DoesNotContain(idAntes, idsEnRango);
        Assert.Contains(idDentro, idsEnRango);
        Assert.DoesNotContain(idDespues, idsEnRango);
    }

    // --- Task 3.3: proveedor identity match + JSON_VALUE fallback ----------------------------

    [Fact]
    public async Task ListarAsync_FiltersByProveedor_MatchesIdentityOnPromotedRows()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-prov-promovido");
        var inboxEventId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        var promocionRepo = new SqlPromocionRepository(_db.ConnectionString);
        var factura = new Core.FacturaPromovida(
            ProveedorCodigo: "P00001", TipoComprobante: "01", Numero: "F001-2", RucProveedor: "20999999999",
            TotalOrig: 200m, Moneda: "PEN", FechaEmision: new DateOnly(2026, 8, 9),
            Indicadores: new Core.IndicadoresFactura(false, false, false, false, false),
            Extracciones: Array.Empty<Core.FacturaExtraccionPromovida>(), Estado: "PENDIENTE_VALIDACION");
        var documento = new Core.DocumentoPromovido(
            DocumentoRecibidoId: 2, NombreArchivo: "g.pdf", MimeType: "application/pdf", RutaRelativa: "/g.pdf", TamanoBytes: 10);
        await promocionRepo.PromoverAsync(inboxEventId, procesamientoId, factura, documento, CancellationToken.None);
        var sut = new SqlBandejaRepository(_db.ConnectionString);

        var porRuc = await sut.ListarAsync(Filtros(estado: "PROMOVIDO", proveedor: "20999999999"), CancellationToken.None);
        var porCodigo = await sut.ListarAsync(Filtros(estado: "PROMOVIDO", proveedor: "P00001"), CancellationToken.None);
        var sinMatch = await sut.ListarAsync(Filtros(estado: "PROMOVIDO", proveedor: "20111111111"), CancellationToken.None);

        Assert.Contains(porRuc.Items, i => i.InboxEventId == inboxEventId);
        Assert.Contains(porCodigo.Items, i => i.InboxEventId == inboxEventId);
        Assert.Empty(sinMatch.Items);
    }

    [Fact]
    public async Task ListarAsync_FiltersByProveedor_FallsBackToPayloadJson_ForNonPromotedRows()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-prov-pendiente");
        var payload = """{"comprobante":{"rucProveedor":"20555555555"}}""";
        var inboxEventId = await _db.InsertarInboxEventAsync(procesamientoId, payload);
        var sut = new SqlBandejaRepository(_db.ConnectionString);

        var conMatch = await sut.ListarAsync(Filtros(estado: "PENDIENTE", proveedor: "20555555555"), CancellationToken.None);
        var sinMatch = await sut.ListarAsync(Filtros(estado: "PENDIENTE", proveedor: "20444444444"), CancellationToken.None);

        Assert.Contains(conMatch.Items, i => i.InboxEventId == inboxEventId);
        Assert.DoesNotContain(sinMatch.Items, i => i.InboxEventId == inboxEventId);
    }

    // --- Task 3.4: second result set for errors, no row duplication --------------------------

    [Fact]
    public async Task ListarAsync_IncludesErrorHistory_WithoutDuplicatingBandejaRows()
    {
        var procesamientoConErrores = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-con-errores");
        var inboxEventConErrores = await _db.InsertarInboxEventAsync(procesamientoConErrores, "{}");
        await _db.InsertarProcesamientoErrorAsync(procesamientoConErrores, clasificacion: "TRANSITORIO", mensaje: "primer intento");
        await _db.InsertarProcesamientoErrorAsync(procesamientoConErrores, clasificacion: "DIFERIBLE", mensaje: "segundo intento");
        var procesamientoSinErrores = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-sin-errores");
        var inboxEventSinErrores = await _db.InsertarInboxEventAsync(procesamientoSinErrores, "{}");
        var sut = new SqlBandejaRepository(_db.ConnectionString);

        var resultado = await sut.ListarAsync(Filtros(estado: "PENDIENTE", orden: "asc"), CancellationToken.None);

        var conErrores = Assert.Single(resultado.Items, i => i.InboxEventId == inboxEventConErrores);
        Assert.Equal(2, conErrores.Errores.Count);
        Assert.Contains(conErrores.Errores, e => e.Mensaje == "primer intento");
        Assert.Contains(conErrores.Errores, e => e.Mensaje == "segundo intento");

        var sinErrores = Assert.Single(resultado.Items, i => i.InboxEventId == inboxEventSinErrores);
        Assert.Empty(sinErrores.Errores);
    }

    // --- Task 3.5: reprocesarDisponibleEn from fact.CommandQueue -----------------------------

    [Fact]
    public async Task ListarAsync_ComputesReprocesarDisponibleEn_FromPendingCommandQueue()
    {
        var procesamientoPendiente = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-cq-pendiente");
        var inboxPendiente = await _db.InsertarInboxEventAsync(procesamientoPendiente, "{}");
        var creadoEnComando = DateTime.UtcNow;
        await _db.InsertarCommandQueueReprocesarAsync(procesamientoPendiente, estado: "PENDIENTE", creadoEn: creadoEnComando);

        var procesamientoSinComando = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-cq-ninguno");
        var inboxSinComando = await _db.InsertarInboxEventAsync(procesamientoSinComando, "{}");
        var sut = new SqlBandejaRepository(_db.ConnectionString);

        var resultado = await sut.ListarAsync(Filtros(estado: "PENDIENTE", orden: "asc"), CancellationToken.None);

        var conComando = resultado.Items.Single(i => i.InboxEventId == inboxPendiente);
        Assert.NotNull(conComando.ReprocesarDisponibleEn);
        Assert.Equal(
            creadoEnComando.AddMinutes(PoliticaDeReprocesamiento.VentanaMinutos),
            conComando.ReprocesarDisponibleEn!.Value,
            TimeSpan.FromSeconds(1));

        var sinComando = resultado.Items.Single(i => i.InboxEventId == inboxSinComando);
        Assert.Null(sinComando.ReprocesarDisponibleEn);
    }

    // --- Task 3.6: empty page (pagina > totalPaginas) -> truthful totalRegistros via fallback COUNT

    [Fact]
    public async Task ListarAsync_EmptyPage_ReturnsTruthfulTotalRegistros_ViaFallbackCount()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-fuera-de-rango");
        await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        var sut = new SqlBandejaRepository(_db.ConnectionString);

        var resultado = await sut.ListarAsync(Filtros(estado: "PENDIENTE", pagina: 5, tamanioPagina: 1), CancellationToken.None);

        Assert.Empty(resultado.Items);
        Assert.Equal(1, resultado.TotalRegistros);
        Assert.Equal(1, resultado.TotalPaginas);
    }

    // --- Phase 4 gap found during API-level testing: default view (estado omitted) ------------

    [Fact]
    public async Task ListarAsync_DefaultView_ExcludesTerminalRows_WhenEstadoIsOmitted()
    {
        var procesamientoPendiente = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-default-pendiente");
        var inboxPendiente = await _db.InsertarInboxEventAsync(procesamientoPendiente, "{}");

        var procesamientoPromovido = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-default-promovido");
        var inboxPromovido = await _db.InsertarInboxEventAsync(procesamientoPromovido, "{}");
        var promocionRepo = new SqlPromocionRepository(_db.ConnectionString);
        var factura = new Core.FacturaPromovida(
            ProveedorCodigo: "P00002", TipoComprobante: "01", Numero: "F001-3", RucProveedor: "20888888888",
            TotalOrig: 50m, Moneda: "PEN", FechaEmision: new DateOnly(2026, 8, 9),
            Indicadores: new Core.IndicadoresFactura(false, false, false, false, false),
            Extracciones: Array.Empty<Core.FacturaExtraccionPromovida>(), Estado: "PENDIENTE_VALIDACION");
        var documento = new Core.DocumentoPromovido(
            DocumentoRecibidoId: 3, NombreArchivo: "h.pdf", MimeType: "application/pdf", RutaRelativa: "/h.pdf", TamanoBytes: 10);
        await promocionRepo.PromoverAsync(inboxPromovido, procesamientoPromovido, factura, documento, CancellationToken.None);

        var sut = new SqlBandejaRepository(_db.ConnectionString);

        var resultado = await sut.ListarAsync(Filtros(estado: null, orden: "asc"), CancellationToken.None);

        Assert.Contains(resultado.Items, i => i.InboxEventId == inboxPendiente);
        Assert.DoesNotContain(resultado.Items, i => i.InboxEventId == inboxPromovido);
    }

    // --- Task 3.7: run AS usr_api, proving the D1 grant via the engine, not mocked permissions -

    [Fact]
    public async Task ListarAsync_RunsAsUsrApi_ProvingTheD1PermissionGrant()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-usr-api");
        var inboxEventId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        await _db.InsertarProcesamientoErrorAsync(procesamientoId, clasificacion: "PERMANENTE", mensaje: "denegado antes de 018");
        await _db.InsertarCommandQueueReprocesarAsync(procesamientoId, estado: "PENDIENTE");

        var resultado = await _db.ExecuteAsUserAsync(
            "usr_api",
            connection => SqlBandejaRepository.ListarConConexionAsync(connection, Filtros(estado: "PENDIENTE"), CancellationToken.None));

        var item = Assert.Single(resultado.Items, i => i.InboxEventId == inboxEventId);
        var error = Assert.Single(item.Errores);
        Assert.Equal("denegado antes de 018", error.Mensaje);
        Assert.NotNull(item.ReprocesarDisponibleEn);
    }
}
