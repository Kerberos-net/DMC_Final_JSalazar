/**
 * Mirrors `SmartNet.Inbox.Core.BandejaItem` / `IndicadoresFactura` (ADR 0008's
 * `GET /api/bandeja` contract, D6). `IndicadoresFactura` and `FacturaId` are only present once
 * `EstadoConsumo` is `PROMOVIDO`. `IndicadoresFactura` carries 5 computed flags (design D5) —
 * `EsReferenciaExterna` always keeps its DDL default and is never sent by the API.
 */

export type EstadoConsumo = 'PENDIENTE' | 'PROMOVIDO' | 'DESCARTADO';

export type OrdenFecha = 'asc' | 'desc';

export interface IndicadoresFactura {
  readonly esProveedorGenerico: boolean;
  readonly posibleDuplicado: boolean;
  readonly tieneCamposNoExtraidos: boolean;
  readonly fechaEnDomingo: boolean;
  readonly afectacionMixta: boolean | null;
}

export interface BandejaItem {
  readonly inboxEventId: number;
  readonly estadoConsumo: EstadoConsumo;
  readonly creadoEn: string;
  readonly facturaId: number | null;
  readonly indicadores: IndicadoresFactura | null;
  readonly motivoDescarte: string | null;
}
