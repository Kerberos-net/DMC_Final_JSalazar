import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/**
 * BACKLOG #22, design D8 -- shared blob-download helper for the three `/exportacion` routes.
 * `http.get(..., { responseType: 'blob', observe: 'response' })` -- NOT `window.open` (a 401 in a
 * new tab bypasses `httpErrorInterceptor`'s redirect). Filename comes from Content-Disposition
 * (same-origin, ADR 0012). On error the promise rejects; `descargando` is always cleared.
 */
@Injectable({ providedIn: 'root' })
export class DescargaXlsx {
  private readonly http = inject(HttpClient);

  private readonly descargandoSignal = signal(false);
  readonly descargando = this.descargandoSignal.asReadonly();

  async descargar(url: string, params: Record<string, string | number> = {}): Promise<void> {
    this.descargandoSignal.set(true);
    try {
      const httpParams = Object.entries(params).reduce(
        (acc, [clave, valor]) => acc.set(clave, String(valor)),
        new HttpParams()
      );
      const respuesta = await firstValueFrom(
        this.http.get(url, { params: httpParams, responseType: 'blob', observe: 'response' })
      );
      const blob = respuesta.body ?? new Blob();
      const nombre = nombreDesdeContentDisposition(respuesta.headers.get('Content-Disposition'));
      this.dispararDescarga(blob, nombre);
    } finally {
      this.descargandoSignal.set(false);
    }
  }

  private dispararDescarga(blob: Blob, nombre: string): void {
    const objectUrl = URL.createObjectURL(blob);
    try {
      const ancla = document.createElement('a');
      ancla.href = objectUrl;
      ancla.download = nombre;
      ancla.rel = 'noopener';
      document.body.appendChild(ancla);
      ancla.click();
      ancla.remove();
    } finally {
      URL.revokeObjectURL(objectUrl);
    }
  }
}

/**
 * Extracts the download filename from a Content-Disposition header. Handles the RFC 5987
 * `filename*=UTF-8''...` form and the plain quoted `filename="..."` form; falls back to
 * `export.xlsx` when the header is absent or unparseable.
 */
export function nombreDesdeContentDisposition(cabecera: string | null): string {
  if (!cabecera) {
    return 'export.xlsx';
  }
  const extendido = /filename\*=(?:UTF-8'')?([^;]+)/i.exec(cabecera);
  if (extendido) {
    try {
      return decodeURIComponent(extendido[1].trim().replace(/^"|"$/g, ''));
    } catch {
      return extendido[1].trim().replace(/^"|"$/g, '');
    }
  }
  const simple = /filename="?([^";]+)"?/i.exec(cabecera);
  return simple ? simple[1].trim() : 'export.xlsx';
}
