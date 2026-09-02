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
        string orden = "desc", int pagina = 1, int tamanioPagina = 20, string? estadoDerivado = null) =>
        new(estado, desde, hasta, proveedor, orden, pagina, tamanioPagina, estadoDerivado);

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
            Indicadores: new Core.IndicadoresFactura(true, false, false, false, false, Array.Empty<string>()),
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
    public async Task ListarAsync_CollapsesASecondaryPromotion_OneRowPerFactura()
    {
        // The XML's InboxEvent creates the factura (fact.Factura.ProcesamientoId = this one).
        var xmlInboxEventId = await PromoverFacturaAsync("msg-par-xml", proveedorCodigo: "P00000", numero: "F001-77");
        var facturaId = await _db.ExecuteScalarAsync<long>(
            "SELECT MAX(FacturaId) FROM fact.Factura WHERE Numero = 'F001-77';");

        // The associated-PDF InboxEvent (BACKLOG #25 merge): a distinct ProcesamientoId, but
        // marked PROMOVIDO onto the SAME factura instead of creating a second one.
        var pdfProcesamientoId = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-par-pdf");
        var pdfInboxEventId = await _db.InsertarInboxEventAsync(pdfProcesamientoId, "{}");
        await _db.ExecuteNonQueryAsync(
            $"UPDATE fact.InboxEvent SET EstadoConsumo = 'PROMOVIDO', FacturaId = {facturaId} WHERE InboxEventId = {pdfInboxEventId};");

        var sut = new SqlBandejaRepository(_db.ConnectionString);
        var resultado = await sut.ListarAsync(Filtros(estado: "PROMOVIDO", orden: "asc"), CancellationToken.None);

        // The factura appears exactly once — via the XML's event, not the secondary PDF merge.
        var item = Assert.Single(resultado.Items, i => i.FacturaId == facturaId);
        Assert.Equal(xmlInboxEventId, item.InboxEventId);
        Assert.DoesNotContain(resultado.Items, i => i.InboxEventId == pdfInboxEventId);

        // The dashboard aggregate never double-counts the factura either.
        Assert.Equal(1, resultado.Resumen.Validadas);
        Assert.Equal(resultado.Resumen.Total,
            resultado.Resumen.Pendientes + resultado.Resumen.Validadas + resultado.Resumen.ConError
            + resultado.Resumen.Alertas + resultado.Resumen.Descartadas);
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
            Indicadores: new Core.IndicadoresFactura(false, false, false, false, false, Array.Empty<string>()),
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
            Indicadores: new Core.IndicadoresFactura(false, false, false, false, false, Array.Empty<string>()),
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

    // --- BACKLOG #21 task 2.1: enriched comprobante fields -----------------------------------

    private async Task<long> PromoverFacturaAsync(
        string gmailMessageId, string proveedorCodigo, string tipoComprobante = "01", string numero = "F001-9",
        string rucProveedor = "20100000009", decimal totalOrig = 123.45m, string moneda = "PEN",
        DateOnly? fechaEmision = null, bool esProveedorGenerico = false, bool posibleDuplicado = false)
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync(gmailMessageId: gmailMessageId);
        var inboxEventId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        var promocionRepo = new SqlPromocionRepository(_db.ConnectionString);
        var factura = new Core.FacturaPromovida(
            ProveedorCodigo: proveedorCodigo, TipoComprobante: tipoComprobante, Numero: numero, RucProveedor: rucProveedor,
            TotalOrig: totalOrig, Moneda: moneda, FechaEmision: fechaEmision ?? new DateOnly(2026, 8, 9),
            Indicadores: new Core.IndicadoresFactura(esProveedorGenerico, posibleDuplicado, false, false, false, Array.Empty<string>()),
            Extracciones: Array.Empty<Core.FacturaExtraccionPromovida>(), Estado: "PENDIENTE_VALIDACION");
        var documento = new Core.DocumentoPromovido(
            DocumentoRecibidoId: 1, NombreArchivo: "f.pdf", MimeType: "application/pdf", RutaRelativa: "/f.pdf", TamanoBytes: 10);
        await promocionRepo.PromoverAsync(inboxEventId, procesamientoId, factura, documento, CancellationToken.None);
        return inboxEventId;
    }

    private Task SeedProveedorAsync(string codpro, string nombre) =>
        _db.ExecuteNonQueryAsync(
            $"INSERT INTO dbo.Proveedor (codpro, proveedor, rucpro) VALUES ('{codpro}', N'{nombre}', NULL);");

    [Fact]
    public async Task ListarAsync_ProjectsEnrichedComprobanteFields_FromFacturaAndProveedor()
    {
        await SeedProveedorAsync("P00777", "Distribuidora del Sur SAC");
        var inboxEventId = await PromoverFacturaAsync(
            "msg-21-enriquecido", proveedorCodigo: "P00777", tipoComprobante: "07", numero: "F123-456",
            totalOrig: 4200.50m, moneda: "USD", fechaEmision: new DateOnly(2026, 7, 15));

        var sut = new SqlBandejaRepository(_db.ConnectionString);
        var resultado = await sut.ListarAsync(Filtros(estado: "PROMOVIDO", orden: "asc"), CancellationToken.None);

        var item = Assert.Single(resultado.Items, i => i.InboxEventId == inboxEventId);
        Assert.Equal("Distribuidora del Sur SAC", item.ProveedorNombre);
        Assert.Equal("07", item.TipoComprobante);
        Assert.Equal("F123-456", item.Numero);
        Assert.Equal(4200.50m, item.TotalOrig);
        Assert.Equal("USD", item.Moneda);
        Assert.Equal(new DateOnly(2026, 7, 15), item.FechaEmision);
    }

    [Fact]
    public async Task ListarAsync_ProveedorNombreIsNull_WhenCodproIsAbsentFromCatalog()
    {
        var inboxEventId = await PromoverFacturaAsync(
            "msg-21-sin-catalogo", proveedorCodigo: "P09999", numero: "F001-11");

        var sut = new SqlBandejaRepository(_db.ConnectionString);
        var resultado = await sut.ListarAsync(Filtros(estado: "PROMOVIDO", orden: "asc"), CancellationToken.None);

        var item = Assert.Single(resultado.Items, i => i.InboxEventId == inboxEventId);
        Assert.Null(item.ProveedorNombre);
        Assert.Equal("P09999", item.ProveedorCodigo);
    }

    [Fact]
    public async Task ListarAsync_EnrichedFieldsAreNull_ForIncidenciaRows()
    {
        var procesamientoId = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-21-incidencia");
        var inboxEventId = await _db.InsertarInboxEventAsync(procesamientoId, "{}");
        await _db.InsertarProcesamientoErrorAsync(procesamientoId, mensaje: "fallo");

        var sut = new SqlBandejaRepository(_db.ConnectionString);
        var resultado = await sut.ListarAsync(Filtros(estado: "PENDIENTE", orden: "asc"), CancellationToken.None);

        var item = Assert.Single(resultado.Items, i => i.InboxEventId == inboxEventId);
        Assert.Equal("INCIDENCIA", item.Origen);
        Assert.Null(item.ProveedorNombre);
        Assert.Null(item.TipoComprobante);
        Assert.Null(item.Numero);
        Assert.Null(item.TotalOrig);
        Assert.Null(item.Moneda);
        Assert.Null(item.FechaEmision);
    }

    // --- BACKLOG #21 task 2.2: the global estado aggregate ----------------------------------

    [Fact]
    public async Task Resumen_BucketsPartitionTheSet_AndCountPromotedRowsInValidadas()
    {
        // pendiente
        var pendienteProc = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-21-agg-pendiente");
        await _db.InsertarInboxEventAsync(pendienteProc, "{}");
        // validada: promoted, no errors, no alert flags
        await PromoverFacturaAsync("msg-21-agg-validada", proveedorCodigo: "P00001", numero: "F001-21");
        // con error: pendiente + a ProcesamientoError row
        var errorProc = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-21-agg-error");
        await _db.InsertarInboxEventAsync(errorProc, "{}");
        await _db.InsertarProcesamientoErrorAsync(errorProc, mensaje: "boom");
        // alerta: promoted with esProveedorGenerico
        await PromoverFacturaAsync("msg-21-agg-alerta", proveedorCodigo: "P00002", numero: "F001-22", esProveedorGenerico: true);
        // descartada
        var descartadaProc = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-21-agg-descartada");
        var descartadaId = await _db.InsertarInboxEventAsync(descartadaProc, "{}");
        await _db.ExecuteNonQueryAsync(
            $"UPDATE fact.InboxEvent SET EstadoConsumo = 'DESCARTADO', MotivoDescarte = 'sin monto' WHERE InboxEventId = {descartadaId};");

        var sut = new SqlBandejaRepository(_db.ConnectionString);
        var r = (await sut.ListarAsync(Filtros(orden: "asc"), CancellationToken.None)).Resumen;

        Assert.Equal(1, r.Pendientes);
        Assert.Equal(1, r.Validadas);
        Assert.Equal(1, r.ConError);
        Assert.Equal(1, r.Alertas);
        Assert.Equal(1, r.Descartadas);
        Assert.Equal(r.Total, r.Pendientes + r.Validadas + r.ConError + r.Alertas + r.Descartadas);
        Assert.Equal(5, r.Total);
    }

    [Fact]
    public async Task Resumen_FirstMatchPrecedence_ErrorBeatsAlerta_DescartadoBeatsError()
    {
        // promoted, generic proveedor (alerta) AND has an error row -> must count as ConError, not Alertas
        var inboxEventId = await PromoverFacturaAsync(
            "msg-21-prec-error-alerta", proveedorCodigo: "P00003", numero: "F001-23", esProveedorGenerico: true);
        var procId = await _db.ExecuteScalarAsync<long>(
            $"SELECT ProcesamientoId FROM fact.InboxEvent WHERE InboxEventId = {inboxEventId};");
        await _db.InsertarProcesamientoErrorAsync(procId, mensaje: "boom");

        // discarded row that still carries error history -> must count as Descartadas, not ConError
        var descProc = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-21-prec-descartado");
        var descId = await _db.InsertarInboxEventAsync(descProc, "{}");
        await _db.InsertarProcesamientoErrorAsync(descProc, mensaje: "historico");
        await _db.ExecuteNonQueryAsync(
            $"UPDATE fact.InboxEvent SET EstadoConsumo = 'DESCARTADO', MotivoDescarte = 'x' WHERE InboxEventId = {descId};");

        var sut = new SqlBandejaRepository(_db.ConnectionString);
        var r = (await sut.ListarAsync(Filtros(orden: "asc"), CancellationToken.None)).Resumen;

        Assert.Equal(1, r.ConError);
        Assert.Equal(0, r.Alertas);
        Assert.Equal(1, r.Descartadas);
    }

    [Fact]
    public async Task Resumen_ObsoletoOnlyErrorHistory_StillCountsAsConError_MatchingTheChip()
    {
        var procId = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-21-obsoleto");
        await _db.InsertarInboxEventAsync(procId, "{}");
        await _db.InsertarProcesamientoErrorAsync(procId, clasificacion: "OBSOLETO", mensaje: "reintento superado");

        var sut = new SqlBandejaRepository(_db.ConnectionString);
        var r = (await sut.ListarAsync(Filtros(orden: "asc"), CancellationToken.None)).Resumen;

        // D2b: the aggregate's ERROR bucket uses an unfiltered EXISTS, matching chipEstadoDe
        // (any errores.length > 0), not FiltroWhere (which drops OBSOLETO from the default view).
        Assert.Equal(1, r.ConError);
        Assert.Equal(0, r.Pendientes);
    }

    // --- BACKLOG #21 task 2.3: the aggregate ignores filters and pagination -----------------

    [Fact]
    public async Task Resumen_IsIdenticalAcrossFilterAndPaginationParameters()
    {
        await PromoverFacturaAsync("msg-21-inv-a", proveedorCodigo: "P00004", numero: "F001-24");
        var pendProc = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-21-inv-b");
        await _db.InsertarInboxEventAsync(pendProc, """{"comprobante":{"rucProveedor":"20555550000"}}""");

        var sut = new SqlBandejaRepository(_db.ConnectionString);
        var baseline = (await sut.ListarAsync(Filtros(orden: "asc"), CancellationToken.None)).Resumen;

        var conEstado = (await sut.ListarAsync(Filtros(estado: "PENDIENTE"), CancellationToken.None)).Resumen;
        var conProveedor = (await sut.ListarAsync(Filtros(proveedor: "20555550000"), CancellationToken.None)).Resumen;
        var conFechas = (await sut.ListarAsync(
            Filtros(desde: new DateOnly(2026, 1, 1), hasta: new DateOnly(2026, 1, 2)), CancellationToken.None)).Resumen;
        var pagina2 = (await sut.ListarAsync(Filtros(pagina: 2, tamanioPagina: 1), CancellationToken.None)).Resumen;

        Assert.Equal(baseline, conEstado);
        Assert.Equal(baseline, conProveedor);
        Assert.Equal(baseline, conFechas);
        Assert.Equal(baseline, pagina2);
    }

    // --- BACKLOG #21 follow-up: estadoDerivado bucket filter (SPA estado chips) --------------

    /// <summary>
    /// Seeds one row per derived bucket and returns their InboxEventIds keyed by bucket name.
    /// </summary>
    private async Task<Dictionary<string, long>> SeedUnaFilaPorBucketAsync(string sufijo)
    {
        var ids = new Dictionary<string, long>();

        var pendProc = await _db.InsertarProcesamientoAsync(gmailMessageId: $"msg-ed-{sufijo}-pend");
        ids["PENDIENTE"] = await _db.InsertarInboxEventAsync(pendProc, "{}");

        ids["VALIDADA"] = await PromoverFacturaAsync(
            $"msg-ed-{sufijo}-val", proveedorCodigo: "P00001", numero: $"F-{sufijo}-1");

        var errProc = await _db.InsertarProcesamientoAsync(gmailMessageId: $"msg-ed-{sufijo}-err");
        ids["ERROR"] = await _db.InsertarInboxEventAsync(errProc, "{}");
        await _db.InsertarProcesamientoErrorAsync(errProc, mensaje: "boom");

        ids["ALERTA"] = await PromoverFacturaAsync(
            $"msg-ed-{sufijo}-ale", proveedorCodigo: "P00002", numero: $"F-{sufijo}-2", esProveedorGenerico: true);

        var descProc = await _db.InsertarProcesamientoAsync(gmailMessageId: $"msg-ed-{sufijo}-desc");
        ids["DESCARTADA"] = await _db.InsertarInboxEventAsync(descProc, "{}");
        await _db.ExecuteNonQueryAsync(
            $"UPDATE fact.InboxEvent SET EstadoConsumo = 'DESCARTADO', MotivoDescarte = 'x' WHERE InboxEventId = {ids["DESCARTADA"]};");

        return ids;
    }

    [Theory]
    [InlineData("PENDIENTE")]
    [InlineData("VALIDADA")]
    [InlineData("ERROR")]
    [InlineData("ALERTA")]
    [InlineData("DESCARTADA")]
    public async Task ListarAsync_EstadoDerivado_ReturnsOnlyRowsInThatBucket(string bucket)
    {
        var ids = await SeedUnaFilaPorBucketAsync(bucket.ToLowerInvariant());
        var sut = new SqlBandejaRepository(_db.ConnectionString);

        var resultado = await sut.ListarAsync(Filtros(estadoDerivado: bucket, orden: "asc"), CancellationToken.None);

        Assert.Equal(new[] { ids[bucket] }, resultado.Items.Select(i => i.InboxEventId));
        Assert.Equal(1, resultado.TotalRegistros);
    }

    [Fact]
    public async Task ListarAsync_EstadoDerivadoTodos_ReturnsEveryEligibleRow_MatchingResumenTotal()
    {
        await SeedUnaFilaPorBucketAsync("todos");
        var sut = new SqlBandejaRepository(_db.ConnectionString);

        var resultado = await sut.ListarAsync(Filtros(estadoDerivado: "TODOS", orden: "asc"), CancellationToken.None);

        Assert.Equal(5, resultado.TotalRegistros);
        Assert.Equal(resultado.Resumen.Total, resultado.TotalRegistros);
    }

    [Fact]
    public async Task ListarAsync_EstadoDerivadoError_IncludesObsoletoOnlyRow_MatchingTheCard()
    {
        var procId = await _db.InsertarProcesamientoAsync(gmailMessageId: "msg-ed-obsoleto");
        var inboxId = await _db.InsertarInboxEventAsync(procId, "{}");
        await _db.InsertarProcesamientoErrorAsync(procId, clasificacion: "OBSOLETO", mensaje: "reintento superado");
        var sut = new SqlBandejaRepository(_db.ConnectionString);

        var resultado = await sut.ListarAsync(Filtros(estadoDerivado: "ERROR", orden: "asc"), CancellationToken.None);

        // D2b: the ERROR bucket (chip + card + estadoDerivado) counts any ProcesamientoError,
        // unlike the default list view which drops OBSOLETO.
        Assert.Contains(resultado.Items, i => i.InboxEventId == inboxId);
        Assert.Equal(resultado.Resumen.ConError, resultado.TotalRegistros);
    }

    [Fact]
    public async Task ListarAsync_EstadoDerivadoBucketTotals_MatchTheResumenBuckets()
    {
        await SeedUnaFilaPorBucketAsync("match");
        var sut = new SqlBandejaRepository(_db.ConnectionString);
        var resumen = (await sut.ListarAsync(Filtros(orden: "asc"), CancellationToken.None)).Resumen;

        foreach (var (bucket, esperado) in new[]
        {
            ("PENDIENTE", resumen.Pendientes), ("VALIDADA", resumen.Validadas), ("ERROR", resumen.ConError),
            ("ALERTA", resumen.Alertas), ("DESCARTADA", resumen.Descartadas),
        })
        {
            var r = await sut.ListarAsync(Filtros(estadoDerivado: bucket, orden: "asc"), CancellationToken.None);
            Assert.Equal(esperado, r.TotalRegistros);
        }
    }

    // --- BACKLOG #21 task 2.4: the whole widened batch runs as usr_api ---------------------

    [Fact]
    public async Task ListarAsync_WidenedBatch_RunsAsUsrApi_ProvingProveedorAndAggregateGrants()
    {
        await SeedProveedorAsync("P00555", "Comercial Andina EIRL");
        var inboxEventId = await PromoverFacturaAsync(
            "msg-21-usr-api", proveedorCodigo: "P00555", numero: "F001-25");

        var resultado = await _db.ExecuteAsUserAsync(
            "usr_api",
            connection => SqlBandejaRepository.ListarConConexionAsync(
                connection, Filtros(estado: "PROMOVIDO"), CancellationToken.None));

        var item = Assert.Single(resultado.Items, i => i.InboxEventId == inboxEventId);
        Assert.Equal("Comercial Andina EIRL", item.ProveedorNombre);
        Assert.Equal(1, resultado.Resumen.Validadas);
        Assert.Equal(resultado.Resumen.Total,
            resultado.Resumen.Pendientes + resultado.Resumen.Validadas + resultado.Resumen.ConError
            + resultado.Resumen.Alertas + resultado.Resumen.Descartadas);
    }
}
