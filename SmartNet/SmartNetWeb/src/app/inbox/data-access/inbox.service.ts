import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  BandejaItem,
  FiltrosBandeja,
  PaginaBandeja,
  ResumenBandeja,
} from '../models/bandeja-item.model';

/**
 * Server state for the Inbox screen (ADR 0009: signals in `providedIn: 'root'` services,
 * private writable signal + public `asReadonly()`). `GET /api/bandeja?estado=&desde=&hasta=
 * &proveedor=&pagina=&orden=` already returns the combined, paginated view (ADR 0003/0009,
 * BACKLOG #13 design D2-D5/D7) -- this service does no client-side merging or pagination math.
 */
@Injectable({ providedIn: 'root' })
export class InboxService {
  private readonly http = inject(HttpClient);

  private readonly itemsSignal = signal<BandejaItem[]>([]);
  private readonly loadingSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);
  private readonly paginaSignal = signal(1);
  private readonly tamanioPaginaSignal = signal(20);
  private readonly totalRegistrosSignal = signal(0);
  private readonly totalPaginasSignal = signal(0);
  private readonly resumenSignal = signal<ResumenBandeja | null>(null);
  private readonly ultimosFiltrosSignal = signal<FiltrosBandeja>({});

  readonly items = this.itemsSignal.asReadonly();
  readonly loading = this.loadingSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();
  readonly pagina = this.paginaSignal.asReadonly();
  readonly tamanioPagina = this.tamanioPaginaSignal.asReadonly();
  readonly totalRegistros = this.totalRegistrosSignal.asReadonly();
  readonly totalPaginas = this.totalPaginasSignal.asReadonly();
  /** BACKLOG #21 -- global estado aggregate for the summary cards; `null` until the first load. */
  readonly resumen = this.resumenSignal.asReadonly();
  /** design.md Data Flow -- reused by `InboxPage` to refetch the same page after `reprocesar()`. */
  readonly ultimosFiltros = this.ultimosFiltrosSignal.asReadonly();

  async cargar(filtros: FiltrosBandeja): Promise<void> {
    this.loadingSignal.set(true);
    this.errorSignal.set(null);

    let params = new HttpParams().set('orden', filtros.orden ?? 'desc');
    if (filtros.estado) {
      params = params.set('estado', filtros.estado);
    }
    if (filtros.estadoDerivado) {
      // Sent even for 'TODOS' — the API's no-param default is the NARROW non-terminal view; the
      // "Todos" chip must reach the wide predicate (`estadoDerivado=TODOS`).
      params = params.set('estadoDerivado', filtros.estadoDerivado);
    }
    if (filtros.desde) {
      params = params.set('desde', filtros.desde);
    }
    if (filtros.hasta) {
      params = params.set('hasta', filtros.hasta);
    }
    if (filtros.proveedor) {
      params = params.set('proveedor', filtros.proveedor);
    }
    if (filtros.pagina) {
      params = params.set('pagina', String(filtros.pagina));
    }

    try {
      const respuesta = await firstValueFrom(
        this.http.get<PaginaBandeja<BandejaItem>>('/api/bandeja', { params })
      );
      this.itemsSignal.set([...respuesta.items]);
      this.paginaSignal.set(respuesta.pagina);
      this.tamanioPaginaSignal.set(respuesta.tamanioPagina);
      this.totalRegistrosSignal.set(respuesta.totalRegistros);
      this.totalPaginasSignal.set(respuesta.totalPaginas);
      this.resumenSignal.set(respuesta.resumen ?? null);
      this.ultimosFiltrosSignal.set(filtros);
    } catch (err) {
      this.errorSignal.set('No se pudo cargar la bandeja.');
      throw err;
    } finally {
      this.loadingSignal.set(false);
    }
  }

  /**
   * BACKLOG #13 -- `{id}` MUST be `ProcesamientoId` (api-incidencias-integraciones spec.md), not
   * `InboxEventId`/`FacturaId`. The route already exists (#11); this only adds the client call.
   */
  async reprocesar(procesamientoId: number): Promise<void> {
    await firstValueFrom(this.http.post(`/api/incidencias/${procesamientoId}/reprocesar`, {}));
  }
}
