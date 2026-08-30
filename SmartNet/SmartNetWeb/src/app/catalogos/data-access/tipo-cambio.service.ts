import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { TipoCambioHistorico, TipoCambioRespuesta } from '../models/tipo-cambio.model';

/**
 * Server state for the tipo de cambio screen (BACKLOG #22 PR8, spa spec req 4, design D8).
 * `GET /api/tipos-cambio?desde=&hasta=` returns a bounded range (the endpoint caps the inclusive
 * span at 366 days) in one unpaged response; the screen sorts it client-side, so this service only
 * ever holds the current range's rows. Every range change re-queries via `cargar`.
 *
 * On a 400 (invalid/inverted/oversized range) the list is CLEARED and a non-blocking validation
 * message is exposed -- stale rows must never be shown as if they matched the new range.
 */
@Injectable({ providedIn: 'root' })
export class TipoCambioService {
  private readonly http = inject(HttpClient);

  private readonly itemsSignal = signal<readonly TipoCambioHistorico[]>([]);
  private readonly cargandoSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);

  readonly items = this.itemsSignal.asReadonly();
  readonly cargando = this.cargandoSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();

  async cargar(desde: string, hasta: string): Promise<void> {
    this.cargandoSignal.set(true);
    this.errorSignal.set(null);
    const params = new HttpParams().set('desde', desde).set('hasta', hasta);
    try {
      const respuesta = await firstValueFrom(
        this.http.get<TipoCambioRespuesta>('/api/tipos-cambio', { params })
      );
      this.itemsSignal.set([...respuesta.items]);
    } catch (err) {
      this.itemsSignal.set([]);
      this.errorSignal.set(
        err instanceof HttpErrorResponse && err.status === 400
          ? 'El rango de fechas no es válido.'
          : 'No se pudo cargar el tipo de cambio.'
      );
      throw err;
    } finally {
      this.cargandoSignal.set(false);
    }
  }
}
