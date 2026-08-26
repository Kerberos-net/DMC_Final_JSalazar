import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { EntradaAuditoriaRespuesta } from '../models/historial.model';

/**
 * Server state for `GET /api/facturas/{id}/historial` (design.md D7), ADR 0009 signals pattern
 * (private writable signal + `asReadonly()`, matching `FacturaService`/`AsientoService`). No ETag
 * -- this is a read-only side channel, never written back to.
 */
@Injectable({ providedIn: 'root' })
export class HistorialService {
  private readonly http = inject(HttpClient);

  private readonly entradasSignal = signal<readonly EntradaAuditoriaRespuesta[]>([]);
  private readonly loadingSignal = signal(false);

  readonly entradas = this.entradasSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();

  async cargar(facturaId: number): Promise<void> {
    this.loadingSignal.set(true);
    try {
      const entradas = await firstValueFrom(
        this.http.get<EntradaAuditoriaRespuesta[]>(`/api/facturas/${facturaId}/historial`)
      );
      this.entradasSignal.set(entradas);
    } finally {
      this.loadingSignal.set(false);
    }
  }
}
