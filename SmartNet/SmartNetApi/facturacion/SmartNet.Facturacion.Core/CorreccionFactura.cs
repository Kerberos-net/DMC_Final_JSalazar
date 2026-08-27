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
    string? Numero = null);
