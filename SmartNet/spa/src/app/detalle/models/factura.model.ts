/**
 * Mirrors `SmartNet.Api.FacturaRespuesta` / `CorreccionFacturaRequest` (BACKLOG #11's
 * `FacturaEndpoints.cs` — GET/PATCH `/api/facturas/{id}`). ASP.NET Core's default
 * `System.Text.Json` options camelCase the C# PascalCase property names.
 */
export interface FacturaRespuesta {
  readonly facturaId: number;
  readonly estado: string;
  readonly proveedorCodigo: string;
  readonly rucProveedor: string | null;
  readonly tipoComprobante: string;
  readonly numero: string | null;
  readonly totalOrig: number;
  readonly moneda: string;
  readonly fechaEmision: string;
  readonly motivo: number | null;
  readonly afectacion: string | null;
}

/** Cuerpo de `PATCH /api/facturas/{id}` — todos los campos opcionales (corrección parcial). */
export interface CorreccionFacturaRequest {
  readonly proveedorCodigo?: string | null;
  readonly rucProveedor?: string | null;
  readonly moneda?: string | null;
  readonly totalOrig?: number | null;
  readonly fechaEmision?: string | null;
  readonly motivo?: number | null;
  readonly afectacion?: string | null;
}
