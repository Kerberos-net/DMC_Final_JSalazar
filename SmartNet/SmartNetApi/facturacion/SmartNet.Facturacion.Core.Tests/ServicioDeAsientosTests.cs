using SmartNet.Contable.Core;
using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Core.Tests;

/// <summary>
/// tasks.md 1.5/1.6 — <see cref="ServicioDeAsientos"/> command sequencing against the fake
/// <see cref="IUnidadDeTrabajo"/>: PATCH (CAS + CORRECCION audit), reabrir (REAPERTURA audit),
/// anular (ANULACION audit, terminal — design.md api-asientos requirements).
/// </summary>
public class ServicioDeAsientosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly byte[] VersionActual = { 0, 0, 0, 0, 0, 0, 0, 5 };

    private static AsientoContable AsientoValido() => new(
        ProveedorCodigo: "P00123",
        FechaContable: new DateOnly(2026, 8, 10),
        MotivoDescripcion: "Compra de suministros",
        TipoCambioVenta: null,
        BasePEN: 100m,
        IgvPEN: 18m,
        NetoPEN: 118m,
        AfectacionCongelada: Afectacion.Gravada,
        Comprobante: TipoComprobante.Factura,
        Lineas: Array.Empty<LineaAsiento>());

    private static AsientoPersistido Confirmado() => new(
        AsientoContableId: 501,
        FacturaId: 100,
        Estado: AsientoPersistido.Confirmado,
        NumeroAsiento: "02-2026-08-000001",
        Version: VersionActual,
        Asiento: AsientoValido(),
        Hechos: HechosDeConflicto.Ninguno);

    private static AsientoPersistido Borrador() => Confirmado() with
    {
        Estado = AsientoPersistido.Borrador,
        NumeroAsiento = null,
    };

    [Fact]
    public async Task ActualizarAsync_WhenVersionMatches_SavesAndRegistersCorreccionAudit_AndCommits()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = Borrador();
        var sut = new ServicioDeAsientos(store);

        var resultado = await sut.ActualizarAsync(
            501, VersionActual, "Glosa", "Antigua", "Nueva", usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        Assert.Single(store.UnidadDeTrabajo.AuditoriasRegistradas);
        Assert.Equal(EntradaAuditoria.Acciones.Correccion, store.UnidadDeTrabajo.AuditoriasRegistradas[0].Accion);
        Assert.Equal("Glosa", store.UnidadDeTrabajo.AuditoriasRegistradas[0].Campo);
        Assert.True(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task ActualizarAsync_WhenAsientoIsConfirmado_ReturnsConflicto_EditWithoutReabrirIsBlocked()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = Confirmado();
        var sut = new ServicioDeAsientos(store);

        var resultado = await sut.ActualizarAsync(
            501, VersionActual, "Glosa", "Antigua", "Nueva", usuarioId: 1, Ahora, CancellationToken.None);

        var conflicto = Assert.IsType<ResultadoComando.Conflicto>(resultado);
        Assert.Equal(CasoConflicto.AsientoYaConfirmado, conflicto.Caso);
        Assert.False(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task ActualizarAsync_WhenVersionIsStale_ReturnsVersionEnConflicto_AndNeverCommits()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = Borrador();
        store.UnidadDeTrabajo.ResultadoDeGuardar = ResultadoEscritura.VersionEnConflicto;
        var sut = new ServicioDeAsientos(store);

        var resultado = await sut.ActualizarAsync(
            501, new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }, "Glosa", "Antigua", "Nueva", usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.VersionEnConflicto>(resultado);
        Assert.Empty(store.UnidadDeTrabajo.AuditoriasRegistradas);
        Assert.False(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task ReabrirAsync_WithMotivo_TransitionsToBorrador_RegistersReaperturaAudit_AndCommits()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = Confirmado();
        var sut = new ServicioDeAsientos(store);

        var resultado = await sut.ReabrirAsync(501, VersionActual, "Corrección de cuenta", usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        Assert.Equal(AsientoPersistido.Borrador, store.UnidadDeTrabajo.UltimoAsientoGuardado!.Estado);
        var auditoria = Assert.Single(store.UnidadDeTrabajo.AuditoriasRegistradas);
        Assert.Equal(EntradaAuditoria.Acciones.Reapertura, auditoria.Accion);
        Assert.Equal("Corrección de cuenta", auditoria.Motivo);
        Assert.True(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task ReabrirAsync_WithoutMotivo_ThrowsArgumentException_AndNeverOpensAWriteSequence()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = Confirmado();
        var sut = new ServicioDeAsientos(store);

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.ReabrirAsync(501, VersionActual, "  ", usuarioId: 1, Ahora, CancellationToken.None));

        Assert.Empty(store.UnidadDeTrabajo.Llamadas);
    }

    [Fact]
    public async Task ReabrirAsync_WhenAsientoIsBorrador_ReturnsConflicto_NothingConfirmedToReopen()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = Confirmado() with { Estado = AsientoPersistido.Borrador };
        var sut = new ServicioDeAsientos(store);

        var resultado = await sut.ReabrirAsync(501, VersionActual, "Motivo", usuarioId: 1, Ahora, CancellationToken.None);

        var conflicto = Assert.IsType<ResultadoComando.Conflicto>(resultado);
        Assert.Equal(CasoConflicto.AsientoYaConfirmado, conflicto.Caso);
        Assert.False(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task AnularAsync_WhenConfirmado_TransitionsToAnulado_RegistersAnulacionAudit_AndCommits()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = Confirmado();
        var sut = new ServicioDeAsientos(store);

        var resultado = await sut.AnularAsync(501, VersionActual, "Factura anulada por el proveedor", usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        Assert.Equal(AsientoPersistido.Anulado, store.UnidadDeTrabajo.UltimoAsientoGuardado!.Estado);
        var auditoria = Assert.Single(store.UnidadDeTrabajo.AuditoriasRegistradas);
        Assert.Equal(EntradaAuditoria.Acciones.Anulacion, auditoria.Accion);
        Assert.True(store.UnidadDeTrabajo.Committed);

        // outbox-mensajeria (BACKLOG #14, design D1/D2) -- ASIENTO_ANULADO, asientoId explícito
        // (ObtenerAsientoVigenteIdAsync excluye ANULADO -- resolver "vigente" tras anular no lo
        // encontraría).
        var evento = Assert.Single(store.UnidadDeTrabajo.EventosOutbox);
        Assert.Equal("ASIENTO_ANULADO", evento.Tipo);
        Assert.Equal(100, evento.FacturaId);
        Assert.DoesNotContain(nameof(IUnidadDeTrabajo.ObtenerAsientoVigenteIdAsync), store.UnidadDeTrabajo.Llamadas);
    }

    [Fact]
    public async Task AnularAsync_WhenAlreadyAnulado_ReturnsConflicto_TerminalNoTransition()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = Confirmado() with { Estado = AsientoPersistido.Anulado };
        var sut = new ServicioDeAsientos(store);

        var resultado = await sut.AnularAsync(501, VersionActual, "Motivo", usuarioId: 1, Ahora, CancellationToken.None);

        var conflicto = Assert.IsType<ResultadoComando.Conflicto>(resultado);
        Assert.Equal(CasoConflicto.AsientoYaConfirmado, conflicto.Caso);
        Assert.False(store.UnidadDeTrabajo.Committed);
        Assert.Empty(store.UnidadDeTrabajo.EventosOutbox);
    }

    // -----------------------------------------------------------------------------------------
    // tasks.md 3.2 (PR 3) — líneas por LineaId (spec.md api-asientos: "never position"). Cada
    // comando escribe UNA fila AuditoriaCorreccion(Accion=REPARTO_MANUAL) — design D6.
    // -----------------------------------------------------------------------------------------

    private static readonly LineaAsiento LineaValida = new(
        Orden: 1, Bloque: Bloque.Principal, Tipo: TipoLinea.D, Debe: 100m, Haber: 0m,
        CuentaCodigo: "639915", CuentaDescripcion: null, CtaReflejaCodigo: null, CtaPuenteCodigo: null);

    [Fact]
    public async Task AgregarLineaAsync_WhenAsientoIsBorrador_InsertsLinea_RegistersRepartoManualAudit_AndCommits()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = Borrador();
        store.UnidadDeTrabajo.ResultadoDeAgregarLinea = new ResultadoLinea(ResultadoEscritura.Aplicado, 950);
        var sut = new ServicioDeAsientos(store);

        var (resultado, lineaId) = await sut.AgregarLineaAsync(501, VersionActual, LineaValida, usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        Assert.Equal(950, lineaId);
        Assert.Equal(LineaValida, store.UnidadDeTrabajo.UltimaLineaAgregada);
        var auditoria = Assert.Single(store.UnidadDeTrabajo.AuditoriasRegistradas);
        Assert.Equal(EntradaAuditoria.Acciones.RepartoManual, auditoria.Accion);
        Assert.Equal("Cargos", auditoria.Campo);
        Assert.Null(auditoria.Motivo);
        Assert.True(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task AgregarLineaAsync_WhenAsientoIsConfirmado_ReturnsConflicto_EditWithoutReabrirIsBlocked()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = Confirmado();
        var sut = new ServicioDeAsientos(store);

        var (resultado, lineaId) = await sut.AgregarLineaAsync(501, VersionActual, LineaValida, usuarioId: 1, Ahora, CancellationToken.None);

        var conflicto = Assert.IsType<ResultadoComando.Conflicto>(resultado);
        Assert.Equal(CasoConflicto.AsientoYaConfirmado, conflicto.Caso);
        Assert.Null(lineaId);
        Assert.False(store.UnidadDeTrabajo.Committed);
        Assert.Empty(store.UnidadDeTrabajo.AuditoriasRegistradas);
    }

    [Fact]
    public async Task AgregarLineaAsync_WhenVersionIsStale_ReturnsVersionEnConflicto_AndNeverCommits()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = Borrador();
        store.UnidadDeTrabajo.ResultadoDeAgregarLinea = new ResultadoLinea(ResultadoEscritura.VersionEnConflicto, null);
        var sut = new ServicioDeAsientos(store);

        var (resultado, lineaId) = await sut.AgregarLineaAsync(501, VersionActual, LineaValida, usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.VersionEnConflicto>(resultado);
        Assert.Null(lineaId);
        Assert.False(store.UnidadDeTrabajo.Committed);
        Assert.Empty(store.UnidadDeTrabajo.AuditoriasRegistradas);
    }

    [Fact]
    public async Task ActualizarLineaAsync_WhenAsientoIsBorrador_UpdatesLinea_RegistersRepartoManualAudit_AndCommits()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = Borrador();
        var sut = new ServicioDeAsientos(store);

        var resultado = await sut.ActualizarLineaAsync(501, 42, VersionActual, LineaValida, usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        Assert.Equal(LineaValida, store.UnidadDeTrabajo.UltimaLineaActualizada);
        var auditoria = Assert.Single(store.UnidadDeTrabajo.AuditoriasRegistradas);
        Assert.Equal(EntradaAuditoria.Acciones.RepartoManual, auditoria.Accion);
        Assert.True(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task ActualizarLineaAsync_WhenLineaDoesNotExist_ReturnsNoEncontrado()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = Borrador();
        store.UnidadDeTrabajo.ResultadoDeActualizarLinea = ResultadoEscritura.NoEncontrado;
        var sut = new ServicioDeAsientos(store);

        var resultado = await sut.ActualizarLineaAsync(501, 999, VersionActual, LineaValida, usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.NoEncontrado>(resultado);
        Assert.False(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task EliminarLineaAsync_WhenAsientoIsBorrador_DeletesLinea_RegistersRepartoManualAudit_AndCommits()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = Borrador();
        var sut = new ServicioDeAsientos(store);

        var resultado = await sut.EliminarLineaAsync(501, 42, VersionActual, usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        Assert.Equal(42, store.UnidadDeTrabajo.UltimaLineaEliminadaId);
        var auditoria = Assert.Single(store.UnidadDeTrabajo.AuditoriasRegistradas);
        Assert.Equal(EntradaAuditoria.Acciones.RepartoManual, auditoria.Accion);
        Assert.True(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task EliminarLineaAsync_WhenAsientoIsConfirmado_ReturnsConflicto_EditWithoutReabrirIsBlocked()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = Confirmado();
        var sut = new ServicioDeAsientos(store);

        var resultado = await sut.EliminarLineaAsync(501, 42, VersionActual, usuarioId: 1, Ahora, CancellationToken.None);

        var conflicto = Assert.IsType<ResultadoComando.Conflicto>(resultado);
        Assert.Equal(CasoConflicto.AsientoYaConfirmado, conflicto.Caso);
        Assert.False(store.UnidadDeTrabajo.Committed);
    }

    // -----------------------------------------------------------------------------------------
    // BACKLOG #24 (tasks 3.3/3.4) — RecomponerAsync: regenera las líneas desde la factura + los
    // hechos actuales vía ReemplazarLineasAsync y escribe UNA fila REPARTO_MANUAL (design B3).
    // Gate BORRADOR + CAS de encabezado, igual que el resto de comandos de línea.
    // -----------------------------------------------------------------------------------------

    [Fact]
    public async Task RecomponerAsync_WhenAsientoIsBorrador_ReplacesLineas_WritesOneRepartoManualAudit_AndCommits()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = Borrador();
        var sut = new ServicioDeAsientos(store);

        var resultado = await sut.RecomponerAsync(
            501, VersionActual, cuentaCodigo: null, usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        Assert.NotNull(store.UnidadDeTrabajo.UltimoAsientoReemplazado);
        var auditoria = Assert.Single(store.UnidadDeTrabajo.AuditoriasRegistradas);
        Assert.Equal(EntradaAuditoria.Acciones.RepartoManual, auditoria.Accion);
        Assert.Equal(EntradaAuditoria.EntidadTipos.Asiento, auditoria.EntidadTipo);
        Assert.Equal("Cargos", auditoria.Campo);
        Assert.Null(auditoria.Motivo);
        Assert.True(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task RecomponerAsync_WhenAsientoIsConfirmado_ReturnsConflicto_AndNeverReplacesLineas()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = Confirmado();
        var sut = new ServicioDeAsientos(store);

        var resultado = await sut.RecomponerAsync(
            501, VersionActual, cuentaCodigo: null, usuarioId: 1, Ahora, CancellationToken.None);

        var conflicto = Assert.IsType<ResultadoComando.Conflicto>(resultado);
        Assert.Equal(CasoConflicto.AsientoYaConfirmado, conflicto.Caso);
        Assert.DoesNotContain(nameof(IUnidadDeTrabajo.ReemplazarLineasAsync), store.UnidadDeTrabajo.Llamadas);
        Assert.False(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task RecomponerAsync_WhenVersionIsStale_ReturnsVersionEnConflicto_AndNeverCommits()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = Borrador();
        store.UnidadDeTrabajo.ResultadoDeReemplazarLineas = ResultadoEscritura.VersionEnConflicto;
        var sut = new ServicioDeAsientos(store);

        var resultado = await sut.RecomponerAsync(
            501, new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 }, cuentaCodigo: null, usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.VersionEnConflicto>(resultado);
        Assert.Empty(store.UnidadDeTrabajo.AuditoriasRegistradas);
        Assert.False(store.UnidadDeTrabajo.Committed);
    }

    // BACKLOG #24 (design C1) — el cuentaCodigo opcional del cuerpo se resuelve contra
    // dbo.CuentaContable e inyecta en los hechos como CuentaSugerida antes de sembrar.

    [Fact]
    public async Task RecomponerAsync_WithAnExplicitCuentaCodigo_SeedsThatAccountOnTheCargoLine_NoPlaceholder()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = Borrador();
        store.UnidadDeTrabajo.CuentasContables["631111"] = new SmartNet.Catalogos.Core.CuentaContable(
            "631111", "FLETE TRASLADO DE MERCADERIA", null, "946311", "791111");
        var sut = new ServicioDeAsientos(store);

        var resultado = await sut.RecomponerAsync(
            501, VersionActual, cuentaCodigo: "631111", usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        var asiento = store.UnidadDeTrabajo.UltimoAsientoReemplazado!;
        Assert.Contains(asiento.Lineas, l =>
            l.CuentaCodigo == "631111" && l.Bloque == Bloque.Principal && l.Tipo == TipoLinea.D);
        Assert.DoesNotContain(asiento.Lineas, l => l.CuentaCodigo is null);
        Assert.True(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task RecomponerAsync_WithAnUnknownCuentaCodigo_ReturnsCorreccionInvalida_AndNeverReplacesLineas()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = Borrador();
        var sut = new ServicioDeAsientos(store);

        var resultado = await sut.RecomponerAsync(
            501, VersionActual, cuentaCodigo: "999999", usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.CorreccionInvalida>(resultado);
        Assert.DoesNotContain(nameof(IUnidadDeTrabajo.ReemplazarLineasAsync), store.UnidadDeTrabajo.Llamadas);
        Assert.False(store.UnidadDeTrabajo.Committed);
    }
}
