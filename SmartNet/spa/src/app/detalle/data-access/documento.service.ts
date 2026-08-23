import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { DocumentoRespuesta } from '../models/documento.model';

/**
 * Server state for `GET /api/facturas/{id}/documentos` (unified list, design D1) and the
 * same-origin `/contenido` URL the visor `<iframe>` consumes (ADR 0009 signals pattern).
 */
@Injectable({ providedIn: 'root' })
export class DocumentoService {
  private readonly http = inject(HttpClient);

  private readonly documentosSignal = signal<DocumentoRespuesta[]>([]);
  private readonly loadingSignal = signal(false);

  readonly documentos = this.documentosSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();

  async cargar(facturaId: number): Promise<void> {
    this.loadingSignal.set(true);
    try {
      const documentos = await firstValueFrom(
        this.http.get<DocumentoRespuesta[]>(`/api/facturas/${facturaId}/documentos`)
      );
      this.documentosSignal.set(documentos);
    } finally {
      this.loadingSignal.set(false);
    }
  }

  /** design D2 -- mismo-origen, servido con `nosniff` + `Content-Disposition: inline`. */
  contenidoUrl(id: string): string {
    return `/api/documentos/${encodeURIComponent(id)}/contenido`;
  }
}
