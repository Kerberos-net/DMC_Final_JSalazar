using SmartNet.Contable.Core;

namespace SmartNet.Facturacion.Core;

/// <summary>
/// design.md D1/Interfaces-Contracts — sesión de una transacción, <see cref="IAsyncDisposable"/>
/// que posee un <c>SqlTransaction</c> (Infrastructure): rollback salvo <see cref="CommitAsync"/>
/// explícito. Un <see cref="ServicioDeFacturas"/>/<see cref="ServicioDeAsientos"/> abre una, la usa
/// para cargar -&gt; componer/evaluar (puro) -&gt; escribir -&gt; commitear, todo dentro de ella
/// (ADR 0006: las invariantes se evalúan DENTRO de la transacción). Firma exacta fijada en
/// design.md; no se renombra ni se reordena aquí.
/// </summary>
public interface IUnidadDeTrabajo : IAsyncDisposable
{
    Task<AsientoPersistido?> CargarAsientoAsync(long asientoId, CancellationToken ct);

    Task<ResultadoEscritura> GuardarAsientoAsync(
        long id, byte[] versionEsperada, AsientoPersistido asiento, CancellationToken ct);

    Task<int> AsignarCorrelativoAsync(short anio, byte mes, string origen, CancellationToken ct);

    Task RegistrarAuditoriaAsync(EntradaAuditoria entrada, CancellationToken ct);

    Task EmitirOutboxAsync(string tipo, long facturaId, string payload, CancellationToken ct);

    Task CommitAsync(CancellationToken ct);

    // --- PR 2 (Phase 2, deviación documentada en FacturaPersistida.cs): el contrato factura-shaped
    // que design.md no detalló porque su único snippet fue asiento-shaped (PR 1, deviación #1). ---

    /// <summary>Carga <c>fact.Factura</c> por id, o <c>null</c> si no existe.</summary>
    Task<FacturaPersistida?> CargarFacturaAsync(long facturaId, CancellationToken ct);

    /// <summary>Escritura CAS de <c>fact.Factura</c> — mismo contrato de <see cref="ResultadoEscritura"/>
    /// que <see cref="GuardarAsientoAsync"/> (design D2: <c>@@ROWCOUNT=0</c> -&gt; re-SELECT ->
    /// 412 si existe con otra Version, 404 si no existe).</summary>
    Task<ResultadoEscritura> GuardarFacturaAsync(
        long id, byte[] versionEsperada, FacturaPersistida factura, CancellationToken ct);

    /// <summary>Resuelve el <c>AsientoContableId</c> del asiento vigente (no ANULADO,
    /// <c>UQ_Asiento_Vigente</c>) de una factura, o <c>null</c> si no tiene ninguno — lo que
    /// <c>POST /api/facturas/{id}/validar</c> necesita para resolver factura-&gt;asiento antes de
    /// reutilizar la lógica de <see cref="ServicioDeFacturas.ValidarAsync"/> (PR 1, deviación #1).</summary>
    Task<long?> ObtenerAsientoVigenteIdAsync(long facturaId, CancellationToken ct);

    /// <summary>Crea un <c>fact.AsientoContable</c> nuevo en BORRADOR para una factura sin asiento
    /// vigente (design D1, <c>abrir</c>) y devuelve su id. La composición de líneas (Bloque
    /// PRINCIPAL/DESTINO) es de Phase 3 — este método solo crea el ENCABEZADO.</summary>
    Task<long> CrearAsientoBorradorAsync(
        long facturaId, string proveedorCodigo, DateOnly fechaContable, CancellationToken ct);

    /// <summary>Inserta un <c>fact.AdjuntoManual</c> y devuelve su id.</summary>
    Task<long> RegistrarAdjuntoAsync(AdjuntoManual adjunto, CancellationToken ct);

