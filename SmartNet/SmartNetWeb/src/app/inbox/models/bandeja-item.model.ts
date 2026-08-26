/**
 * Mirrors `SmartNet.Inbox.Core.BandejaItem` / `PaginaBandeja<T>` (BACKLOG #13, design.md
 * Interfaces/Contracts, widening ADR 0008's `GET /api/bandeja` contract). `IndicadoresFactura`
 * and `facturaId` are only present once `estadoConsumo` is `PROMOVIDO` — narrowed below via the
 * `origen` discriminated union (design D2). `errores` is never `null` — an empty array means no
 * error history, for either `origen`. `reprocesarDisponibleEn` `null` means the reprocesar
 * control is enabled now (design D5).
 */

export type EstadoConsumo = 'PENDIENTE' | 'PROMOVIDO' | 'DESCARTADO';

export type OrdenFecha = 'asc' | 'desc';

export type Origen = 'FACTURA' | 'INCIDENCIA';

export interface IndicadoresFactura {
  readonly esProveedorGenerico: boolean;
  readonly posibleDuplicado: boolean;
  readonly tieneCamposNoExtraidos: boolean;
  readonly fechaEnDomingo: boolean;
  readonly afectacionMixta: boolean | null;
}

export interface ErrorProcesamiento {
  readonly procesamientoErrorId: number;
  readonly integracion: string;
  readonly mensaje: string;
  readonly clasificacion: string;
  readonly ocurridoEn: string;
}

interface BandejaItemBase {
  readonly inboxEventId: number;
  readonly procesamientoId: number;
  readonly estadoConsumo: EstadoConsumo;
  readonly creadoEn: string;
  readonly proveedorCodigo: string | null;
  readonly rucProveedor: string | null;
  readonly motivoDescarte: string | null;
  readonly errores: readonly ErrorProcesamiento[];
  readonly reprocesarDisponibleEn: string | null;
}

/**
 * Design D2 -- flat wire shape (same nullable-by-state fields `SqlBandejaRepository` already
 * projects), narrowed here on the client via `origen` so a `FACTURA` row's `facturaId`/
 * `indicadores` type as non-null without a cast.
 */
export type BandejaItem =
  | (BandejaItemBase & {
      readonly origen: 'FACTURA';
      readonly facturaId: number;
      readonly indicadores: IndicadoresFactura;
    })
  | (BandejaItemBase & {
      readonly origen: 'INCIDENCIA';
      readonly facturaId: null;
      readonly indicadores: null;
    });

/** design.md Interfaces/Contracts -- the pagination envelope `GET /api/bandeja` always returns. */
export interface PaginaBandeja<T> {
  readonly items: readonly T[];
  readonly pagina: number;
  readonly tamanioPagina: number;
  readonly totalRegistros: number;
  readonly totalPaginas: number;
}

/** Filters accepted by `InboxService.cargar()` (design.md Data Flow) -- all optional. */
export interface FiltrosBandeja {
  readonly estado?: EstadoConsumo | null;
  readonly desde?: string | null;
  readonly hasta?: string | null;
  readonly proveedor?: string | null;
  readonly orden?: OrdenFecha;
  readonly pagina?: number;
}
