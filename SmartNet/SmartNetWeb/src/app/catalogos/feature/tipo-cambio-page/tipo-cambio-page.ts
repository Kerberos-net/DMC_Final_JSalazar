import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { TipoCambioService } from '../../data-access/tipo-cambio.service';
import { DescargaXlsx } from '../../data-access/descarga-xlsx';
import { TipoCambioTabla } from '../../ui/tipo-cambio-tabla/tipo-cambio-tabla';
import { BotonExportar } from '../../ui/boton-exportar/boton-exportar';
import { alternarOrden, ordenarPor, type EstadoOrden } from '../../ui/orden';
import { ClaveTipoCambio } from '../../models/tipo-cambio.model';
import { rangoMesActual } from '../../../shared/formato';

/**
 * Container (smart) component for the tipo de cambio screen (spa spec req 1,4,5). Owns the date
 * range and the client-side sort signal; the data-access service re-fetches
 * `GET /api/tipos-cambio?desde=&hasta=` on every range change, and each column sort is a
 * `computed()` over the in-memory rows -- no re-query (design D8). The default range is the first
 * day of the current month .. today, read from the browser clock in LOCAL time (the ONE place a
 * clock is read -- never in any Core, ADR 0019). "Exportar a Excel" delegates to the shared
 * `descarga-xlsx` helper with the current range. Strictly read-only.
 */
@Component({
  selector: 'app-tipo-cambio-page',
  standalone: true,
  imports: [TipoCambioTabla, BotonExportar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './tipo-cambio-page.html',
  styleUrl: './tipo-cambio-page.css',
})
export class TipoCambioPage {
  private readonly servicio = inject(TipoCambioService);
  private readonly descarga = inject(DescargaXlsx);

  protected readonly cargando = this.servicio.cargando;
  protected readonly error = this.servicio.error;
  protected readonly descargando = this.descarga.descargando;

  private readonly rangoInicial = rangoMesActual();
  protected readonly desde = signal(this.rangoInicial.desde);
  protected readonly hasta = signal(this.rangoInicial.hasta);
  protected readonly orden = signal<EstadoOrden<ClaveTipoCambio> | null>(null);

  protected readonly filas = computed(() => {
    const actual = this.orden();
    const items = this.servicio.items();
    if (actual === null) {
      return items;
    }
    return ordenarPor(items, (t) => t[actual.campo], actual.direccion);
  });

  constructor() {
    void this.recargar();
  }

  private recargar(): Promise<void> {
    return this.servicio.cargar(this.desde(), this.hasta()).catch(() => undefined);
  }

  onDesde(valor: string): void {
    this.desde.set(valor);
    void this.recargar();
  }

  onHasta(valor: string): void {
    this.hasta.set(valor);
    void this.recargar();
  }

  onOrdenar(campo: ClaveTipoCambio): void {
    const actual = this.orden();
    this.orden.set(actual ? alternarOrden(actual, campo) : { campo, direccion: 'asc' });
  }

  exportar(): void {
    void this.descarga
      .descargar('/api/tipos-cambio/exportacion', { desde: this.desde(), hasta: this.hasta() })
      .catch(() => undefined);
  }
}
