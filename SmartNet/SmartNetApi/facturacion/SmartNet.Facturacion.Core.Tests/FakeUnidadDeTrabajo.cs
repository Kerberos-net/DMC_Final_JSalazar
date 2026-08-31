using SmartNet.Contable.Core;
using SmartNet.Facturacion.Core;

namespace SmartNet.Facturacion.Core.Tests;

/// <summary>
/// tasks.md 1.5 — doble de prueba de <see cref="IUnidadDeTrabajo"/>: sin DB, en memoria, registra
/// cada llamada en <see cref="Llamadas"/> en orden para que las pruebas de
/// <see cref="ServicioDeFacturas"/>/<see cref="ServicioDeAsientos"/> verifiquen la SECUENCIA exacta
/// de comandos (design D5: CAS -&gt; gate -&gt; invariantes -&gt; correlativo -&gt; auditoría -&gt;
/// outbox -&gt; commit), no solo el resultado final.
/// </summary>
public sealed class FakeUnidadDeTrabajo : IUnidadDeTrabajo
{
    public List<string> Llamadas { get; } = new();
    public bool Disposed { get; private set; }
    public bool Committed { get; private set; }

    public AsientoPersistido? AsientoACargar { get; set; }
    public ResultadoEscritura ResultadoDeGuardar { get; set; } = ResultadoEscritura.Aplicado;
    public int CorrelativoAAsignar { get; set; } = 1;
    public List<EntradaAuditoria> AuditoriasRegistradas { get; } = new();
    public List<(string Tipo, long FacturaId, string Payload)> EventosOutbox { get; } = new();
    public AsientoPersistido? UltimoAsientoGuardado { get; private set; }

    public Task<AsientoPersistido?> CargarAsientoAsync(long asientoId, CancellationToken ct)
    {
        Llamadas.Add(nameof(CargarAsientoAsync));
        // design D9 fidelity fix: "la transacción ve sus propias escrituras" -- una vez que
        // GuardarAsientoAsync corrió en esta misma unidad de trabajo, la siguiente lectura debe
        // reflejar lo escrito (p.ej. el PayloadOutbox re-read de D2), no el estado pre-escritura.
        return Task.FromResult(UltimoAsientoGuardado ?? AsientoACargar);
    }

    public Task<ResultadoEscritura> GuardarAsientoAsync(
        long id, byte[] versionEsperada, AsientoPersistido asiento, CancellationToken ct)
    {
        Llamadas.Add(nameof(GuardarAsientoAsync));
        UltimoAsientoGuardado = asiento;
        return Task.FromResult(ResultadoDeGuardar);
    }

    public Task<int> AsignarCorrelativoAsync(short anio, byte mes, string origen, CancellationToken ct)
    {
        Llamadas.Add(nameof(AsignarCorrelativoAsync));
        return Task.FromResult(CorrelativoAAsignar);
    }

    public Task RegistrarAuditoriaAsync(EntradaAuditoria entrada, CancellationToken ct)
    {
        Llamadas.Add(nameof(RegistrarAuditoriaAsync));
        AuditoriasRegistradas.Add(entrada);
        return Task.CompletedTask;
    }

    private readonly HashSet<(string Tipo, long FacturaId)> _emitidosEnEstaTx = new();

    public Task EmitirOutboxAsync(string tipo, long facturaId, string payload, CancellationToken ct)
    {
        Llamadas.Add(nameof(EmitirOutboxAsync));

        // design D8 -- a lo sumo un evento por (Tipo, FacturaId) por transacción; fail-loud en vez
        // de un dedupe silencioso que escondería el error de diseño (mismo mirror que
        // SqlUnidadDeTrabajo.EmitirOutboxAsync, tasks.md 2.8).
        if (!_emitidosEnEstaTx.Add((tipo, facturaId)))
        {
            throw new InvalidOperationException(
                $"Ya se emitió un evento '{tipo}' para la factura {facturaId} en esta transacción.");
        }

        EventosOutbox.Add((tipo, facturaId, payload));
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken ct)
    {
        Llamadas.Add(nameof(CommitAsync));
        Committed = true;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }

    // --- PR 2 additions ---

