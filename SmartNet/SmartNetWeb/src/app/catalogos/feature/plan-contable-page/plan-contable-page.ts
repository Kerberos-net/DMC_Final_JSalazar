import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { PlanContableService } from '../../data-access/plan-contable.service';
import { DescargaXlsx } from '../../data-access/descarga-xlsx';
import { PlanContableTabla } from '../../ui/plan-contable-tabla/plan-contable-tabla';
import { TablaPaginador } from '../../ui/tabla-paginador/tabla-paginador';
import { BotonExportar } from '../../ui/boton-exportar/boton-exportar';
import { alternarOrden, ordenarPor, type EstadoOrden } from '../../ui/orden';
import { ClavePlanContable } from '../../models/cuenta-contable.model';

const TAMANIOS = [6, 10, 20, 50] as const;

/**
 * Container (smart) component for the plan contable screen (spa spec req 1,3,5). Owns the
 * filter / sort / pagination signals; the data-access service fetches the full plan once on init
 * and every subsequent narrowing is a `computed()` over the in-memory list -- no re-query
 * (design D7/D8). "Exportar a Excel" delegates to the shared `descarga-xlsx` helper with the
 * current filter term (`/api/catalogos/plan-contable/exportacion?q=`). Strictly read-only.
 */
@Component({
  selector: 'app-plan-contable-page',
  standalone: true,
  imports: [PlanContableTabla, TablaPaginador, BotonExportar],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './plan-contable-page.html',
  styleUrl: './plan-contable-page.css',
})
export class PlanContablePage {
  private readonly servicio = inject(PlanContableService);
  private readonly descarga = inject(DescargaXlsx);

  protected readonly cargando = this.servicio.cargando;
  protected readonly error = this.servicio.error;
  protected readonly descargando = this.descarga.descargando;

  protected readonly filtro = signal('');
  protected readonly orden = signal<EstadoOrden<ClavePlanContable> | null>(null);
  protected readonly pagina = signal(1);
  protected readonly tamanio = signal<number>(20);
  protected readonly tamaniosDisponibles = TAMANIOS;

  private readonly filtradas = computed(() => {
    const termino = this.filtro().trim().toLowerCase();
    const plan = this.servicio.plan();
    if (termino.length === 0) {
      return plan;
    }
    return plan.filter(
      (c) =>
        c.cuenta.toLowerCase().includes(termino) ||
        c.descripcion.toLowerCase().includes(termino)
    );
  });

  private readonly ordenadas = computed(() => {
    const actual = this.orden();
    if (actual === null) {
      return this.filtradas();
    }
    return ordenarPor(this.filtradas(), (c) => c[actual.campo], actual.direccion);
  });

  protected readonly totalPaginas = computed(() =>
    Math.max(1, Math.ceil(this.ordenadas().length / this.tamanio()))
  );

  protected readonly paginaActual = computed(() => Math.min(this.pagina(), this.totalPaginas()));

  protected readonly visibles = computed(() => {
    const inicio = (this.paginaActual() - 1) * this.tamanio();
    return this.ordenadas().slice(inicio, inicio + this.tamanio());
  });

  constructor() {
    void this.servicio.cargar().catch(() => undefined);
  }

  onFiltro(valor: string): void {
    this.pagina.set(1);
    this.filtro.set(valor);
  }

  onOrdenar(campo: ClavePlanContable): void {
    const actual = this.orden();
    this.orden.set(actual ? alternarOrden(actual, campo) : { campo, direccion: 'asc' });
    this.pagina.set(1);
  }

  onPagina(pagina: number): void {
    this.pagina.set(pagina);
  }

  onTamanio(tamanio: number): void {
    this.tamanio.set(tamanio);
    this.pagina.set(1);
  }

  exportar(): void {
    void this.descarga
      .descargar('/api/catalogos/plan-contable/exportacion', { q: this.filtro().trim() })
      .catch(() => undefined);
  }
}
