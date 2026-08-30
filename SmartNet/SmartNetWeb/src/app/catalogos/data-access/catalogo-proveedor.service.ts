import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { Direccion } from '../ui/orden';
import {
  ClaveOrdenProveedor,
  PaginaProveedores,
  ProveedorCatalogo,
} from '../models/proveedor-catalogo.model';

/**
 * Server state for the proveedores catalogo screen (BACKLOG #22 PR6, spa spec req 2, design D6/D7).
 * Unlike `PlanContableService`, NOTHING is done client-side: `q`, `orden`, `direccion`, `pagina`
 * and `tamanio` are request state, and any change re-queries
 * `GET /api/catalogos/proveedores?modo=catalogo`. The readonly signals hold the current server page
 * plus its `PaginaBandeja<T>` metadata. The search box is debounced exactly like the picker
 * `ProveedorService`; a size/sort/search change resets to page 1. This is a SEPARATE service from
 * the picker singleton -- it never writes the picker's signals (design D4).
 */
@Injectable({ providedIn: 'root' })
export class CatalogoProveedorService {
  private readonly http = inject(HttpClient);

  /** Debounce window in ms for the search box. Mutable so specs can zero it without fake timers. */
  debounceMs = 250;

  private readonly itemsSignal = signal<readonly ProveedorCatalogo[]>([]);
  private readonly paginaSignal = signal(1);
  private readonly tamanioPaginaSignal = signal(20);
  private readonly totalRegistrosSignal = signal(0);
  private readonly totalPaginasSignal = signal(0);
  private readonly cargandoSignal = signal(false);
  private readonly errorSignal = signal<string | null>(null);

  readonly items = this.itemsSignal.asReadonly();
  readonly pagina = this.paginaSignal.asReadonly();
  readonly tamanioPagina = this.tamanioPaginaSignal.asReadonly();
  readonly totalRegistros = this.totalRegistrosSignal.asReadonly();
  readonly totalPaginas = this.totalPaginasSignal.asReadonly();
  readonly cargando = this.cargandoSignal.asReadonly();
  readonly error = this.errorSignal.asReadonly();

  private consulta = '';
  private orden: ClaveOrdenProveedor = 'proveedor';
  private direccion: Direccion = 'asc';
  private tamanio = 20;
  private paginaSolicitada = 1;
  private temporizador: ReturnType<typeof setTimeout> | null = null;

  /** Initial load (page 1, default sort). */
  cargar(): void {
    this.programar(0);
  }

  /** Debounced: sets the search term and re-queries from page 1, keeping the active sort. */
  buscar(consulta: string): void {
    this.consulta = consulta.trim();
    this.paginaSolicitada = 1;
    this.programar(this.debounceMs);
  }

  /** Server sort change -> re-query from page 1. */
  ordenar(orden: ClaveOrdenProveedor, direccion: Direccion): void {
    this.orden = orden;
    this.direccion = direccion;
    this.paginaSolicitada = 1;
    this.programar(0);
  }

  /** Page step from the paginador. A no-op when the page is already the one requested. */
  irAPagina(pagina: number): void {
    if (pagina === this.paginaSolicitada) {
      return;
    }
    this.paginaSolicitada = pagina;
    this.programar(0);
  }

  /** Rows-per-page change -> re-query from page 1. */
  cambiarTamanio(tamanio: number): void {
    this.tamanio = tamanio;
    this.paginaSolicitada = 1;
    this.programar(0);
  }

  /**
   * Schedules a fetch on a timer, cancelling any pending one. This debounces the search box AND
   * coalesces the paginador's `tamanioChange` + `paginaChange(1)` burst into a single request.
   */
  private programar(delay: number): void {
    if (this.temporizador !== null) {
      clearTimeout(this.temporizador);
    }
    this.temporizador = setTimeout(() => {
      this.temporizador = null;
      void this.ejecutar();
    }, delay);
  }

  private async ejecutar(): Promise<void> {
    this.cargandoSignal.set(true);
    this.errorSignal.set(null);

    let params = new HttpParams()
      .set('modo', 'catalogo')
      .set('pagina', String(this.paginaSolicitada))
      .set('orden', this.orden)
      .set('direccion', this.direccion)
      .set('tamanio', String(this.tamanio));
    if (this.consulta.length > 0) {
      params = params.set('q', this.consulta);
    }

    try {
      const respuesta = await firstValueFrom(
        this.http.get<PaginaProveedores>('/api/catalogos/proveedores', { params })
      );
      this.itemsSignal.set([...respuesta.items]);
      this.paginaSignal.set(respuesta.pagina);
      this.tamanioPaginaSignal.set(respuesta.tamanioPagina);
      this.totalRegistrosSignal.set(respuesta.totalRegistros);
      this.totalPaginasSignal.set(respuesta.totalPaginas);
    } catch {
      this.itemsSignal.set([]);
      this.totalRegistrosSignal.set(0);
      this.totalPaginasSignal.set(0);
      this.errorSignal.set('No se pudo cargar el listado de proveedores.');
    } finally {
      this.cargandoSignal.set(false);
    }
  }
}
