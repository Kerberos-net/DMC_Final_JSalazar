import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BandejaItem, EstadoConsumo, OrdenFecha } from '../models/bandeja-item.model';

/**
 * Server state for the Inbox screen (ADR 0009: signals in `providedIn: 'root'` services,
 * private writable signal + public `asReadonly()`). `GET /api/bandeja?estado=&orden=` already
 * returns the combined view (ADR 0003/0009) — this service does no client-side merging.
 */
@Injectable({ providedIn: 'root' })
export class InboxService {
  private readonly http = inject(HttpClient);

  private readonly itemsSignal = signal<BandejaItem[]>([]);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);

  readonly items = this.itemsSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();

  async cargar(estado: EstadoConsumo | null, orden: OrdenFecha): Promise<void> {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);

    let params = new HttpParams().set('orden', orden);
    if (estado) {
      params = params.set('estado', estado);
    }

    try {
      const items = await firstValueFrom(
        this.http.get<BandejaItem[]>('/api/bandeja', { params })
      );
      this.itemsSignal.set(items);
    } catch (err) {
      this.errorSignal.set('No se pudo cargar la bandeja.');
      throw err;
    } finally {
      this.loadingSignal.set(false);
    }
  }
}
