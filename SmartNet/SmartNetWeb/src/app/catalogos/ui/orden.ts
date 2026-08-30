/**
 * BACKLOG #22, design D8 -- client-side column sort shared by the plan-contable and
 * tipo-de-cambio screens (bounded sets). PURE module functions, not a component. Deterministic,
 * no side effects.
 */

export type Direccion = 'asc' | 'desc';

export interface EstadoOrden<C extends string = string> {
  readonly campo: C;
  readonly direccion: Direccion;
}

/** Same field -> flip direction; different field -> select it ascending. */
export function alternarOrden<C extends string>(actual: EstadoOrden<C>, campo: C): EstadoOrden<C> {
  if (actual.campo === campo) {
    return { campo, direccion: actual.direccion === 'asc' ? 'desc' : 'asc' };
  }
  return { campo, direccion: 'asc' };
}

/** Arrow glyph for a header cell: up/down for the active field, empty otherwise. */
export function flechaOrden<C extends string>(actual: EstadoOrden<C>, campo: C): '▲' | '▼' | '' {
  if (actual.campo !== campo) {
    return '';
  }
  return actual.direccion === 'asc' ? '▲' : '▼';
}

/** The ONE module-level collator (design D8): Spanish locale, numeric-aware, accent-insensitive. */
const colador = new Intl.Collator('es', { numeric: true, sensitivity: 'base' });

/**
 * Returns a NEW array sorted by `clave`. `null` keys always sort last (both directions). String
 * keys use the Spanish collator; numeric keys compare numerically. `Array.prototype.sort` is
 * stable, so equal keys keep their prior order.
 */
export function ordenarPor<T>(
  filas: readonly T[],
  clave: (fila: T) => string | number | null,
  direccion: Direccion
): T[] {
  const factor = direccion === 'asc' ? 1 : -1;
  return [...filas].sort((a, b) => {
    const va = clave(a);
    const vb = clave(b);
    if (va === null && vb === null) {
      return 0;
    }
    if (va === null) {
      return 1;
    }
    if (vb === null) {
      return -1;
    }
    if (typeof va === 'number' && typeof vb === 'number') {
      return (va - vb) * factor;
    }
    return colador.compare(String(va), String(vb)) * factor;
  });
}
