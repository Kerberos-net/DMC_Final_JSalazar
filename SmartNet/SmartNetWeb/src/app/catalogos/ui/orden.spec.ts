import { describe, expect, it } from 'vitest';
import { alternarOrden, flechaOrden, ordenarPor, type EstadoOrden } from './orden';

/**
 * tasks.md 3.2 (RED first, design D8) -- `orden.ts` is PURE module functions, not a component:
 * the sortable header toggle + arrow glyph selector shared by the plan-contable and tipo-de-cambio
 * screens (both sort client-side over bounded sets). No side effects, deterministic.
 */
describe('orden -- alternarOrden', () => {
  it('toggles asc -> desc on the same field', () => {
    expect(alternarOrden({ campo: 'cuenta', direccion: 'asc' }, 'cuenta')).toEqual({
      campo: 'cuenta',
      direccion: 'desc',
    });
  });

  it('toggles desc -> asc on the same field', () => {
    expect(alternarOrden({ campo: 'cuenta', direccion: 'desc' }, 'cuenta')).toEqual({
      campo: 'cuenta',
      direccion: 'asc',
    });
  });

  it('resets to asc when switching field', () => {
    expect(alternarOrden({ campo: 'cuenta', direccion: 'desc' }, 'descripcion')).toEqual({
      campo: 'descripcion',
      direccion: 'asc',
    });
  });

  it('is pure: does not mutate the input', () => {
    const actual: EstadoOrden = { campo: 'cuenta', direccion: 'asc' };
    alternarOrden(actual, 'cuenta');
    expect(actual).toEqual({ campo: 'cuenta', direccion: 'asc' });
  });
});

describe('orden -- flechaOrden', () => {
  it('returns the up arrow for the active asc field', () => {
    expect(flechaOrden({ campo: 'cuenta', direccion: 'asc' }, 'cuenta')).toBe('▲');
  });

  it('returns the down arrow for the active desc field', () => {
    expect(flechaOrden({ campo: 'cuenta', direccion: 'desc' }, 'cuenta')).toBe('▼');
  });

  it('returns an empty string for an inactive field', () => {
    expect(flechaOrden({ campo: 'cuenta', direccion: 'asc' }, 'descripcion')).toBe('');
  });
});

describe('orden -- ordenarPor (Intl.Collator es)', () => {
  it('sorts strings with Spanish collation ascending', () => {
    const filas = [{ n: 'nandu' }, { n: 'zorro' }, { n: 'anfora' }];
    expect(ordenarPor(filas, (f) => f.n, 'asc').map((f) => f.n)).toEqual(['anfora', 'nandu', 'zorro']);
  });

  it('reverses for desc', () => {
    const filas = [{ n: 'a' }, { n: 'b' }, { n: 'c' }];
    expect(ordenarPor(filas, (f) => f.n, 'desc').map((f) => f.n)).toEqual(['c', 'b', 'a']);
  });

  it('orders numerically, not lexically', () => {
    const filas = [{ v: 10 }, { v: 2 }, { v: 1 }];
    expect(ordenarPor(filas, (f) => f.v, 'asc').map((f) => f.v)).toEqual([1, 2, 10]);
  });

  it('pushes null keys to the end regardless of direction', () => {
    const filas = [{ v: 'b' as string | null }, { v: null }, { v: 'a' }];
    expect(ordenarPor(filas, (f) => f.v, 'asc').map((f) => f.v)).toEqual(['a', 'b', null]);
    expect(ordenarPor(filas, (f) => f.v, 'desc').map((f) => f.v)).toEqual(['b', 'a', null]);
  });

  it('does not mutate the source array', () => {
    const filas = [{ v: 2 }, { v: 1 }];
    ordenarPor(filas, (f) => f.v, 'asc');
    expect(filas.map((f) => f.v)).toEqual([2, 1]);
  });
});
