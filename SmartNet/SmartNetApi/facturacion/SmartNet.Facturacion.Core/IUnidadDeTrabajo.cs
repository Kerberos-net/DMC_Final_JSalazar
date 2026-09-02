using SmartNet.Catalogos.Core;
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

    /// <summary>BACKLOG #24 (design A1/A3) — resuelve los hechos externos que
    /// <see cref="SembradoDeAsiento"/> necesita y que una <see cref="FacturaPersistida"/> no lleva:
    /// <c>fact.ProveedorAtributo.EsRelacionada</c>, <c>dbo.Motivo.descripcion</c> y el tipo de
    /// cambio VENTA vigente para la fecha de emisión (null para PEN). Precedente:
    /// <see cref="ExisteTipoCambioVigenteAsync"/> — una lectura catálogo-ish sobre este puerto.</summary>
    Task<HechosDeComposicion> ResolverHechosDeComposicionAsync(long facturaId, CancellationToken ct);

    /// <summary>BACKLOG #24 (design C1) — lee una fila de <c>dbo.CuentaContable</c> por su código
    /// exacto (grant <c>SELECT dbo.CuentaContable</c> en <c>008</c>), o <c>null</c> si no existe. Lo
    /// que <see cref="ServicioDeAsientos.RecomponerAsync"/> usa para resolver el <c>cuentaCodigo</c>
    /// opcional del cuerpo a una <see cref="CuentaContable"/> (con su reflejo/puente) antes de
    /// re-sembrar — así el asistente cierra el bucle A2 en una sola acción.</summary>
    Task<CuentaContable?> ObtenerCuentaContableAsync(string cuentaCodigo, CancellationToken ct);

    /// <summary>BACKLOG #24 (design B1) — crea un <c>fact.AsientoContable</c> nuevo en BORRADOR para
    /// una factura sin asiento vigente (design D1, <c>abrir</c>) y persiste el encabezado (escalares
    /// del motor) más las <c>N</c> líneas ya compuestas por <see cref="ComposicionDeAsiento.Componer"/>.
    /// Devuelve el id del asiento nuevo. Las líneas se insertan en un bucle dentro de la misma
    /// transacción — NUNCA vía <c>AgregarLineaAsync</c> (su CAS de encabezado espera una Version que
    /// el llamador no sostiene: la fila se creó microsegundos antes en esta transacción).</summary>
    Task<long> CrearAsientoBorradorAsync(
        long facturaId, AsientoContable asiento, CancellationToken ct);

    /// <summary>BACKLOG #24 (design B2, <c>recomponer</c>) — reemplaza TODAS las líneas de un asiento
    /// BORRADOR por las recién compuestas y re-deriva los escalares del encabezado
    /// (<c>BasePEN/IgvPEN/NetoPEN/MotivoDescripcion/TipoCambioVenta/FechaContable</c> — nunca
    /// <c>Estado</c> ni <c>NumeroAsiento</c>). CAS contra <c>fact.AsientoContable.Version</c> vía el
    /// mismo <c>TocarEncabezadoAsync</c> que las escrituras de línea: <see cref="ResultadoEscritura.VersionEnConflicto"/>
    /// con un ETag rancio, <see cref="ResultadoEscritura.NoEncontrado"/> si el asiento no existe.</summary>
    Task<ResultadoEscritura> ReemplazarLineasAsync(
        long asientoContableId, byte[] versionEsperada, AsientoContable asiento, CancellationToken ct);

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

    // --- PR 3 (Phase 3, BACKLOG #12): lectura read-only para la lista unificada de documentos y el
    // visor (spec.md documentos-lista-unificada-api / documento-contenido-api, design D1). Ningún
    // miembro nuevo toca fact.DocumentoRecibido -- el proyecto .NET-owned fact.DocumentoFactura
    // (schema 016) y fact.AdjuntoManual son las únicas dos fuentes (ADR 0003 §Privadas). ---

    /// <summary>Todos los <c>fact.DocumentoFactura</c> (proyección de documentos ingeridos por
    /// Python, poblada en promoción) de una factura, en el orden en que fueron creados.</summary>
    Task<IReadOnlyList<DocumentoFacturaPersistido>> CargarDocumentosFacturaAsync(long facturaId, CancellationToken ct);

    /// <summary>Todos los <c>fact.AdjuntoManual</c> NO eliminados (<c>EliminadoEn IS NULL</c>) de una
    /// factura -- un adjunto borrado lógicamente desaparece de la lista unificada (D6/ADR 0013 "borrado
    /// con rastro": el rastro vive en <c>AuditoriaCorreccion</c>, no en la lista visible).</summary>
    Task<IReadOnlyList<AdjuntoManual>> CargarAdjuntosDeFacturaAsync(long facturaId, CancellationToken ct);

    /// <summary>Un <c>fact.DocumentoFactura</c> por id, o <c>null</c> si no existe -- lo que
    /// <c>GET /api/documentos/{id}/contenido</c> necesita para resolver un id de origen "ingesta"
    /// (design D2) antes de servir bytes.</summary>
    Task<DocumentoFacturaPersistido?> CargarDocumentoFacturaPorIdAsync(long documentoFacturaId, CancellationToken ct);

    /// <summary>Un <c>fact.AdjuntoManual</c> por id, o <c>null</c> si no existe O si ya fue
    /// eliminado lógicamente (mismo criterio que <see cref="CargarAdjuntosDeFacturaAsync"/>: un
    /// adjunto eliminado no es servible, aunque la fila siga físicamente en la tabla).</summary>
    Task<AdjuntoManual?> CargarAdjuntoPorIdAsync(long adjuntoManualId, CancellationToken ct);

    // --- diseno-visual-spa-item-12 (BACKLOG #12 reabierto, design D10): confirmación explícita de
    // afectación tributaria (REGLAS.md §8 "La factura mixta" -- el asistente confirma tras mirar el
    // documento). Escritura CAS DEDICADA porque GuardarFacturaAsync's UPDATE nunca toca
    // AfectacionMixta a propósito (design D9: las 4 columnas indicadoras son de solo lectura para un
    // PATCH normal, así un round-trip PATCH nunca las pisa). El gate CasoConflicto.
    // AfectacionNoVerificada permanece DORMIDO -- este método solo escribe la columna y deja la
    // auditoría/commit al llamador (mismo patrón que GuardarFacturaAsync). ---

    /// <summary>Escritura CAS de <c>fact.Factura.AfectacionMixta</c> únicamente -- mismo contrato de
    /// <see cref="ResultadoEscritura"/> que <see cref="GuardarFacturaAsync"/>.</summary>
    Task<ResultadoEscritura> ConfirmarAfectacionAsync(
        long facturaId, byte[] versionEsperada, bool esMixta, CancellationToken ct);

    // --- outbox-mensajeria (BACKLOG #14, design D10 / ADR 0020 decisión 5) addition ---

    /// <summary>Único miembro nuevo de #14: transición <c>state-CAS</c> (no version-CAS, ver ADR 0020
    /// decisión 5) <c>PENDIENTE_VALIDACION -&gt; VALIDADA</c> de <c>fact.Factura.Estado</c>. Nunca
    /// recibe versión ni estado destino -- una columna, un valor literal, un único estado de origen
    /// legal (design D10).</summary>
    Task<TransicionEstadoFactura> MarcarFacturaValidadaAsync(long facturaId, CancellationToken ct);

    // --- BACKLOG #19 (design D4/D6) additions: el PATCH contable necesita recomputar la proyección
    // escalar del asiento y el indicador PosibleDuplicado DENTRO de la misma transacción. ---

    /// <summary>design D6 — <c>true</c> si existe OTRA <c>fact.Factura</c> (no la misma, no
    /// DESCARTADA) con la misma tripleta de identidad (<c>RucProveedor</c>, <c>TipoComprobante</c>,
    /// <c>Numero</c>, <c>IX_Factura_Identidad</c>). Lo que <see cref="ServicioDeFacturas.PatchAsync"/>
    /// usa para recomputar <c>fact.Factura.PosibleDuplicado</c> cuando la tripleta cambia.</summary>
    Task<bool> ExisteIdentidadPreviaAsync(
        long facturaId, string? rucProveedor, string tipoComprobante, string? numero, CancellationToken ct);

    /// <summary>design D6 — escritura DIRECTA (sin CAS: el CAS de <c>fact.Factura.Version</c> ya lo
    /// hizo <see cref="GuardarFacturaAsync"/> en esta misma transacción) de <c>PosibleDuplicado</c>
    /// únicamente. <c>GuardarFacturaAsync</c>'s UPDATE deliberadamente no toca las columnas
    /// indicadoras (design D9); esta recomputación derivada es la única excepción.</summary>
    Task ActualizarPosibleDuplicadoAsync(long facturaId, bool posibleDuplicado, CancellationToken ct);

    /// <summary>design D4 — escritura de los tres escalares (<c>BasePEN</c>, <c>IgvPEN</c>,
    /// <c>NetoPEN</c>) sobre el asiento BORRADOR vigente, en la misma transacción del PATCH. Solo
    /// aplica si el asiento sigue en BORRADOR; <see cref="ResultadoEscritura.NoEncontrado"/> si no
    /// existe o ya no está en BORRADOR. <c>ROWVERSION</c> se incrementa por el propio UPDATE.</summary>
    Task<ResultadoEscritura> ActualizarProyeccionEscalarAsync(
        long asientoContableId, decimal basePen, decimal igvPen, decimal netoPen, CancellationToken ct);
}

/// <summary>design D10 — resultado de <see cref="IUnidadDeTrabajo.MarcarFacturaValidadaAsync"/>.</summary>
public enum TransicionEstadoFactura
{
    /// <summary><c>@@ROWCOUNT &gt; 0</c> -- la factura estaba PENDIENTE_VALIDACION y ahora es VALIDADA.</summary>
    Aplicada,

    /// <summary>La factura ya estaba VALIDADA -- reconfirmación tras reabrir (D1); no es un error, no
    /// hace rollback.</summary>
    YaValidada,

    /// <summary>Cualquier otro estado (hoy: DESCARTADA) -- terminal, 409 (OQ5, ADR 0020 decisión 5).</summary>
    NoTransicionable,
}