    /// <summary>Borrado lógico de un adjunto (<c>CK_AdjuntoManual_Eliminacion</c>: los tres campos
    /// de eliminación juntos o ninguno). <see cref="ResultadoEscritura.Aplicado"/> si existía y no
    /// estaba ya eliminado; <see cref="ResultadoEscritura.NoEncontrado"/> en otro caso (incluye "ya
    /// eliminado" — no hay un segundo borrado del mismo adjunto).</summary>
    Task<ResultadoEscritura> EliminarAdjuntoAsync(
        long adjuntoManualId, long facturaId, DateTimeOffset eliminadoEn, long eliminadoPorUsuarioId,
        string motivoEliminacion, CancellationToken ct);

    // --- PR 3 (Phase 3, deviación #9 de apply-progress): GuardarAsientoAsync solo escribe el
    // encabezado -- estos cuatro miembros extienden el contrato para las líneas por LineaId
    // (spec.md api-asientos: "never position"). Cada escritura hace CAS contra
    // fact.AsientoContable.Version (design D2: "3 line routes" están en la lista de superficies
    // mutables con ETag), aunque el cambio real ocurra en fact.AsientoContableDetalle. ---

    /// <summary>Todas las líneas de un asiento, con su <see cref="LineaId"/> estable, en orden.</summary>
    Task<IReadOnlyList<LineaPersistida>> CargarLineasPersistidasAsync(long asientoContableId, CancellationToken ct);

    /// <summary>Inserta una línea nueva. CAS contra <c>fact.AsientoContable.Version</c> (el encabezado
    /// no cambia de valor, pero su <c>Version</c> se toca -- design D2's ETag round-trip exige que
    /// la siguiente escritura de línea use el ETag nuevo). <see cref="ResultadoLinea.LineaId"/> trae
    /// el id nuevo solo cuando <see cref="ResultadoLinea.Resultado"/> es <see cref="ResultadoEscritura.Aplicado"/>.</summary>
    Task<ResultadoLinea> AgregarLineaAsync(
        long asientoContableId, byte[] versionEsperada, LineaAsiento linea, CancellationToken ct);

    /// <summary>Reemplaza una línea existente por <see cref="LineaId"/> (nunca por posición/Orden).
    /// Mismo CAS de encabezado que <see cref="AgregarLineaAsync"/>; <see cref="ResultadoEscritura.NoEncontrado"/>
    /// si el CAS del encabezado aplica pero la línea no existe (id equivocado o de otro asiento).</summary>
    Task<ResultadoEscritura> ActualizarLineaAsync(
        long lineaId, long asientoContableId, byte[] versionEsperada, LineaAsiento linea, CancellationToken ct);

    /// <summary>Elimina (físicamente -- <c>fact.AsientoContableDetalle</c> no tiene borrado lógico,
    /// a diferencia de <c>fact.AdjuntoManual</c>) una línea por <see cref="LineaId"/>. Mismo CAS de
    /// encabezado que <see cref="AgregarLineaAsync"/>.</summary>
    Task<ResultadoEscritura> EliminarLineaAsync(
        long lineaId, long asientoContableId, byte[] versionEsperada, CancellationToken ct);

    // --- PR 5 (Phase 5, SinTipoCambio gap closure, verify-report.md CRITICAL finding): cierra el
    // gap documentado en HechosDeConflicto.cs/deviation 4-8 de apply-progress.md -- ServicioDeFacturas
    // .AbrirAsync (Core) necesita esta pregunta ANTES de que exista un asiento (CargarAsientoAsync no
    // aplica todavía), así que se expone como su propio miembro del puerto, delegando en
    // Infrastructure al ITipoCambioRepository ya existente (item #3/#11) -- ningún SELECT nuevo, solo
    // el wiring que faltaba. ---

    /// <summary>spec.md api-facturas "Opening a factura with no tipo de cambio (foreign currency)":
    /// <c>true</c> si existe una fila vigente (SBS o MANUAL) en <c>fact.TipoCambio</c> para
    /// <paramref name="fecha"/> (ADR 0018 pt. 3, <c>ITipoCambioRepository.ObtenerVigenteAsync</c>).
    /// Core solo llama esto para monedas distintas de PEN -- CasoConflicto.SinTipoCambio nunca se
    /// evalúa para facturas en moneda local.</summary>
    Task<bool> ExisteTipoCambioVigenteAsync(DateOnly fecha, CancellationToken ct);
}
