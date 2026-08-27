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