    // design D9 fidelity fix -- default no-nulo: los tests de ValidarAsync/ValidarPorFacturaAsync
    // (ServicioDeFacturasTests/ServicioDeFacturasPhase2Tests) solo fijan AsientoACargar, nunca
    // FacturaACargar, y ahora PayloadOutbox.ConstruirAsync SIEMPRE re-lee la factura (D2). FacturaId
    // 100 coincide con AsientoPersistidoBorrador().FacturaId de ambos archivos de test. Los tests
    // que necesitan "factura ausente" ya fijan FacturaACargar = null explícitamente.
    public FacturaPersistida? FacturaACargar { get; set; } = new FacturaPersistida(
        FacturaId: 100,
        Estado: FacturaPersistida.PendienteValidacion,
        ProveedorCodigo: "P00123",
        RucProveedor: "20100000001",
        TipoComprobante: "01",
        Numero: "F001-1",
        TotalOrig: 118.00m,
        Moneda: "PEN",
        FechaEmision: new DateOnly(2026, 8, 10),
        Motivo: null,
        Afectacion: "GRAVADA",
        Version: new byte[] { 0, 0, 0, 0, 0, 0, 0, 1 });
    public ResultadoEscritura ResultadoDeGuardarFactura { get; set; } = ResultadoEscritura.Aplicado;
    public long? AsientoVigenteId { get; set; }
    public long AsientoBorradorCreadoId { get; set; } = 900;
    public ResultadoEscritura ResultadoDeEliminarAdjunto { get; set; } = ResultadoEscritura.Aplicado;
    public long AdjuntoRegistradoId { get; set; } = 700;
    public FacturaPersistida? UltimaFacturaGuardada { get; private set; }
    public List<AdjuntoManual> AdjuntosRegistrados { get; } = new();

    public Task<FacturaPersistida?> CargarFacturaAsync(long facturaId, CancellationToken ct)
    {
        Llamadas.Add(nameof(CargarFacturaAsync));
        // design D9 fidelity fix -- misma razón que CargarAsientoAsync: la transacción ve sus
        // propias escrituras (GuardarFacturaAsync o MarcarFacturaValidadaAsync, que muta
        // FacturaACargar directamente porque no pasa por GuardarFacturaAsync -- ver más abajo).
        return Task.FromResult(UltimaFacturaGuardada ?? FacturaACargar);
    }

    public Task<ResultadoEscritura> GuardarFacturaAsync(
        long id, byte[] versionEsperada, FacturaPersistida factura, CancellationToken ct)
    {
        Llamadas.Add(nameof(GuardarFacturaAsync));
        UltimaFacturaGuardada = factura;
        return Task.FromResult(ResultadoDeGuardarFactura);
    }

    public Task<long?> ObtenerAsientoVigenteIdAsync(long facturaId, CancellationToken ct)
    {
        Llamadas.Add(nameof(ObtenerAsientoVigenteIdAsync));
        return Task.FromResult(AsientoVigenteId);
    }

    public Task<long> CrearAsientoBorradorAsync(
        long facturaId, string proveedorCodigo, DateOnly fechaContable, CancellationToken ct)
    {
        Llamadas.Add(nameof(CrearAsientoBorradorAsync));
        return Task.FromResult(AsientoBorradorCreadoId);
    }

    public Task<long> RegistrarAdjuntoAsync(AdjuntoManual adjunto, CancellationToken ct)
    {
        Llamadas.Add(nameof(RegistrarAdjuntoAsync));
        AdjuntosRegistrados.Add(adjunto);
        return Task.FromResult(AdjuntoRegistradoId);
    }

    public Task<ResultadoEscritura> EliminarAdjuntoAsync(
        long adjuntoManualId, long facturaId, DateTimeOffset eliminadoEn, long eliminadoPorUsuarioId,
        string motivoEliminacion, CancellationToken ct)
    {
        Llamadas.Add(nameof(EliminarAdjuntoAsync));
        return Task.FromResult(ResultadoDeEliminarAdjunto);
    }

    // --- PR 3 (Phase 3) additions: líneas por LineaId ---

    public IReadOnlyList<LineaPersistida> LineasACargar { get; set; } = Array.Empty<LineaPersistida>();
    public ResultadoLinea ResultadoDeAgregarLinea { get; set; } = new(ResultadoEscritura.Aplicado, 950);
    public ResultadoEscritura ResultadoDeActualizarLinea { get; set; } = ResultadoEscritura.Aplicado;
    public ResultadoEscritura ResultadoDeEliminarLinea { get; set; } = ResultadoEscritura.Aplicado;
    public LineaAsiento? UltimaLineaAgregada { get; private set; }
    public LineaAsiento? UltimaLineaActualizada { get; private set; }
    public long? UltimaLineaEliminadaId { get; private set; }

