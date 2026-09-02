import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { CorreccionFacturaRequest, FacturaRespuesta } from '../models/factura.model';

/**
 * Server state for `GET/PATCH /api/facturas/{id}` and `POST /api/facturas/{id}/validar`
 * (ADR 0009: signals in `providedIn: 'root'` services, private writable signal + `asReadonly()`,
 * following `InboxService`'s pattern). The ETag is read from the response header (design D2 —
 * "never duplicated in the body") and threaded back as `If-Match` on the next write (design D7).
 */
@Injectable({ providedIn: 'root' })
export class FacturaService {
  private readonly http = inject(HttpClient);

  private readonly facturaSignal = signal<FacturaRespuesta | null>(null);
  private readonly etagSignal = signal<string | null>(null);
  private readonly loadingSignal = signal(false);

  readonly factura = this.facturaSignal.asReadonly();
  readonly etag = this.etagSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();

  async cargar(id: number): Promise<void> {
    this.loadingSignal.set(true);
    try {
      const respuesta = await firstValueFrom(
        this.http.get<FacturaRespuesta>(`/api/facturas/${id}`, { observe: 'response' })
      );
      this.facturaSignal.set(respuesta.body);
      this.etagSignal.set(respuesta.headers.get('ETag'));
    } finally {
      this.loadingSignal.set(false);
    }
  }

  /** design D2: `If-Match` es obligatorio en toda escritura -- sin ETag mirroreado, es un bug del
   * cliente (428 -- nunca un estado de usuario, design D6), así que se lanza antes de llamar HTTP. */
  async guardar(id: number, correccion: CorreccionFacturaRequest): Promise<void> {
    const etag = this.etagRequerido();
    const respuesta = await firstValueFrom(
      this.http.patch<FacturaRespuesta>(`/api/facturas/${id}`, correccion, {
        headers: { 'If-Match': etag },
        observe: 'response',
      })
    );
    this.facturaSignal.set(respuesta.body);
    this.etagSignal.set(respuesta.headers.get('ETag'));
  }

  /** `POST /api/facturas/{id}/validar?fechaCorteContable=` -- sin cuerpo, sin `If-Match` (el
   * endpoint resuelve factura-&gt;asiento internamente, spec.md api-facturas). */
  async validar(id: number, fechaCorteContable: string): Promise<void> {
    const params = new HttpParams().set('fechaCorteContable', fechaCorteContable);
    await firstValueFrom(this.http.post<void>(`/api/facturas/${id}/validar`, null, { params }));
  }

  /** `POST /api/facturas/{id}/abrir` (design C/E) -- crea el asiento BORRADOR compuesto por el
   * motor si aún no existe (caso moneda extranjera sin TC vigente en la promoción). Sin cuerpo,
   * sin `If-Match`; idempotente en el servidor. 200 sin cuerpo -- el detalle recarga el asiento. */
  async abrir(id: number): Promise<void> {
    await firstValueFrom(this.http.post<void>(`/api/facturas/${id}/abrir`, null));
  }

  /** `POST /api/facturas/{id}/confirmar-afectacion` (design D10) -- misma forma CAS que
   * `guardar()`: `If-Match` obligatorio, respuesta trae la `FacturaRespuesta` completa + nuevo
   * ETag. Solo registra la afirmación del asistente; NO desbloquea `validar` (gate dormido). */
  async confirmarAfectacion(id: number, esMixta: boolean): Promise<void> {
    const etag = this.etagRequerido();
    const respuesta = await firstValueFrom(
      this.http.post<FacturaRespuesta>(
        `/api/facturas/${id}/confirmar-afectacion`,
        { esMixta },
        { headers: { 'If-Match': etag }, observe: 'response' }
      )
    );
    this.facturaSignal.set(respuesta.body);
    this.etagSignal.set(respuesta.headers.get('ETag'));
  }

  private etagRequerido(): string {
    const etag = this.etagSignal();
    if (etag === null) {
      throw new Error('FacturaService: no hay ETag mirroreado -- cargar() debe llamarse antes de escribir.');
    }
    return etag;
  }
}
