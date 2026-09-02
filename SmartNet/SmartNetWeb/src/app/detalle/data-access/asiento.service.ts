import { HttpClient, HttpResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AsientoRespuesta, FacturaAsientoRespuesta, LineaAsientoRequest } from '../models/asiento.model';

/**
 * Server state for the asiento side of the detail screen (ADR 0009 signals pattern). Every línea
 * route CASes `fact.AsientoContable.Version` (design D7), so this service always threads the
 * latest ETag forward and replaces `asiento()` wholesale from each response — no client-side
 * merging of líneas, matching `InboxService`'s "no client-side merging" convention.
 */
@Injectable({ providedIn: 'root' })
export class AsientoService {
  private readonly http = inject(HttpClient);

  private readonly asientoSignal = signal<AsientoRespuesta | null>(null);
  private readonly etagSignal = signal<string | null>(null);
  private readonly loadingSignal = signal(false);

  readonly asiento = this.asientoSignal.asReadonly();
  readonly etag = this.etagSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();

  /** design D3 -- resuelve factura-&gt;asiento vigente; `body.asiento` es `null` sin asiento vigente
   * (spec.md: distinto de un 404 de factura desconocida, que ya rechazó la promesa). */
  async cargarPorFactura(facturaId: number): Promise<void> {
    this.loadingSignal.set(true);
    try {
      const respuesta = await firstValueFrom(
        this.http.get<FacturaAsientoRespuesta>(`/api/facturas/${facturaId}/asiento`, { observe: 'response' })
      );
      this.asientoSignal.set(respuesta.body?.asiento ?? null);
      this.etagSignal.set(respuesta.headers.get('ETag'));
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async cargar(asientoId: number): Promise<void> {
    this.loadingSignal.set(true);
    try {
      const respuesta = await firstValueFrom(
        this.http.get<AsientoRespuesta>(`/api/asientos/${asientoId}`, { observe: 'response' })
      );
      this.aplicar(respuesta);
    } finally {
      this.loadingSignal.set(false);
    }
  }

  async actualizarLinea(asientoId: number, lineaId: number, linea: LineaAsientoRequest): Promise<void> {
    const respuesta = await firstValueFrom(
      this.http.patch<AsientoRespuesta>(`/api/asientos/${asientoId}/lineas/${lineaId}`, linea, {
        headers: { 'If-Match': this.etagRequerido() },
        observe: 'response',
      })
    );
    this.aplicar(respuesta);
  }

  async eliminarLinea(asientoId: number, lineaId: number): Promise<void> {
    const respuesta = await firstValueFrom(
      this.http.delete<AsientoRespuesta>(`/api/asientos/${asientoId}/lineas/${lineaId}`, {
        headers: { 'If-Match': this.etagRequerido() },
        observe: 'response',
      })
    );
    this.aplicar(respuesta);
  }

  /** `POST .../lineas` solo devuelve `{ lineaId }` (design D2 -- 201 Created), no el asiento
   * completo -- se recarga con `cargar()` para obtener la lista de líneas actualizada. */
  async agregarLinea(asientoId: number, linea: LineaAsientoRequest): Promise<void> {
    await firstValueFrom(
      this.http.post(`/api/asientos/${asientoId}/lineas`, linea, {
        headers: { 'If-Match': this.etagRequerido() },
      })
    );
    await this.cargar(asientoId);
  }

  /** design C1/E -- `POST /api/asientos/{id}/recomponer`: regenera la semilla del motor (líneas +
   * cabecera) descartando las ediciones manuales. `If-Match` obligatorio (428 sin él); cuerpo
   * opcional `{ cuentaCodigo }` para fijar la cuenta del cargo por defecto. Devuelve el
   * `AsientoRespuesta` completo + nuevo ETag, igual que `actualizarLinea`. */
  async recomponer(asientoId: number, cuentaCodigo?: string | null): Promise<void> {
    const respuesta = await firstValueFrom(
      this.http.post<AsientoRespuesta>(
        `/api/asientos/${asientoId}/recomponer`,
        cuentaCodigo ? { cuentaCodigo } : null,
        {
          headers: { 'If-Match': this.etagRequerido() },
          observe: 'response',
        }
      )
    );
    this.aplicar(respuesta);
  }

  private aplicar(respuesta: HttpResponse<AsientoRespuesta>): void {
    this.asientoSignal.set(respuesta.body);
    this.etagSignal.set(respuesta.headers.get('ETag'));
  }

  private etagRequerido(): string {
    const etag = this.etagSignal();
    if (etag === null) {
      throw new Error('AsientoService: no hay ETag mirroreado -- cargar() debe llamarse antes de escribir.');
    }
    return etag;
  }
}