    public Task<IReadOnlyList<LineaPersistida>> CargarLineasPersistidasAsync(long asientoContableId, CancellationToken ct)
    {
        Llamadas.Add(nameof(CargarLineasPersistidasAsync));
        return Task.FromResult(LineasACargar);
    }

    public Task<ResultadoLinea> AgregarLineaAsync(
        long asientoContableId, byte[] versionEsperada, LineaAsiento linea, CancellationToken ct)
    {
        Llamadas.Add(nameof(AgregarLineaAsync));
        UltimaLineaAgregada = linea;
        return Task.FromResult(ResultadoDeAgregarLinea);
    }

    public Task<ResultadoEscritura> ActualizarLineaAsync(
        long lineaId, long asientoContableId, byte[] versionEsperada, LineaAsiento linea, CancellationToken ct)
    {
        Llamadas.Add(nameof(ActualizarLineaAsync));
        UltimaLineaActualizada = linea;
        return Task.FromResult(ResultadoDeActualizarLinea);
    }

    public Task<ResultadoEscritura> EliminarLineaAsync(
        long lineaId, long asientoContableId, byte[] versionEsperada, CancellationToken ct)
    {
        Llamadas.Add(nameof(EliminarLineaAsync));
        UltimaLineaEliminadaId = lineaId;
        return Task.FromResult(ResultadoDeEliminarLinea);
    }

    // --- PR 5 (Phase 5) addition ---

    public bool TipoCambioVigente { get; set; } = true;
    public DateOnly? UltimaFechaConsultadaTipoCambio { get; private set; }

    public Task<bool> ExisteTipoCambioVigenteAsync(DateOnly fecha, CancellationToken ct)
    {
        Llamadas.Add(nameof(ExisteTipoCambioVigenteAsync));
        UltimaFechaConsultadaTipoCambio = fecha;
        return Task.FromResult(TipoCambioVigente);
    }

    // --- PR 3 (Phase 3, BACKLOG #12) additions ---

    public IReadOnlyList<DocumentoFacturaPersistido> DocumentosFacturaACargar { get; set; } = Array.Empty<DocumentoFacturaPersistido>();
    public IReadOnlyList<AdjuntoManual> AdjuntosDeFacturaACargar { get; set; } = Array.Empty<AdjuntoManual>();
    public DocumentoFacturaPersistido? DocumentoFacturaPorId { get; set; }
    public AdjuntoManual? AdjuntoPorId { get; set; }

    public Task<IReadOnlyList<DocumentoFacturaPersistido>> CargarDocumentosFacturaAsync(long facturaId, CancellationToken ct)
    {
        Llamadas.Add(nameof(CargarDocumentosFacturaAsync));
        return Task.FromResult(DocumentosFacturaACargar);
    }

    public Task<IReadOnlyList<AdjuntoManual>> CargarAdjuntosDeFacturaAsync(long facturaId, CancellationToken ct)
    {
        Llamadas.Add(nameof(CargarAdjuntosDeFacturaAsync));
        return Task.FromResult(AdjuntosDeFacturaACargar);
    }

    public Task<DocumentoFacturaPersistido?> CargarDocumentoFacturaPorIdAsync(long documentoFacturaId, CancellationToken ct)
    {
        Llamadas.Add(nameof(CargarDocumentoFacturaPorIdAsync));
        return Task.FromResult(DocumentoFacturaPorId);
    }

    public Task<AdjuntoManual?> CargarAdjuntoPorIdAsync(long adjuntoManualId, CancellationToken ct)
    {
        Llamadas.Add(nameof(CargarAdjuntoPorIdAsync));
        return Task.FromResult(AdjuntoPorId);
    }

    // --- diseno-visual-spa-item-12 addition ---

    public ResultadoEscritura ResultadoDeConfirmarAfectacion { get; set; } = ResultadoEscritura.Aplicado;
    public bool? UltimaAfectacionMixtaConfirmada { get; private set; }

    public Task<ResultadoEscritura> ConfirmarAfectacionAsync(
        long facturaId, byte[] versionEsperada, bool esMixta, CancellationToken ct)
    {
        Llamadas.Add(nameof(ConfirmarAfectacionAsync));
        UltimaAfectacionMixtaConfirmada = esMixta;
        return Task.FromResult(ResultadoDeConfirmarAfectacion);
    }

