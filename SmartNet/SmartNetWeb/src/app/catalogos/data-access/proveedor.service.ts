import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { BusquedaProveedoresRespuesta, Proveedor } from './proveedor.model';

/**
 * Server state for the proveedor picker (BACKLOG #18 PR8, spa-picker-proveedor / ADR 0009 signals
 * pattern — no state library). Search input is debounced before a request is issued; the readonly
 * signals hold the current page of results and whether more pages exist. `firstValueFrom` +
 * `HttpClient.get`, matching `AsientoService` / `InboxService`.
 */
@Injectable({ providedIn: 'root' })
export class ProveedorService {
  private readonly http = inject(HttpClient);

  /** Minimum trimmed length before a request runs — mirrors the server-side guard. */
  private static readonly LONGITUD_MINIMA = 2;

  /** Debounce window in ms. Mutable so specs can shrink it without fake timers. */
  debounceMs = 250;

  private readonly resultadosSignal = signal<readonly Proveedor[]>([]);
  private readonly hayMasSignal = signal(false);
  private readonly buscandoSignal = signal(false);

  readonly resultados = this.resultadosSignal.asReadonly();
  readonly hayMas = this.hayMasSignal.asReadonly();
  readonly buscando = this.buscandoSignal.asReadonly();

  private consultaActual = '';
  private paginaActual = 1;
  private temporizador: ReturnType<typeof setTimeout> | null = null;

  /** Debounced: schedules a fresh (page 1) search for `consulta`. A blank or too-short term
   * clears the results immediately and issues no request (no unbounded scan). */
  buscar(consulta: string): void {
    if (this.temporizador !== null) {
      clearTimeout(this.temporizador);
      this.temporizador = null;
    }

    const termino = consulta.trim();
    if (termino.length < ProveedorService.LONGITUD_MINIMA) {
      this.consultaActual = '';
      this.resultadosSignal.set([]);
      this.hayMasSignal.set(false);
      return;
    }

    this.temporizador = setTimeout(() => {
      this.temporizador = null;
      void this.ejecutar(termino, 1);
    }, this.debounceMs);
  }

  /** Loads the next page for the current term and appends it to the signal. */
  async masResultados(): Promise<void> {
    if (!this.hayMasSignal() || this.consultaActual === '') {
      return;
    }
    await this.ejecutar(this.consultaActual, this.paginaActual + 1);
  }

  limpiar(): void {
    if (this.temporizador !== null) {
      clearTimeout(this.temporizador);
      this.temporizador = null;
    }
    this.consultaActual = '';
    this.paginaActual = 1;
    this.resultadosSignal.set([]);
    this.hayMasSignal.set(false);
  }

  private async ejecutar(consulta: string, pagina: number): Promise<void> {
    this.buscandoSignal.set(true);
    try {
      const respuesta = await firstValueFrom(
        this.http.get<BusquedaProveedoresRespuesta>('/api/catalogos/proveedores', {
          params: { q: consulta, pagina: String(pagina) },
        })
      );

      this.consultaActual = consulta;
      this.paginaActual = pagina;
      this.hayMasSignal.set(respuesta.hayMas);
      this.resultadosSignal.set(
        pagina === 1
          ? [...respuesta.resultados]
          : [...this.resultadosSignal(), ...respuesta.resultados]
      );
    } finally {
      this.buscandoSignal.set(false);
    }
  }
}
