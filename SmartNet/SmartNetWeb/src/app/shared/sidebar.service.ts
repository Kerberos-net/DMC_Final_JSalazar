import { computed, Injectable, signal } from '@angular/core';

/**
 * design D5: the sidebar collapse preference is a per-viewer, client-only convenience — persisted in
 * `localStorage` under `fact.sidebar`, exactly like `TemaService` persists the theme. There is no
 * backend for it (spec `spa-shell-nav`: "Collapsed state persists per viewer in localStorage").
 * The sidebar has no pre-bootstrap markup, so — unlike the theme — no `main.ts` applier is needed:
 * the service reads storage synchronously in its field initializer, before the first render.
 */
export type EstadoSidebar = 'expandido' | 'colapsado';

const CLAVE_ALMACENAMIENTO = 'fact.sidebar';
const VALORES_VALIDOS: readonly EstadoSidebar[] = ['expandido', 'colapsado'];

function esEstadoValido(valor: string | null): valor is EstadoSidebar {
  return valor !== null && (VALORES_VALIDOS as readonly string[]).includes(valor);
}

/**
 * spec `spa-shell-nav` "client input trust": any stored value outside the allowlist — tampered,
 * empty, wrong case — falls back to `'expandido'`, never throws and never reaches the DOM raw.
 */
export function leerEstadoAlmacenado(
  storage: Pick<Storage, 'getItem'> = localStorage
): EstadoSidebar {
  const valor = storage.getItem(CLAVE_ALMACENAMIENTO);
  return esEstadoValido(valor) ? valor : 'expandido';
}

@Injectable({ providedIn: 'root' })
export class SidebarService {
  private readonly estadoSignal = signal<EstadoSidebar>(leerEstadoAlmacenado());

  readonly estado = this.estadoSignal.asReadonly();
  readonly colapsado = computed(() => this.estadoSignal() === 'colapsado');

  alternar(): void {
    const siguiente: EstadoSidebar =
      this.estadoSignal() === 'colapsado' ? 'expandido' : 'colapsado';
    localStorage.setItem(CLAVE_ALMACENAMIENTO, siguiente);
    this.estadoSignal.set(siguiente);
  }
}
