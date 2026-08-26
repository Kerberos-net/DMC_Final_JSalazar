import { Injectable, computed, signal } from '@angular/core';

/**
 * design.md D1: `data-tema` is always explicit on `<html>`; `'sistema'` is resolved in TS via
 * `matchMedia`, never a bare `prefers-color-scheme` media-query override (would duplicate the dark
 * token block -- plain CSS, no SCSS mixin, per `angular.json`). `aplicarTemaInicial()` runs from
 * `main.ts` before `bootstrapApplication` so there is no flash of the wrong theme; `TemaService`
 * (signals, ADR 0009) is the reactive counterpart the theme-toggle control binds to at runtime.
 */
export type PreferenciaTema = 'claro' | 'oscuro' | 'sistema';
export type TemaEfectivo = 'claro' | 'oscuro';

const CLAVE_ALMACENAMIENTO = 'fact.tema';
const VALORES_VALIDOS: readonly PreferenciaTema[] = ['claro', 'oscuro', 'sistema'];

function esPreferenciaValida(valor: string | null): valor is PreferenciaTema {
  return valor !== null && (VALORES_VALIDOS as readonly string[]).includes(valor);
}

/**
 * spec.md threat-matrix "client input trust": any stored value outside the allowlist -- tampered,
 * empty, wrong case -- falls back to `'sistema'`, never throws and never reaches the DOM raw.
 */
export function leerPreferenciaAlmacenada(
  storage: Pick<Storage, 'getItem'> = localStorage
): PreferenciaTema {
  const valor = storage.getItem(CLAVE_ALMACENAMIENTO);
  return esPreferenciaValida(valor) ? valor : 'sistema';
}

/** Pure per design.md's Contracts section -- no DOM, no storage access. */
export function resolverTema(preferencia: PreferenciaTema, prefiereOscuro: boolean): TemaEfectivo {
  if (preferencia === 'sistema') {
    return prefiereOscuro ? 'oscuro' : 'claro';
  }
  return preferencia;
}

function prefiereOscuroDelSistema(): boolean {
  return typeof matchMedia === 'function' && matchMedia('(prefers-color-scheme: dark)').matches;
}

/** `main.ts`, pre-bootstrap (design D1) -- must not depend on Angular DI. */
export function aplicarTemaInicial(): void {
  const preferencia = leerPreferenciaAlmacenada();
  const efectivo = resolverTema(preferencia, prefiereOscuroDelSistema());
  document.documentElement.dataset['tema'] = efectivo;
}

@Injectable({ providedIn: 'root' })
export class TemaService {
  private readonly preferenciaSignal = signal<PreferenciaTema>(leerPreferenciaAlmacenada());

  readonly preferencia = this.preferenciaSignal.asReadonly();
  readonly efectivo = computed(() => resolverTema(this.preferenciaSignal(), prefiereOscuroDelSistema()));

  constructor() {
    this.aplicarAlDom(this.efectivo());
  }

  establecer(preferencia: PreferenciaTema): void {
    localStorage.setItem(CLAVE_ALMACENAMIENTO, preferencia);
    this.preferenciaSignal.set(preferencia);
    this.aplicarAlDom(this.efectivo());
  }

  private aplicarAlDom(tema: TemaEfectivo): void {
    document.documentElement.dataset['tema'] = tema;
  }
}
