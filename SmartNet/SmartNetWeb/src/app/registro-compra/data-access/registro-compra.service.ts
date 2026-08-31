import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  PaginaRegistroCompra,
  RegistroCompraCabecera,
} from '../models/registro-compra.model';

/**
 * BACKLOG #23 (spa spec req 2/6/7) — server state for the registro de compra screen. Cloned from
 * `CatalogoProveedorService`: NOTHING is done client-side. `periodo`, `pagina` and `tamanioPagina`
 * are request state and any change re-queries `GET /api/registro-compra`. A period change resets to
 * page 1. The readonly signals hold the current server page plus its envelope metadata. Query-only —
 * no mutation surface (spec req 7).
 */
@Injectable({ providedIn: 'root' })
export class RegistroCompraService {
  private readonly http = inject(HttpClient);

  private readonly itemsSignal = signal<readonly RegistroCompraCabecera[]>([]);
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

  private periodo = '';
  private tamanio = 20;
  private paginaSolicitada = 1;
  private temporizador: ReturnType<typeof setTimeout> | null = null;

  /** Initial load for `periodo` (page 1). */
  cargar(periodo: string): void {
    this.periodo = periodo;
    this.paginaSolicitada = 1;
    this.programar();
  }

  /** Period filter change -> re-query from page 1. */
  cambiarPeriodo(periodo: string): void {
    this.periodo = periodo;
    this.paginaSolicitada = 1;
    this.programar();
  }

  /** Page step from the paginador. A no-op when the page is already the one requested. */
  irAPagina(pagina: number): void {
    if (pagina === this.paginaSolicitada) {
      return;
    }
    this.paginaSolicitada = pagina;
    this.programar();
  }

  /** Rows-per-page change -> re-query from page 1. */
  cambiarTamanio(tamanio: number): void {
    this.tamanio = tamanio;
    this.paginaSolicitada = 1;
    this.programar();
  }

  /**
   * Schedules a fetch on a 0-delay timer, cancelling any pending one. This coalesces the
   * paginador's `tamanioChange` + `paginaChange(1)` burst into a single request.
   */
  private programar(): void {
    if (this.temporizador !== null) {
      clearTimeout(this.temporizador);
    }
    this.temporizador = setTimeout(() => {
      this.temporizador = null;
      void this.ejecutar();
    }, 0);
  }

  private async ejecutar(): Promise<void> {
    this.cargandoSignal.set(true);
    this.errorSignal.set(null);

    const params = new HttpParams()
      .set('periodo', this.periodo)
      .set('pagina', String(this.paginaSolicitada))
      .set('tamanioPagina', String(this.tamanio));

    try {
      const respuesta = await firstValueFrom(
        this.http.get<PaginaRegistroCompra>('/api/registro-compra', { params })
      );
      this.itemsSignal.set([...respuesta.items]);
      this.paginaSignal.set(respuesta.pagina);
      this.tamanioPaginaSignal.set(respuesta.tamanioPagina);
      this.totalRegistrosSignal.set(respuesta.totalRegistros);
      this.totalPaginasSignal.set(respuesta.totalPaginas);
    } catch (error) {
      this.itemsSignal.set([]);
      this.totalRegistrosSignal.set(0);
      this.totalPaginasSignal.set(0);
      // A 400 is a non-blocking validation problem with the period filter (spa spec req 2); any
      // other failure is a generic load error.
      this.errorSignal.set(
        error instanceof HttpErrorResponse && error.status === 400
          ? 'El periodo seleccionado no es valido.'
          : 'No se pudo cargar el registro de compra.'
      );
    } finally {
      this.cargandoSignal.set(false);
    }
  }
}
