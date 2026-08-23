import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/**
 * Client-side mirror of the `__Host-session` cookie state (ADR 0009: signals in
 * `providedIn: 'root'` services, private writable signal + public `asReadonly()`).
 * The cookie itself is HttpOnly and same-origin (`SameSite=Lax`, BACKLOG #2) — the browser
 * attaches it automatically to same-origin requests, so this service never reads or writes
 * it directly. It only mirrors what `GET /api/sesion` reports (200 `{ nombreUsuario }` | 401).
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly http = inject(HttpClient);

  private readonly usuarioSignal = signal<string | null>(null);

  readonly usuario = this.usuarioSignal.asReadonly();
  readonly autenticado = computed(() => this.usuarioSignal() !== null);

  /** Asks the API for the current session state and mirrors the result. Never throws. */
  async verificar(): Promise<boolean> {
    try {
      const respuesta = await firstValueFrom(
        this.http.get<{ nombreUsuario: string }>('/api/sesion')
      );
      this.usuarioSignal.set(respuesta.nombreUsuario);
      return true;
    } catch {
      this.usuarioSignal.set(null);
      return false;
    }
  }

  /** Clears the locally mirrored session state (e.g. after a 401 or explicit logout). */
  limpiar(): void {
    this.usuarioSignal.set(null);
  }
}
