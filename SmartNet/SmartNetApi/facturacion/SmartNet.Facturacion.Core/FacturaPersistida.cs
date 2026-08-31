namespace SmartNet.Facturacion.Core;

/// <summary>
/// PR 2 — espejo de <c>fact.Factura</c> que <see cref="IUnidadDeTrabajo.CargarFacturaAsync"/>
/// devuelve. Análogo de <see cref="AsientoPersistido"/> pero para el agregado Factura: PATCH,
/// abrir, descartar y adjuntos operan sobre esta forma, nunca sobre <see cref="AsientoContable"/>
/// directamente (design.md D1: Core nunca ve SQL, solo estos records).
///
/// DESVIACIÓN DOCUMENTADA (PR 2, misma naturaleza que la #1 de PR 1): design.md solo fijó la forma
/// de <see cref="IUnidadDeTrabajo"/> alrededor de un asiento. Este record y los miembros de
/// <see cref="IUnidadDeTrabajo"/> que lo cargan/guardan son una extensión del contrato, necesaria
/// para que <c>PATCH /api/facturas/{id}</c>, <c>abrir</c>, <c>descartar</c> y adjuntos (Phase 2)
/// tengan un puerto propio en vez de forzar el de asiento.
/// </summary>
public sealed record FacturaPersistida(
    long FacturaId,
    string Estado,
    string ProveedorCodigo,
    string? RucProveedor,
    string TipoComprobante,
    string? Numero,
    decimal TotalOrig,
    string Moneda,
    DateOnly FechaEmision,
    int? Motivo,
    string? Afectacion,
    byte[] Version,
    // diseno-visual-spa-item-12 (design D9) — mismas cuatro columnas que fact.Factura ya persiste y
    // que SqlBandejaRepository.ListarAsync (#13) ya lee; CargarFacturaAsync simplemente no las
    // seleccionaba. TRAILING con default: mantiene fuente-compatibles los ~20 call sites existentes
    // que ya usan argumentos con nombre (ninguno posicional hoy, verificado), y a FakeUnidadDeTrabajo
    // (SmartNet.Facturacion.Core.Tests), que no construye este record directamente pero sí lo
    // reasigna vía FacturaACargar en varios tests -- un quinto/sexto/séptimo/octavo parámetro nuevo
    // sin default rompería cualquier construcción existente que no los provea.
    bool EsProveedorGenerico = false,
    bool PosibleDuplicado = false,
    bool TieneCamposNoExtraidos = false,
    bool? AfectacionMixta = null,
    // BACKLOG #19 — IgvOrig es NULLABLE (una boleta / no gravada no lo desglosa); Glosa es texto
    // libre (schema 021); CamposNoExtraidos es la lista por campo del OCR (schema 021, D8) —
    // NULL para facturas promovidas antes de 021, y entonces la SPA cae al bool coarse.
    decimal? IgvOrig = null,
    string? Glosa = null,
    IReadOnlyList<string>? CamposNoExtraidos = null)
{
    public const string PendienteValidacion = "PENDIENTE_VALIDACION";
    public const string Validada = "VALIDADA";
    public const string Descartada = "DESCARTADA";
}
