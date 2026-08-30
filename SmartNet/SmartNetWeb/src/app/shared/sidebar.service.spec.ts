import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { EstadoSidebar, leerEstadoAlmacenado, SidebarService } from './sidebar.service';

/**
 * tasks.md 1.1 (RED first) — design D5: the sidebar collapse preference is client-only, persisted
 * in `localStorage` under `fact.sidebar`, mirroring `tema.service.ts`. Any value outside the
 * allowlist falls back to `'expandido'` and never throws (spec `spa-shell-nav`: "Corrupt or absent
 * value falls back to expanded"; threat-matrix "client input trust").
 */
describe('leerEstadoAlmacenado', () => {
  it('returns the stored value when it is one of the valid options', () => {
    expect(leerEstadoAlmacenado({ getItem: () => 'colapsado' })).toBe('colapsado');
    expect(leerEstadoAlmacenado({ getItem: () => 'expandido' })).toBe('expandido');
  });

  it('returns expandido when nothing is stored', () => {
    expect(leerEstadoAlmacenado({ getItem: () => null })).toBe('expandido');
  });

  it('returns expandido for a tampered/invalid stored value without throwing', () => {
    expect(leerEstadoAlmacenado({ getItem: () => 'javascript:alert(1)' })).toBe('expandido');
    expect(leerEstadoAlmacenado({ getItem: () => '' })).toBe('expandido');
    expect(leerEstadoAlmacenado({ getItem: () => 'COLAPSADO' })).toBe('expandido');
  });
});

describe('SidebarService', () => {
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({});
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('starts expanded when nothing is stored', () => {
    const servicio = TestBed.inject(SidebarService);

    expect(servicio.estado()).toBe('expandido');
    expect(servicio.colapsado()).toBe(false);
  });

  it('starts from the stored preference', () => {
    localStorage.setItem('fact.sidebar', 'colapsado');
    const servicio = TestBed.inject(SidebarService);

    expect(servicio.estado()).toBe('colapsado');
    expect(servicio.colapsado()).toBe(true);
  });

  it('alternar() flips the state, updates the signal, and persists to localStorage', () => {
    const servicio = TestBed.inject(SidebarService);

    servicio.alternar();
    expect(servicio.estado()).toBe('colapsado');
    expect(servicio.colapsado()).toBe(true);
    expect(localStorage.getItem('fact.sidebar')).toBe('colapsado');

    servicio.alternar();
    expect(servicio.estado()).toBe('expandido');
    expect(localStorage.getItem('fact.sidebar')).toBe('expandido');
  });

  it('a tampered localStorage value resolves to expandido, not an error', () => {
    localStorage.setItem('fact.sidebar', 'no-es-un-estado-valido');
    const servicio: SidebarService = TestBed.inject(SidebarService);

    expect(servicio.estado()).toBe<EstadoSidebar>('expandido');
  });
});
