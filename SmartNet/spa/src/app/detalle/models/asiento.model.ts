/**
 * Mirrors `SmartNet.Api.AsientoRespuesta` / `LineaRespuesta` / `LineaAsientoRequest` /
 * `FacturaAsientoRespuesta` (BACKLOG #11/#12 — `AsientoEndpoints.cs`, `FacturaEndpoints.cs`).
 * `LineaRespuesta.lineaId` is the stable id (spec.md api-asientos: "never position") — PR5 gap
 * closure that added `Lineas` to `AsientoRespuesta` (see apply-progress).
 */
export type Bloque = 'PRINCIPAL' | 'DESTINO';
export type TipoLinea = 'D' | 'H';

export interface LineaRespuesta {
  readonly lineaId: number;
  readonly orden: number;
  readonly bloque: Bloque;
  readonly tipo: TipoLinea;
  readonly debe: number;
  readonly haber: number;
  readonly cuentaCodigo: string | null;
  readonly cuentaDescripcion: string | null;
  readonly ctaReflejaCodigo: string | null;
  readonly ctaPuenteCodigo: string | null;
}

/** Cuerpo de `POST/PATCH /api/asientos/{id}/lineas[/{lineaId}]`. */
export interface LineaAsientoRequest {
  readonly orden: number;
  readonly bloque: Bloque;
  readonly tipo: TipoLinea;
  readonly debe: number;
  readonly haber: number;
  readonly cuentaCodigo: string | null;
  readonly cuentaDescripcion: string | null;
  readonly ctaReflejaCodigo: string | null;
  readonly ctaPuenteCodigo: string | null;
}

export interface AsientoRespuesta {
  readonly asientoContableId: number;
  readonly estado: string;
  readonly numeroAsiento: string | null;
  readonly proveedorCodigo: string;
  readonly fechaContable: string;
  readonly motivoDescripcion: string | null;
  readonly tipoCambioVenta: number | null;
  readonly lineas: readonly LineaRespuesta[];
}

/** `GET /api/facturas/{id}/asiento` (design D3) — ambos `null` juntos = "sin asiento vigente". */
export interface FacturaAsientoRespuesta {
  readonly asientoContableId: number | null;
  readonly asiento: AsientoRespuesta | null;
}
