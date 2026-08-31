import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RegistroCompraService } from '../../data-access/registro-compra.service';
import { RegistroCompraDetalleService } from '../../data-access/registro-compra-detalle.service';
import { DescargaXlsx } from '../../../catalogos/data-access/descarga-xlsx';
import { RegistroCompraTabla } from '../../ui/registro-compra-tabla/registro-compra-tabla';
import { TablaPaginador } from '../../../catalogos/ui/tabla-paginador/tabla-paginador';
import { BotonExportar } from '../../../catalogos/ui/boton-exportar/boton-exportar';
import { mesActual } from '../../../shared/formato';
import { LineaRegistro } from '../../models/registro-compra.model';

/**
 * BACKLOG #23 (spa spec req 2/3/5/6) — container for the registro de compra screen. Owns the
 * `periodo` filter (default: current LOCAL accounting month), the single expanded-row id and the
 * per-asiento line cache handoff. All narrowing is server-side: a period change or a page/size step
 * re-queries `GET /api/registro-compra` and drops the line cache (a stale asiento may leave view).
 * "Exportar a Excel" delegates to the shared `descarga-xlsx` helper. Strictly read-only.
 */
@Component({
  selector: 'app-registro-compra-page',
  standalone: true,
  imports: [RegistroCompraTabla, TablaPaginador, BotonExportar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './registro-compra-page.html',
  styleUrl: './registro-compra-page.css',
})
export class RegistroCompraPage {
  private readonly servicio = inject(RegistroCompraService);
  private readonly detalle = inject(RegistroCompraDetalleService);
  private readonly descarga = inject(DescargaXlsx);

  protected readonly items = this.servicio.items;
  protected readonly pagina = this.servicio.pagina;
  protected readonly totalPaginas = this.servicio.totalPaginas;
  protected readonly totalRegistros = this.servicio.totalRegistros;
  protected readonly tamanioPagina = this.servicio.tamanioPagina;
  protected readonly cargando = this.servicio.cargando;
  protected readonly error = this.servicio.error;
  protected readonly descargando = this.descarga.descargando;

  protected readonly periodo = signal(mesActual());
  protected readonly expandido = signal<number | null>(null);
  protected readonly lineasPorAsiento = signal<ReadonlyMap<number, readonly LineaRegistro[]>>(
    new Map()
  );

  constructor() {
    this.servicio.cargar(this.periodo());
  }

  onPeriodo(valor: string): void {
    this.periodo.set(valor);
    this.reiniciarDetalle();
    this.servicio.cambiarPeriodo(valor);
  }

  onPagina(pagina: number): void {
    this.reiniciarDetalle();
    this.servicio.irAPagina(pagina);
  }

  onTamanio(tamanio: number): void {
    this.reiniciarDetalle();
    this.servicio.cambiarTamanio(tamanio);
  }

  async onAlternar(asientoId: number): Promise<void> {
    if (this.expandido() === asientoId) {
      this.expandido.set(null);
      return;
    }
    this.expandido.set(asientoId);
    try {
      const lineas = await this.detalle.obtener(asientoId);
      const siguiente = new Map(this.lineasPorAsiento());
      siguiente.set(asientoId, lineas);
      this.lineasPorAsiento.set(siguiente);
    } catch {
      // A failed detail fetch leaves the row expanded with the "loading" placeholder; the listing
      // itself is unaffected. No blocking error surface for a per-row expand.
    }
  }

  exportar(): void {
    void this.descarga
      .descargar('/api/registro-compra/export', { periodo: this.periodo() })
      .catch(() => undefined);
  }

  private reiniciarDetalle(): void {
    this.expandido.set(null);
    this.lineasPorAsiento.set(new Map());
    this.detalle.limpiar();
  }
}
