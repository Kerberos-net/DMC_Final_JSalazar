using SmartNet.Contable.Core;
using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Core.Tests;

/// <summary>
/// tasks.md 1.5/1.6 — <see cref="ServicioDeFacturas.ValidarAsync"/> is design D5 ("One transaction
/// for validar = ADR 0006 'confirmar'") driven entirely through the fake <see cref="IUnidadDeTrabajo"/>:
/// no DB. Verifies the exact command SEQUENCE, and the D3/obs#138 remap of
/// InvarianteContable.FechaAnteriorAlCorte/ProveedorVarios (Global 3/4) from 422 to 409.
/// </summary>
public class ServicioDeFacturasTests
{
    private static readonly DateOnly FechaCorte = new(2026, 8, 1);
    private static readonly DateTimeOffset Ahora = new(2026, 8, 21, 10, 0, 0, TimeSpan.Zero);

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
        Lineas: new[]
        {
            new LineaAsiento(1, Bloque.Principal, TipoLinea.D, 100m, 0m, "639915", null, null, null),
            new LineaAsiento(2, Bloque.Principal, TipoLinea.D, 18m, 0m, "401111", null, null, null),
            new LineaAsiento(3, Bloque.Principal, TipoLinea.H, 0m, 118m, "421001", null, null, null),
        });

    private static AsientoPersistido AsientoPersistidoBorrador(
        AsientoContable? asiento = null, HechosDeConflicto? hechos = null) =>
        new(
            AsientoContableId: 501,
            FacturaId: 100,
            Estado: AsientoPersistido.Borrador,
            NumeroAsiento: null,
            Version: new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 },
            Asiento: asiento ?? AsientoValido(),
            Hechos: hechos ?? HechosDeConflicto.Ninguno);

    [Fact]
    public async Task ValidarAsync_WhenAsientoDoesNotExist_ReturnsNoEncontrado_AndNeverAssignsCorrelativo()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = null;
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.ValidarAsync(999, FechaCorte, Ahora, usuarioId: 1, CancellationToken.None);

        Assert.IsType<ResultadoComando.NoEncontrado>(resultado);
        Assert.DoesNotContain(nameof(IUnidadDeTrabajo.AsignarCorrelativoAsync), store.UnidadDeTrabajo.Llamadas);
        Assert.False(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task ValidarAsync_WhenAsientoAlreadyConfirmado_ReturnsConflicto_AndNeverAssignsCorrelativo()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = AsientoPersistidoBorrador() with { Estado = AsientoPersistido.Confirmado };
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.ValidarAsync(501, FechaCorte, Ahora, usuarioId: 1, CancellationToken.None);

        var conflicto = Assert.IsType<ResultadoComando.Conflicto>(resultado);
        Assert.Equal(CasoConflicto.AsientoYaConfirmado, conflicto.Caso);
        Assert.DoesNotContain(nameof(IUnidadDeTrabajo.AsignarCorrelativoAsync), store.UnidadDeTrabajo.Llamadas);
    }

    [Fact]
    public async Task ValidarAsync_WhenDuplicadoNoResuelto_ReturnsConflicto_BeforeEvaluatingInvariantes()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = AsientoPersistidoBorrador(
            hechos: HechosDeConflicto.Ninguno with { DuplicadoNoResuelto = true });
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.ValidarAsync(501, FechaCorte, Ahora, usuarioId: 1, CancellationToken.None);

        var conflicto = Assert.IsType<ResultadoComando.Conflicto>(resultado);
        Assert.Equal(CasoConflicto.DuplicadoNoResuelto, conflicto.Caso);
        Assert.DoesNotContain(nameof(IUnidadDeTrabajo.AsignarCorrelativoAsync), store.UnidadDeTrabajo.Llamadas);
        Assert.False(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task ValidarAsync_WhenLineaSinCuenta_ReturnsInvariantesIncumplidas_AndNeverCommits()
    {
        var asientoDescuadrado = AsientoValido() with
        {
            Lineas = new[] { new LineaAsiento(1, Bloque.Principal, TipoLinea.D, 100m, 0m, null, null, null, null) },
        };
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = AsientoPersistidoBorrador(asientoDescuadrado);
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.ValidarAsync(501, FechaCorte, Ahora, usuarioId: 1, CancellationToken.None);

        var incumplidas = Assert.IsType<ResultadoComando.InvariantesIncumplidas>(resultado);
        Assert.Contains(incumplidas.Fallos, f => f.Invariante == InvarianteContable.LineaSinCuenta);
        Assert.False(store.UnidadDeTrabajo.Committed);
    }

    [Fact]
    public async Task ValidarAsync_WhenFechaContableAnteriorAlCorte_RemapsGlobal3To409_NotTo422()
    {
        var asientoTardio = AsientoValido() with { FechaContable = new DateOnly(2026, 7, 1) };
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = AsientoPersistidoBorrador(asientoTardio);
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.ValidarAsync(501, FechaCorte, Ahora, usuarioId: 1, CancellationToken.None);

        // obs #138 -- decisión ratificada del dueño del producto: Global 3 va a 409, no a 422.
        var conflicto = Assert.IsType<ResultadoComando.Conflicto>(resultado);
        Assert.Equal(CasoConflicto.FechaAnteriorAlCorte, conflicto.Caso);
    }

    [Fact]
    public async Task ValidarAsync_WhenProveedorVarios_RemapsGlobal4To409_NotTo422()
    {
        var asientoGenerico = AsientoValido() with { ProveedorCodigo = "P00000" };
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = AsientoPersistidoBorrador(asientoGenerico);
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.ValidarAsync(501, FechaCorte, Ahora, usuarioId: 1, CancellationToken.None);

        var conflicto = Assert.IsType<ResultadoComando.Conflicto>(resultado);
        Assert.Equal(CasoConflicto.ProveedorGenericoNoResuelto, conflicto.Caso);
    }

    [Fact]
    public async Task ValidarAsync_WhenEverythingPasses_AssignsCorrelativo_SavesConfirmado_EmitsOutbox_CommitsInOrder()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = AsientoPersistidoBorrador();
        store.UnidadDeTrabajo.CorrelativoAAsignar = 7;
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.ValidarAsync(501, FechaCorte, Ahora, usuarioId: 1, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        // outbox-mensajeria (BACKLOG #14, design D9/D10) -- extendido: MarcarFacturaValidadaAsync
        // (D10, state-CAS) + el re-read de PayloadOutbox.ConstruirAsync (D2: CargarFacturaAsync,
        // CargarAsientoAsync, CargarLineasPersistidasAsync, CargarDocumentosFacturaAsync,
        // CargarAdjuntosDeFacturaAsync) se insertan entre GuardarAsientoAsync y EmitirOutboxAsync.
        Assert.Equal(
            new[]
            {
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
        Assert.Equal("FACTURA_VALIDADA", store.UnidadDeTrabajo.EventosOutbox[0].Tipo);
        Assert.Equal("02-2026-08-000007", store.UnidadDeTrabajo.UltimoAsientoGuardado!.NumeroAsiento);
        Assert.Equal(AsientoPersistido.Confirmado, store.UnidadDeTrabajo.UltimoAsientoGuardado!.Estado);
        Assert.True(store.UnidadDeTrabajo.Committed);
        Assert.True(store.UnidadDeTrabajo.Disposed);
    }

    [Fact]
    public async Task ValidarAsync_WhenGuardarDetectsStaleVersion_ReturnsVersionEnConflicto_AndNeverCommits()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = AsientoPersistidoBorrador();
        store.UnidadDeTrabajo.ResultadoDeGuardar = ResultadoEscritura.VersionEnConflicto;
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.ValidarAsync(501, FechaCorte, Ahora, usuarioId: 1, CancellationToken.None);

        Assert.IsType<ResultadoComando.VersionEnConflicto>(resultado);
        Assert.False(store.UnidadDeTrabajo.Committed);
    }

    // outbox-mensajeria (BACKLOG #14, tasks.md 2.2/2.3, design D10/OQ5/D1) ---------------------

    [Fact]
    public async Task ValidarAsync_WhenFacturaIsDescartada_ReturnsConflicto409_RollsBack_NoOutboxEmitted()
    {
        var store = new FakeFacturacionStore();
        store.UnidadDeTrabajo.AsientoACargar = AsientoPersistidoBorrador();
        store.UnidadDeTrabajo.FacturaACargar = store.UnidadDeTrabajo.FacturaACargar! with { Estado = FacturaPersistida.Descartada };
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.ValidarAsync(501, FechaCorte, Ahora, usuarioId: 1, CancellationToken.None);

        var conflicto = Assert.IsType<ResultadoComando.Conflicto>(resultado);
        Assert.Equal(CasoConflicto.FacturaDescartada, conflicto.Caso);
        Assert.False(store.UnidadDeTrabajo.Committed);
        Assert.Empty(store.UnidadDeTrabajo.EventosOutbox);
        Assert.DoesNotContain(nameof(IUnidadDeTrabajo.EmitirOutboxAsync), store.UnidadDeTrabajo.Llamadas);
        // el asiento CONFIRMADO nunca se persiste como resultado final -- await using uow rueda
        // atrás porque CommitAsync nunca se llamó.
        Assert.Contains(nameof(IUnidadDeTrabajo.MarcarFacturaValidadaAsync), store.UnidadDeTrabajo.Llamadas);
    }

    [Fact]
    public async Task ValidarAsync_ReconfirmingAfterReopen_EmitsAsientoCorregido_NotFacturaValidada_NoRollback()
    {
        var store = new FakeFacturacionStore();
        // D1: NumeroAsiento ya presente antes de la escritura de validar -- "reconfirmación tras
        // reapertura" (ReabrirAsync no lo limpia). D10: la factura ya quedó VALIDADA por la
        // primera validación -- MarcarFacturaValidadaAsync debe devolver YaValidada, no error.
        store.UnidadDeTrabajo.AsientoACargar = AsientoPersistidoBorrador() with { NumeroAsiento = "02-2026-08-000001" };
        store.UnidadDeTrabajo.FacturaACargar = store.UnidadDeTrabajo.FacturaACargar! with { Estado = FacturaPersistida.Validada };
        var sut = new ServicioDeFacturas(store);

        var resultado = await sut.ValidarAsync(501, FechaCorte, Ahora, usuarioId: 1, CancellationToken.None);

        Assert.IsType<ResultadoComando.Aplicado>(resultado);
        Assert.True(store.UnidadDeTrabajo.Committed);
        var evento = Assert.Single(store.UnidadDeTrabajo.EventosOutbox);
        Assert.Equal("ASIENTO_CORREGIDO", evento.Tipo);
    }
}
