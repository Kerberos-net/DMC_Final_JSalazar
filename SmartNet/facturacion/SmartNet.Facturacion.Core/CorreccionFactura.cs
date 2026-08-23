namespace SmartNet.Facturacion.Core;

/// <summary>
/// PR 2 — cuerpo de <c>PATCH /api/facturas/{id}</c> (design D6: "PATCH factura/asiento" -&gt;
/// <c>Accion=CORRECCION</c>, UNA fila de <c>AuditoriaCorreccion</c> POR CAMPO cambiado). Cada
/// propiedad <c>null</c> significa "no se toca"; <see cref="ServicioDeFacturas.PatchAsync"/> compara
/// contra el valor cargado y solo audita los campos que de verdad cambiaron de valor.
/// </summary>
public sealed record CorreccionFactura(
    string? ProveedorCodigo = null,
    string? RucProveedor = null,
    string? Moneda = null,
    decimal? TotalOrig = null,
    DateOnly? FechaEmision = null,
    int? Motivo = null,
    string? Afectacion = null);
