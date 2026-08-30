/**
 * Mirrors `SmartNet.Api.TipoCambioHistoricoResultado` / `TipoCambioHistoricoRespuesta`
 * (BACKLOG #22 PR7 -- `GET /api/tipos-cambio?desde=&hasta=`). ASP.NET Core's default
 * `System.Text.Json` options camelCase the C# PascalCase property names; `DateOnly` serializes as
 * `yyyy-MM-dd` and `origen` is emitted as the string `"SBS"` / `"MANUAL"` by an explicit mapper.
 *
 * The screen fetches a bounded range (<= 366 days x 2 origins) in one response and sorts it
 * client-side (design D8) -- there is no server pagination for this resource.
 */
export type OrigenTipoCambio = 'SBS' | 'MANUAL';

export interface TipoCambioHistorico {
  readonly fecha: string;
  readonly origen: OrigenTipoCambio;
  readonly compra: number;
  readonly venta: number;
  readonly fechaConsulta: string;
}

export interface TipoCambioRespuesta {
  readonly items: readonly TipoCambioHistorico[];
}

/** Sortable columns on the tipo de cambio screen (client-side sort, design D8). */
export type ClaveTipoCambio = 'fecha' | 'origen' | 'compra' | 'venta';
