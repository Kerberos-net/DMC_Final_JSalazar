/**
 * Mirrors `SmartNet.Api.CuentaContableResultado` / `PlanContableRespuesta`
 * (BACKLOG #22 PR2 -- `GET /api/catalogos/plan-contable`). ASP.NET Core's default
 * `System.Text.Json` options camelCase the C# PascalCase property names.
 *
 * `esHojaImputable` is projected server-side (`nivel IS NULL`); the SPA never recomputes it.
 * The screen column labelled "denominación" maps to the `descripcion` field (spec v2.1 note).
 */
export interface CuentaContable {
  readonly cuenta: string;
  readonly descripcion: string;
  readonly nivel: number | null;
  readonly esHojaImputable: boolean;
}

export interface PlanContableRespuesta {
  readonly items: readonly CuentaContable[];
}

/** Sortable columns on the plan contable screen (client-side sort, design D7/D8). */
export type ClavePlanContable = 'cuenta' | 'descripcion';
