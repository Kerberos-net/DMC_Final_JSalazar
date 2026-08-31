namespace SmartNet.Facturacion.Core;

/// <summary>
/// PR 2 — cuerpo de <c>PATCH /api/facturas/{id}</c> (design D6: "PATCH factura/asiento" -&gt;
/// <c>Accion=CORRECCION</c>, UNA fila de <c>AuditoriaCorreccion</c> POR CAMPO cambiado). Cada
/// propiedad <c>null</c> significa "no se toca"; <see cref="ServicioDeFacturas.PatchAsync"/> compara
/// contra el valor cargado y solo audita los campos que de verdad cambiaron de valor.
///
/// BACKLOG #18 PR5 (api-facturas delta) — <see cref="TipoComprobante"/> y <see cref="Numero"/> son
/// dos parametros opcionales TRAILING mas: la SPA ya puede editarlos. <c>null</c> = no se toca
/// (igual que el resto); por eso <see cref="Numero"/> nunca puede volver a <c>NULL</c> via PATCH.
///
/// BACKLOG #19 (design D1) — <see cref="BaseImponible"/> e <see cref="Igv"/> se corrigen como un
/// PAR ATÓMICO (la base es DERIVADA, REGLAS.md §6): el ladder escribe <c>TotalOrig = base + igv</c>
/// e <c>IgvOrig = igv</c>. Enviar uno sin el otro, o el par junto con <see cref="TotalOrig"/>, es
/// 422. <see cref="Glosa"/> es texto libre editable solo en PENDIENTE_VALIDACION (D2).
/// </summary>
public sealed record CorreccionFactura(
    string? ProveedorCodigo = null,
    string? RucProveedor = null,
    string? Moneda = null,
    decimal? TotalOrig = null,
    DateOnly? FechaEmision = null,
    int? Motivo = null,
    string? Afectacion = null,
    string? TipoComprobante = null,
    string? Numero = null,
    decimal? BaseImponible = null,
    decimal? Igv = null,
    string? Glosa = null);
