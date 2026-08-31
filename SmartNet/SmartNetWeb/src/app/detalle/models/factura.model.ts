/**
 * Mirrors `SmartNet.Api.FacturaRespuesta` / `CorreccionFacturaRequest` (BACKLOG #11's
 * `FacturaEndpoints.cs` — GET/PATCH `/api/facturas/{id}`). ASP.NET Core's default
 * `System.Text.Json` options camelCase the C# PascalCase property names.
 *
 * diseno-visual-spa-item-12 (design D9): the 4 trailing indicator fields are a purely additive
 * projection — `esProveedorGenerico`/`posibleDuplicado` drive `.alerta--bloqueante`;
 * `tieneCamposNoExtraidos`/`afectacionMixta === null` drive `.alerta--informativa`.
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
  readonly esProveedorGenerico: boolean;
  readonly posibleDuplicado: boolean;
  readonly tieneCamposNoExtraidos: boolean;
  readonly afectacionMixta: boolean | null;
  /** BACKLOG #19 (design D8/D9): per-field OCR-missing list — canonical set
   * `tipoComprobante | numero | ruc | nombreProveedor | total | igv | moneda | fechaEmision`.
   * Empty for facturas pre-021 (the coarse `tieneCamposNoExtraidos` boolean is the fallback). */
  readonly camposNoExtraidos: readonly string[];
  /** BACKLOG #19 (schema 021): free-text glosa contable, editable while `PENDIENTE_VALIDACION`. */
  readonly glosa: string | null;
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
  /** BACKLOG #18 PR5 (api-facturas delta): tipoComprobante/numero ya son PATCH-editables. */
  readonly tipoComprobante?: string | null;
  readonly numero?: string | null;
  /** BACKLOG #19 (design D1): base imponible / IGV viajan como PAR ATOMICO en moneda de origen
   * (`TotalOrig = baseImponible + igv`); enviar solo uno, o el par junto con `totalOrig`, es 422. */
  readonly baseImponible?: number | null;
  readonly igv?: number | null;
  readonly glosa?: string | null;
}