    // --- BACKLOG #19 (design D4/D6) additions ---

    public bool ExisteIdentidadPrevia { get; set; }
    public bool? UltimoPosibleDuplicadoEscrito { get; private set; }
    public (decimal BasePen, decimal IgvPen, decimal NetoPen)? UltimaProyeccionEscalar { get; private set; }
    public ResultadoEscritura ResultadoDeProyeccionEscalar { get; set; } = ResultadoEscritura.Aplicado;

    public Task<bool> ExisteIdentidadPreviaAsync(
        long facturaId, string? rucProveedor, string tipoComprobante, string? numero, CancellationToken ct)
    {
        Llamadas.Add(nameof(ExisteIdentidadPreviaAsync));
        return Task.FromResult(ExisteIdentidadPrevia);
    }

    public Task ActualizarPosibleDuplicadoAsync(long facturaId, bool posibleDuplicado, CancellationToken ct)
    {
        Llamadas.Add(nameof(ActualizarPosibleDuplicadoAsync));
        UltimoPosibleDuplicadoEscrito = posibleDuplicado;
        return Task.CompletedTask;
    }

    public Task<ResultadoEscritura> ActualizarProyeccionEscalarAsync(
        long asientoContableId, decimal basePen, decimal igvPen, decimal netoPen, CancellationToken ct)
    {
        Llamadas.Add(nameof(ActualizarProyeccionEscalarAsync));
        UltimaProyeccionEscalar = (basePen, igvPen, netoPen);
        return Task.FromResult(ResultadoDeProyeccionEscalar);
    }

    // --- outbox-mensajeria (BACKLOG #14, design D10) addition ---

    public Task<TransicionEstadoFactura> MarcarFacturaValidadaAsync(long facturaId, CancellationToken ct)
    {
        Llamadas.Add(nameof(MarcarFacturaValidadaAsync));

        var actual = FacturaACargar;
        if (actual is null)
        {
            return Task.FromResult(TransicionEstadoFactura.NoTransicionable);
        }

        return actual.Estado switch
        {
            FacturaPersistida.PendienteValidacion => Aplicar(actual),
            FacturaPersistida.Validada => Task.FromResult(TransicionEstadoFactura.YaValidada),
            _ => Task.FromResult(TransicionEstadoFactura.NoTransicionable),
        };

        Task<TransicionEstadoFactura> Aplicar(FacturaPersistida original)
        {
            // "La transacción ve sus propias escrituras" (design D2/D9) -- el próximo
            // CargarFacturaAsync debe ver Estado = VALIDADA, igual que UltimaFacturaGuardada ??
            // FacturaACargar en GuardarFacturaAsync ya modela para el resto de escrituras.
            FacturaACargar = original with { Estado = FacturaPersistida.Validada };
            return Task.FromResult(TransicionEstadoFactura.Aplicada);
        }
    }
}

/// <summary>Fábrica que siempre devuelve la misma <see cref="FakeUnidadDeTrabajo"/> — deja a la
/// prueba inspeccionar <see cref="FakeUnidadDeTrabajo.Llamadas"/> después de llamar al Servicio.</summary>
public sealed class FakeFacturacionStore : IFacturacionStore
{
    public FakeUnidadDeTrabajo UnidadDeTrabajo { get; } = new();

    public Task<IUnidadDeTrabajo> AbrirAsync(CancellationToken ct) =>
        Task.FromResult<IUnidadDeTrabajo>(UnidadDeTrabajo);
}

public sealed class FakeCommandQueueRepository : ICommandQueueRepository
{
    public List<(string Tipo, long? Referencia, string Payload, Guid CorrelationId)> Encolados { get; } = new();

    public Task EncolarAsync(string tipo, long? referencia, string payload, Guid correlationId, CancellationToken ct)
    {
        Encolados.Add((tipo, referencia, payload, correlationId));
        return Task.CompletedTask;
    }
}

public sealed class FakeEstadoIntegracionRepository : IEstadoIntegracionRepository
{
    public IReadOnlyList<EstadoIntegracion> Estados { get; set; } = Array.Empty<EstadoIntegracion>();

    public Task<IReadOnlyList<EstadoIntegracion>> ListarAsync(CancellationToken ct) =>
        Task.FromResult(Estados);
}
