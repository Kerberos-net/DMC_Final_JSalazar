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
        return Task.FromResult(AsientoACargar);
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

    public Task EmitirOutboxAsync(string tipo, long facturaId, string payload, CancellationToken ct)
    {
        Llamadas.Add(nameof(EmitirOutboxAsync));
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

    public FacturaPersistida? FacturaACargar { get; set; }
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
        return Task.FromResult(FacturaACargar);
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
