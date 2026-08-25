import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { aplicarTemaInicial, leerPreferenciaAlmacenada, resolverTema, TemaService } from './tema.service';

/**
 * tasks.md 3.1 (RED first) + 3.9 (RED+GREEN threat-matrix: tampered localStorage value) --
 * design.md D1: `data-tema` always explicit, `'sistema'` resolved in TS via `matchMedia`.
 * jsdom does not implement `matchMedia` (documented gotcha) -- every test that needs system
 * preference resolution stubs it explicitly.
 */
describe('resolverTema', () => {
  it.each([
    ['claro', false, 'claro'],
    ['claro', true, 'claro'],
    ['oscuro', false, 'oscuro'],
    ['oscuro', true, 'oscuro'],
    ['sistema', false, 'claro'],
    ['sistema', true, 'oscuro'],
  ] as const)('resolverTema(%s, prefiereOscuro=%s) -> %s', (preferencia, prefiereOscuro, esperado) => {
    expect(resolverTema(preferencia, prefiereOscuro)).toBe(esperado);
  });
});

describe('leerPreferenciaAlmacenada', () => {
  it('returns the stored value when it is one of the valid options', () => {
    expect(leerPreferenciaAlmacenada({ getItem: () => 'oscuro' })).toBe('oscuro');
  });

  it('returns sistema when nothing is stored', () => {
    expect(leerPreferenciaAlmacenada({ getItem: () => null })).toBe('sistema');
  });

  it('returns sistema for a tampered/invalid stored value (threat-matrix client-input-trust)', () => {
    expect(leerPreferenciaAlmacenada({ getItem: () => 'javascript:alert(1)' })).toBe('sistema');
    expect(leerPreferenciaAlmacenada({ getItem: () => '' })).toBe('sistema');
    expect(leerPreferenciaAlmacenada({ getItem: () => 'CLARO' })).toBe('sistema');
  });
});

describe('aplicarTemaInicial', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-tema');
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('applies the stored explicit preference without needing matchMedia', () => {
    localStorage.setItem('fact.tema', 'oscuro');
    aplicarTemaInicial();
    expect(document.documentElement.dataset['tema']).toBe('oscuro');
  });

  it('falls back to system resolution via matchMedia when nothing is stored', () => {
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: true, media: query }) as MediaQueryList);
    aplicarTemaInicial();
    expect(document.documentElement.dataset['tema']).toBe('oscuro');
  });

  it('a tampered localStorage value falls back to sistema resolution, not an error', () => {
    localStorage.setItem('fact.tema', 'no-es-un-tema-valido');
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: false, media: query }) as MediaQueryList);
    expect(() => aplicarTemaInicial()).not.toThrow();
    expect(document.documentElement.dataset['tema']).toBe('claro');
  });
});

describe('TemaService', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('data-tema');
    vi.stubGlobal('matchMedia', (query: string) => ({ matches: false, media: query }) as MediaQueryList);
    TestBed.configureTestingModule({});
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('starts from the stored/system preference and writes it to the DOM on construction', () => {
    localStorage.setItem('fact.tema', 'oscuro');
    const servicio = TestBed.inject(TemaService);

    expect(servicio.preferencia()).toBe('oscuro');
    expect(servicio.efectivo()).toBe('oscuro');
    expect(document.documentElement.dataset['tema']).toBe('oscuro');
  });

  it('establecer() updates the signal, the DOM, and persists to localStorage', () => {
    const servicio = TestBed.inject(TemaService);

    servicio.establecer('oscuro');

    expect(servicio.preferencia()).toBe('oscuro');
    expect(servicio.efectivo()).toBe('oscuro');
    expect(document.documentElement.dataset['tema']).toBe('oscuro');
    expect(localStorage.getItem('fact.tema')).toBe('oscuro');
  });
});
