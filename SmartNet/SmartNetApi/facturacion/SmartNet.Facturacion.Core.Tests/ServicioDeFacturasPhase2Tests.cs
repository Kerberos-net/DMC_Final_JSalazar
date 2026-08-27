using SmartNet.Contable.Core;
using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Core.Tests;

/// <summary>
/// tasks.md Phase 2 (PR 2) — PATCH/abrir/validar-por-factura/descartar/adjuntos, todo contra el
/// fake <see cref="IUnidadDeTrabajo"/> (sin DB), verificando secuencia de comandos y el contrato de
/// auditoría de design D6 (una fila POR CAMPO cambiado; ningún comando fuera de las siete
/// <c>Accion</c> escribe auditoría).
/// </summary>
public class ServicioDeFacturasPhase2Tests
{
    private static readonly DateTimeOffset Ahora = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);
    private static readonly byte[] VersionInicial = { 0, 0, 0, 0, 0, 0, 0, 1 };

    private static FacturaPersistida FacturaPendiente() => new(
        FacturaId: 100,
        Estado: FacturaPersistida.PendienteValidacion,
        ProveedorCodigo: "P00123",
        RucProveedor: "20100000001",
        TipoComprobante: "01",
        Numero: "F001-1",
        TotalOrig: 118.00m,
        Moneda: "PEN",
        FechaEmision: new DateOnly(2026, 8, 10),
        Motivo: 5,
        Afectacion: "GRAVADA",
        Version: VersionInicial);

    // --- PatchAsync ---

    [Fact]
    public async Task PatchAsync_WhenFacturaDoesNotExist_ReturnsNoEncontrado()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = null;
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.PatchAsync(
            999, VersionInicial, new CorreccionFactura(RucProveedor: "20999999999"), usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.NoEncontrado>(resultado);
        Assert.DoesNotContain(nameof(IUnidadDeTrabajo.RegistrarAuditoriaAsync), store.UnidadDeTrabajo.Llamadas);
    }

    [Fact]
    public async Task PatchAsync_WhenVersionIsStale_ReturnsVersionEnConflicto_AndWritesNoAudit()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente();
        store.UnidadDeTrabajo.ResultadoDeGuardarFactura = ResultadoEscritura.VersionEnConflicto;
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.PatchAsync(
            100, VersionInicial, new CorreccionFactura(RucProveedor: "20999999999"), usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.VersionEnConflicto>(resultado);
        Assert.Empty(store.UnidadDeTrabajo.AuditoriasRegistradas);
        Assert.False(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task PatchAsync_ChangingTwoFields_WritesOneAuditRowPerChangedField_AndCommits()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente();
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.PatchAsync(
            100, VersionInicial,
            new CorreccionFactura(RucProveedor: "20999999999", TotalOrig: 200m),
            usuarioId: 7, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        Assert.Equal(2, store.UnidadDeTrabajo.AuditoriasRegistradas.Count);
        Assert.All(store.UnidadDeTrabajo.AuditoriasRegistradas, e =>
        {
            Assert.Equal(EntradaAuditoria.Acciones.Correccion, e.Accion);
            Assert.Equal(EntradaAuditoria.EntidadTipos.Factura, e.EntidadTipo);
        });
        Assert.Contains(store.UnidadDeTrabajo.AuditoriasRegistradas, e => e.Campo == nameof(FacturaPersistida.RucProveedor));
        Assert.Contains(store.UnidadDeTrabajo.AuditoriasRegistradas, e => e.Campo == nameof(FacturaPersistida.TotalOrig));
        Assert.True(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task PatchAsync_ResendingTheSameValue_WritesNoAuditRow()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente();
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.PatchAsync(
            100, VersionInicial, new CorreccionFactura(RucProveedor: "20100000001"), usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        Assert.Empty(store.UnidadDeTrabajo.AuditoriasRegistradas);
    }

    // --- PatchAsync -- Phase 5 (PR 5): tipoComprobante / numero become PATCH-editable (api-facturas
    // delta, tasks.md 5.1/5.3). Same audit contract as every other field (design D6). ---

    [Fact]
    public async Task PatchAsync_ChangingTipoComprobanteAndNumero_WritesOneAuditRowPerChangedField()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente(); // TipoComprobante "01", Numero "F001-1"
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.PatchAsync(
            100, VersionInicial,
            new CorreccionFactura(TipoComprobante: "07", Numero: "FC01-9"),
            usuarioId: 7, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        Assert.Equal(2, store.UnidadDeTrabajo.AuditoriasRegistradas.Count);
        Assert.Contains(store.UnidadDeTrabajo.AuditoriasRegistradas, e => e.Campo == nameof(FacturaPersistida.TipoComprobante));
        Assert.Contains(store.UnidadDeTrabajo.AuditoriasRegistradas, e => e.Campo == nameof(FacturaPersistida.Numero));
        Assert.All(store.UnidadDeTrabajo.AuditoriasRegistradas, e =>
            Assert.Equal(EntradaAuditoria.Acciones.Correccion, e.Accion));
        Assert.Equal("07", store.UnidadDeTrabajo.UltimaFacturaGuardada!.TipoComprobante);
        Assert.Equal("FC01-9", store.UnidadDeTrabajo.UltimaFacturaGuardada.Numero);
    }

    [Fact]
    public async Task PatchAsync_ResendingTheSameTipoComprobante_WritesNoAuditRow()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente();
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.PatchAsync(
            100, VersionInicial, new CorreccionFactura(TipoComprobante: "01"), usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        Assert.Empty(store.UnidadDeTrabajo.AuditoriasRegistradas);
    }

    [Fact]
    public async Task PatchAsync_WithAnInvalidTipoComprobante_ReturnsCorreccionInvalida_AndNeverSavesOrCommits()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente();
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.PatchAsync(
            100, VersionInicial, new CorreccionFactura(TipoComprobante: "99"), usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.CorreccionInvalida>(resultado);
        Assert.DoesNotContain(nameof(IUnidadDeTrabajo.GuardarFacturaAsync), store.UnidadDeTrabajo.Llamadas);
        Assert.False(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task PatchAsync_WithABlankNumero_ReturnsCorreccionInvalida_AndNeverSavesOrCommits()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente();
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.PatchAsync(
            100, VersionInicial, new CorreccionFactura(Numero: "  "), usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.CorreccionInvalida>(resultado);
        Assert.DoesNotContain(nameof(IUnidadDeTrabajo.GuardarFacturaAsync), store.UnidadDeTrabajo.Llamadas);
        Assert.False(store.UnidadDeTrabajo.Committed);
    }

    // --- AbrirAsync ---

    [Fact]
    public async Task AbrirAsync_WhenFacturaDoesNotExist_ReturnsNoEncontrado()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = null;
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.AbrirAsync(999, CancellationToken.None);

        Assert.IsType<ResultadoComando.NoEncontrado>(resultado);
    }

    [Fact]
    public async Task AbrirAsync_WhenNoAsientoVigenteExists_CreatesOneAndCommits()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente();
        store.UnidadDeTrabajo.AsientoVigenteId = null;
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.AbrirAsync(100, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        Assert.Contains(nameof(IUnidadDeTrabajo.CrearAsientoBorradorAsync), store.UnidadDeTrabajo.Llamadas);
        Assert.True(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task AbrirAsync_WhenAnAsientoVigenteAlreadyExists_IsIdempotent_AndNeverCreatesASecondOne()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente();
        store.UnidadDeTrabajo.AsientoVigenteId = 501;
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.AbrirAsync(100, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        Assert.DoesNotContain(nameof(IUnidadDeTrabajo.CrearAsientoBorradorAsync), store.UnidadDeTrabajo.Llamadas);
    }

    // --- AbrirAsync -- Phase 5 (PR 5): spec.md "Opening a factura with no tipo de cambio
    // (foreign currency)" -- verify-report.md CRITICAL finding, HechosDeConflicto/deviation 4-8. ---

    [Fact]
    public async Task AbrirAsync_ForeignCurrencyWithNoTipoCambio_ReturnsConflicto_AndNeverCreatesAnAsiento()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente() with { Moneda = "USD" };
        store.UnidadDeTrabajo.AsientoVigenteId = null;
        store.UnidadDeTrabajo.TipoCambioVigente = false;
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.AbrirAsync(100, CancellationToken.None);

        var conflicto = Assert.IsType<ResultadoComando.Conflicto>(resultado);
        Assert.Equal(CasoConflicto.SinTipoCambio, conflicto.Caso);
        Assert.DoesNotContain(nameof(IUnidadDeTrabajo.CrearAsientoBorradorAsync), store.UnidadDeTrabajo.Llamadas);
        Assert.False(store.UnidadDeTrabajo.Committed);
        Assert.Equal(new DateOnly(2026, 8, 10), store.UnidadDeTrabajo.UltimaFechaConsultadaTipoCambio);
    }

    [Fact]
    public async Task AbrirAsync_ForeignCurrencyWithATipoCambio_CreatesTheAsientoNormally()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente() with { Moneda = "USD" };
        store.UnidadDeTrabajo.AsientoVigenteId = null;
        store.UnidadDeTrabajo.TipoCambioVigente = true;
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.AbrirAsync(100, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        Assert.Contains(nameof(IUnidadDeTrabajo.CrearAsientoBorradorAsync), store.UnidadDeTrabajo.Llamadas);
        Assert.True(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task AbrirAsync_LocalCurrency_NeverConsultsTipoCambio()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente(); // Moneda: "PEN"
        store.UnidadDeTrabajo.AsientoVigenteId = null;
        store.UnidadDeTrabajo.TipoCambioVigente = false;
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.AbrirAsync(100, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        Assert.DoesNotContain(nameof(IUnidadDeTrabajo.ExisteTipoCambioVigenteAsync), store.UnidadDeTrabajo.Llamadas);
        Assert.Contains(nameof(IUnidadDeTrabajo.CrearAsientoBorradorAsync), store.UnidadDeTrabajo.Llamadas);
    }

    [Fact]
    public async Task AbrirAsync_ForeignCurrencyWithNoTipoCambio_ButAsientoAlreadyExists_StaysIdempotent()
    {
        // Idempotency (existing scenario, PR 2) wins over the D4 gate -- abrir never fails on a
        // factura that already has a vigente asiento, regardless of Moneda/tipo de cambio.
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente() with { Moneda = "USD" };
        store.UnidadDeTrabajo.AsientoVigenteId = 501;
        store.UnidadDeTrabajo.TipoCambioVigente = false;
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.AbrirAsync(100, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
    }

    // --- ValidarPorFacturaAsync ---

    [Fact]
    public async Task ValidarPorFacturaAsync_WhenNoAsientoVigenteExists_ReturnsNoEncontrado()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoVigenteId = null;
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.ValidarPorFacturaAsync(
            100, new DateOnly(2026, 8, 1), Ahora, usuarioId: 1, CancellationToken.None);

        Assert.IsType<ResultadoComando.NoEncontrado>(resultado);
        Assert.DoesNotContain(nameof(IUnidadDeTrabajo.CargarAsientoAsync), store.UnidadDeTrabajo.Llamadas);
    }

    [Fact]
    public async Task ValidarPorFacturaAsync_ResolvesTheAsientoId_ThenRunsTheSameEngineAsValidarAsync()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoVigenteId = 501;
        store.UnidadDeTrabajo.AsientoACargar = new AsientoPersistido(
            AsientoContableId: 501,
            FacturaId: 100,
            Estado: AsientoPersistido.Borrador,
            NumeroAsiento: null,
            Version: VersionInicial,
            Asiento: new AsientoContable(
                ProveedorCodigo: "P00123",
                FechaContable: new DateOnly(2026, 8, 10),
                MotivoDescripcion: "Compra",
                TipoCambioVenta: null,
                BasePEN: 100m,
                IgvPEN: 18m,
                NetoPEN: 118m,
                AfectacionCongelada: Afectacion.Gravada,
                Comprobante: TipoComprobante.Factura,
                Lineas: new[]
                {
                    new LineaAsiento(1, Bloque.Principal, TipoLinea.D, 100m, 0m, "639915", null, null, null),
                    new LineaAsiento(2, Bloque.Principal, TipoLinea.D, 18m, 0m, "401111", null, null, null),
                    new LineaAsiento(3, Bloque.Principal, TipoLinea.H, 0m, 118m, "421001", null, null, null),
                }),
            Hechos: HechosDeConflicto.Ninguno);
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.ValidarPorFacturaAsync(
            100, new DateOnly(2026, 8, 1), Ahora, usuarioId: 1, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        // outbox-mensajeria (BACKLOG #14, design D9/D10) -- misma extensión de ValidarInternoAsync
        // que ValidarAsync_..._CommitsInOrder (ServicioDeFacturasTests.cs).
        Assert.Equal(
            new[]
            {
                nameof(IUnidadDeTrabajo.ObtenerAsientoVigenteIdAsync),
                nameof(IUnidadDeTrabajo.CargarAsientoAsync),
                nameof(IUnidadDeTrabajo.AsignarCorrelativoAsync),
                nameof(IUnidadDeTrabajo.GuardarAsientoAsync),
                nameof(IUnidadDeTrabajo.MarcarFacturaValidadaAsync),
                nameof(IUnidadDeTrabajo.CargarFacturaAsync),
                nameof(IUnidadDeTrabajo.CargarAsientoAsync),
                nameof(IUnidadDeTrabajo.CargarLineasPersistidasAsync),
                nameof(IUnidadDeTrabajo.CargarDocumentosFacturaAsync),
                nameof(IUnidadDeTrabajo.CargarAdjuntosDeFacturaAsync),
                nameof(IUnidadDeTrabajo.EmitirOutboxAsync),
                nameof(IUnidadDeTrabajo.CommitAsync),
            },
            store.UnidadDeTrabajo.Llamadas);
    }

    // --- DescartarAsync ---

    [Fact]
    public async Task DescartarAsync_WhenFacturaIsPendiente_MarksItDescartada_WithNoAudit()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente();
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.DescartarAsync(100, VersionInicial, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        Assert.Equal(FacturaPersistida.Descartada, store.UnidadDeTrabajo.UltimaFacturaGuardada!.Estado);
        Assert.Empty(store.UnidadDeTrabajo.AuditoriasRegistradas);
    }

    [Fact]
    public async Task DescartarAsync_WhenFacturaAlreadyValidada_ReturnsConflicto()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente() with { Estado = FacturaPersistida.Validada };
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.DescartarAsync(100, VersionInicial, CancellationToken.None);

        var conflicto = Assert.IsType<ResultadoComando.Conflicto>(resultado);
        Assert.Equal(CasoConflicto.AsientoYaConfirmado, conflicto.Caso);
        Assert.DoesNotContain(nameof(IUnidadDeTrabajo.GuardarFacturaAsync), store.UnidadDeTrabajo.Llamadas);
    }

    // --- diseno-visual-spa-item-12 (design D10): ConfirmarAfectacionAsync — gate stays dormant,
    // this only writes the column + the existing CONFIRMACION_AFECTACION audit action. ---

    [Fact]
    public async Task ConfirmarAfectacionAsync_WhenFacturaDoesNotExist_ReturnsNoEncontrado()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = null;
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.ConfirmarAfectacionAsync(999, VersionInicial, esMixta: false, usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.NoEncontrado>(resultado);
        Assert.Empty(store.UnidadDeTrabajo.AuditoriasRegistradas);
    }

    [Fact]
    public async Task ConfirmarAfectacionAsync_WhenVersionIsStale_ReturnsVersionEnConflicto_AndWritesNoAudit()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente();
        store.UnidadDeTrabajo.ResultadoDeConfirmarAfectacion = ResultadoEscritura.VersionEnConflicto;
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.ConfirmarAfectacionAsync(100, VersionInicial, esMixta: false, usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.VersionEnConflicto>(resultado);
        Assert.Empty(store.UnidadDeTrabajo.AuditoriasRegistradas);
        Assert.False(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task ConfirmarAfectacionAsync_WhenApplied_WritesConfirmacionAfectacionAudit_AndCommits()
    {
        var store = new FakeFacturacionStore();
        // outbox-mensajeria (BACKLOG #14, design D8): FACTURA_CORREGIDA requiere factura VALIDADA
        // (spec.md "on any accepted update to a validated invoice") -- extendido desde
        // FacturaPendiente() para ejercer el emission gate junto con el cambio de AfectacionMixta.
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente() with { Estado = FacturaPersistida.Validada };
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.ConfirmarAfectacionAsync(100, VersionInicial, esMixta: true, usuarioId: 7, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        var entrada = Assert.Single(store.UnidadDeTrabajo.AuditoriasRegistradas);
        Assert.Equal(EntradaAuditoria.Acciones.ConfirmacionAfectacion, entrada.Accion);
        Assert.Equal(EntradaAuditoria.EntidadTipos.Factura, entrada.EntidadTipo);
        Assert.Equal(100, entrada.EntidadId);
        Assert.Equal("True", entrada.ValorNuevo);
        Assert.Equal(7, entrada.UsuarioId);
        Assert.True(store.UnidadDeTrabajo.Committed);
        var evento = Assert.Single(store.UnidadDeTrabajo.EventosOutbox);
        Assert.Equal("FACTURA_CORREGIDA", evento.Tipo);
        Assert.Equal(100, evento.FacturaId);
        // outbox-mensajeria (BACKLOG #14, design D9) -- +4 reads del re-read de PayloadOutbox
        // (D2: CargarFacturaAsync, ObtenerAsientoVigenteIdAsync, CargarDocumentosFacturaAsync,
        // CargarAdjuntosDeFacturaAsync -- AsientoVigenteId es null por defecto, así que
        // CargarAsientoAsync/CargarLineasPersistidasAsync no corren) + EmitirOutboxAsync.
        Assert.Equal(
            new[]
            {
                nameof(IUnidadDeTrabajo.CargarFacturaAsync),
                "ConfirmarAfectacionAsync",
                nameof(IUnidadDeTrabajo.RegistrarAuditoriaAsync),
                nameof(IUnidadDeTrabajo.CargarFacturaAsync),
                nameof(IUnidadDeTrabajo.ObtenerAsientoVigenteIdAsync),
                nameof(IUnidadDeTrabajo.CargarDocumentosFacturaAsync),
                nameof(IUnidadDeTrabajo.CargarAdjuntosDeFacturaAsync),
                nameof(IUnidadDeTrabajo.EmitirOutboxAsync),
                nameof(IUnidadDeTrabajo.CommitAsync),
            },
            store.UnidadDeTrabajo.Llamadas);
    }

    // --- Adjuntos ---

    [Fact]
    public async Task RegistrarAdjuntoAsync_OnAPendienteFactura_WritesNoAudit_AndEmitsNoOutbox()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente();
        var sut = new ServicioDeFacturas(store);
        var adjunto = new AdjuntoManual(0, 100, "f.pdf", "/f.pdf", "application/pdf", 10, 1, Ahora, null);

        var resultado = await sut.RegistrarAdjuntoAsync(100, adjunto, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        Assert.Empty(store.UnidadDeTrabajo.AuditoriasRegistradas);
        Assert.Empty(store.UnidadDeTrabajo.EventosOutbox);
    }

    [Fact]
    public async Task RegistrarAdjuntoAsync_OnAValidadaFactura_EmitsDocumentacionActualizada()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente() with { Estado = FacturaPersistida.Validada };
        var sut = new ServicioDeFacturas(store);
        var adjunto = new AdjuntoManual(0, 100, "f.pdf", "/f.pdf", "application/pdf", 10, 1, Ahora, null);

        var resultado = await sut.RegistrarAdjuntoAsync(100, adjunto, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        var evento = Assert.Single(store.UnidadDeTrabajo.EventosOutbox);
        Assert.Equal("DOCUMENTACION_ACTUALIZADA", evento.Tipo);
    }

    // outbox-mensajeria (BACKLOG #14, tasks.md 2.7) -- "production-shaped guard test": Estado
    // nunca se fija a mano, se produce por una validación real (ValidarAsync -> D10
    // MarcarFacturaValidadaAsync) sobre el MISMO fake store (FakeFacturacionStore.AbrirAsync
    // siempre devuelve la misma FakeUnidadDeTrabajo), tal como en producción una factura ya
    // VALIDADA persiste su Estado entre transacciones.
    [Fact]
    public async Task RegistrarAdjuntoAsync_AfterARealValidarAsync_EmitsDocumentacionActualizada_NoHandSetEstado()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = new AsientoPersistido(
            AsientoContableId: 501,
            FacturaId: 100,
            Estado: AsientoPersistido.Borrador,
            NumeroAsiento: null,
            Version: VersionInicial,
            Asiento: new AsientoContable(
                ProveedorCodigo: "P00123", FechaContable: new DateOnly(2026, 8, 10),
                MotivoDescripcion: "Compra", TipoCambioVenta: null, BasePEN: 100m, IgvPEN: 18m,
                NetoPEN: 118m, AfectacionCongelada: Afectacion.Gravada, Comprobante: TipoComprobante.Factura,
                Lineas: new[]
                {
                    new LineaAsiento(1, Bloque.Principal, TipoLinea.D, 100m, 0m, "639915", null, null, null),
                    new LineaAsiento(2, Bloque.Principal, TipoLinea.D, 18m, 0m, "401111", null, null, null),
                    new LineaAsiento(3, Bloque.Principal, TipoLinea.H, 0m, 118m, "421001", null, null, null),
                }),
            Hechos: HechosDeConflicto.Ninguno);
        var sut = new ServicioDeFacturas(store);

        var resultadoValidar = await sut.ValidarAsync(501, new DateOnly(2026, 8, 1), Ahora, usuarioId: 1, CancellationToken.None);
        Assert.IsType<ResultadoComando.Aplicado>(resultadoValidar);

        var adjunto = new AdjuntoManual(0, 100, "f.pdf", "/f.pdf", "application/pdf", 10, 1, Ahora, null);
        var resultadoAdjunto = await sut.RegistrarAdjuntoAsync(100, adjunto, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultadoAdjunto);
        Assert.Contains(store.UnidadDeTrabajo.EventosOutbox, e => e.Tipo == "DOCUMENTACION_ACTUALIZADA");
    }

    [Fact]
    public async Task PatchAsync_AfterARealValidarAsync_EmitsFacturaCorregida_NoHandSetEstado()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = new AsientoPersistido(
            AsientoContableId: 501,
            FacturaId: 100,
            Estado: AsientoPersistido.Borrador,
            NumeroAsiento: null,
            Version: VersionInicial,
            Asiento: new AsientoContable(
                ProveedorCodigo: "P00123", FechaContable: new DateOnly(2026, 8, 10),
                MotivoDescripcion: "Compra", TipoCambioVenta: null, BasePEN: 100m, IgvPEN: 18m,
                NetoPEN: 118m, AfectacionCongelada: Afectacion.Gravada, Comprobante: TipoComprobante.Factura,
                Lineas: new[]
                {
                    new LineaAsiento(1, Bloque.Principal, TipoLinea.D, 100m, 0m, "639915", null, null, null),
                    new LineaAsiento(2, Bloque.Principal, TipoLinea.D, 18m, 0m, "401111", null, null, null),
                    new LineaAsiento(3, Bloque.Principal, TipoLinea.H, 0m, 118m, "421001", null, null, null),
                }),
            Hechos: HechosDeConflicto.Ninguno);
        var sut = new ServicioDeFacturas(store);

        var resultadoValidar = await sut.ValidarAsync(501, new DateOnly(2026, 8, 1), Ahora, usuarioId: 1, CancellationToken.None);
        Assert.IsType<ResultadoComando.Aplicado>(resultadoValidar);

        var resultadoPatch = await sut.PatchAsync(
            100, VersionInicial, new CorreccionFactura(RucProveedor: "20999999999"), usuarioId: 1, Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultadoPatch);
        Assert.Contains(store.UnidadDeTrabajo.EventosOutbox, e => e.Tipo == "FACTURA_CORREGIDA");
    }

    [Fact]
    public async Task EliminarAdjuntoAsync_WhenAdjuntoDoesNotExist_ReturnsNoEncontrado()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente();
        store.UnidadDeTrabajo.ResultadoDeEliminarAdjunto = ResultadoEscritura.NoEncontrado;
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.EliminarAdjuntoAsync(100, 700, usuarioId: 1, "motivo", Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.NoEncontrado>(resultado);
        Assert.Empty(store.UnidadDeTrabajo.AuditoriasRegistradas);
    }

    [Fact]
    public async Task EliminarAdjuntoAsync_OnSuccess_AlwaysWritesEliminacionAdjuntoAudit()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.FacturaACargar = FacturaPendiente();
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.EliminarAdjuntoAsync(100, 700, usuarioId: 3, "Motivo requerido", Ahora, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        var entrada = Assert.Single(store.UnidadDeTrabajo.AuditoriasRegistradas);
        Assert.Equal(EntradaAuditoria.Acciones.EliminacionAdjunto, entrada.Accion);
        Assert.Equal(EntradaAuditoria.EntidadTipos.Adjunto, entrada.EntidadTipo);
        Assert.Equal("Motivo requerido", entrada.Motivo);
    }
}
