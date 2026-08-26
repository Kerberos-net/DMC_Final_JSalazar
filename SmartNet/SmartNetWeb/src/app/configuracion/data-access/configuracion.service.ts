import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ConfiguracionEntrada } from '../models/configuracion.model';

/**
 * Server state for `GET/PUT /api/configuracion` (design D6, ADR 0009: signals in
 * `providedIn: 'root'` services, private writable signal + `asReadonly()`, following
 * `InboxService`'s pattern). `actualizar()` updates the in-memory entry from the response it
 * already sent (no refetch round-trip) — the server is still the source of truth for validation,
 * this is a purely local optimistic-consistency shortcut once the write already succeeded.
 */
@Injectable({ providedIn: 'root' })
export class ConfiguracionService {
  private readonly http = inject(HttpClient);

  private readonly entradasSignal = signal<ConfiguracionEntrada[]>([]);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);

  readonly entradas = this.entradasSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();

  async cargar(seccion?: string | null): Promise<void> {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);

    let params = new HttpParams();
    if (seccion) {
      params = params.set('seccion', seccion);
    }

    try {
      const respuesta = await firstValueFrom(
        this.http.get<ConfiguracionEntrada[]>('/api/configuracion', { params })
      );
      this.entradasSignal.set([...respuesta]);
    } catch (err) {
      this.errorSignal.set('No se pudo cargar la configuración.');
      throw err;
    } finally {
      this.loadingSignal.set(false);
    }
  }

  /** Rejects (propagates the HTTP error) on an invalid/unknown key -- the caller (feature
   * container) reads `err.error` as `ProblemaDetails` (spec.md "screen surfaces a rejected
   * write"), same pattern as `detalle-page.ts`'s `manejarError`. */
  async actualizar(seccion: string, clave: string, valor: string | null): Promise<void> {
    await firstValueFrom(
      this.http.put(`/api/configuracion/${seccion}/${clave}`, { valor })
    );
    this.entradasSignal.set(
      this.entradasSignal().map((entrada) =>
        entrada.seccion === seccion && entrada.clave === clave ? { ...entrada, valor } : entrada
      )
    );
  }
}
