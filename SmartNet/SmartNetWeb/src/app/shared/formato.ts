/**
 * Display-only formatting helpers. Pure, no I/O (ADR 0019 parity on the SPA side).
 *
 * Money is ALWAYS shown with exactly 2 decimals, NEVER 3 (CONVENTIONS.md). Callers pass an
 * already-computed amount; this never does float arithmetic beyond `toFixed(2)`.
 */
export function dosDecimales(valor: number): string {
  return valor.toFixed(2);
}

/** Read-only money display: a formatted 2-decimal amount, or an em dash when the value is absent
 * (used while a projection field is not yet available server-side). */
export function importeOpcional(valor: number | null | undefined): string {
  return valor === null || valor === undefined ? '—' : dosDecimales(valor);
}

/**
 * LOCAL `yyyy-MM-dd` (NOT `toISOString`, which is UTC and can shift the day at the boundaries).
 * Shared by the bandeja date-range filter and the tipo de cambio screen default range (design D4).
 */
export function fechaIso(d: Date): string {
  const mes = String(d.getMonth() + 1).padStart(2, '0');
  const dia = String(d.getDate()).padStart(2, '0');
  return `${d.getFullYear()}-${mes}-${dia}`;
}

/**
 * Default tipo de cambio range: the first day of `hoy`'s month .. `hoy`, formatted in LOCAL time
 * (spa spec req 4 -- "defaults 1st-of-month / today LOCAL not UTC").
 */
export function rangoMesActual(hoy: Date = new Date()): { desde: string; hasta: string } {
  return {
    desde: fechaIso(new Date(hoy.getFullYear(), hoy.getMonth(), 1)),
    hasta: fechaIso(hoy),
  };
}
