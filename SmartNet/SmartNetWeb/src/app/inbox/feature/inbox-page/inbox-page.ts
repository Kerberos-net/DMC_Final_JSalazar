import { ChangeDetectionStrategy, Component, effect, inject, signal, viewChild } from '@angular/core';
import { InboxService } from '../../data-access/inbox.service';
import { InboxFilter } from '../../ui/inbox-filter/inbox-filter';
import { InboxList } from '../../ui/inbox-list/inbox-list';
import { InboxResumen } from '../../ui/inbox-resumen/inbox-resumen';
import { ConfirmarReproceso } from '../../ui/confirmar-reproceso/confirmar-reproceso';
import {
  EstadoDerivado,
  OrdenFecha,
  ResumenBandeja,
} from '../../models/bandeja-item.model';
import { fechaIso } from '../../../shared/formato';

interface ChipEstadoFiltro {
  readonly valor: EstadoDerivado;
  readonly etiqueta: string;
  readonly conteo: number;
  /** Drives the per-estado colour class (`inbox-page__chip--{tono}`). */
  readonly tono: 'todos' | 'pendiente' | 'validada' | 'error' | 'alerta' | 'descartada';
}

/**
 * Container (smart) component: owns the filter/orden/pagina signals (ADR 0009 -- "los filtros de
 * la bandeja son signals; la consulta se deriva de ellos") and the real side effects this screen
 * has -- fetching `GET /api/bandeja` through {@link InboxService} whenever a filter signal
 * changes, and driving the `reprocesar` flow (confirm dialog -> `InboxService.reprocesar` ->
 * refetch with the same filters, design.md Data Flow). Delegates all rendering to the
 * presentational `InboxFilter`/`InboxList`/`ConfirmarReproceso`.
 */
@Component({
  selector: 'app-inbox-page',
  standalone: true,
  imports: [InboxFilter, InboxList, InboxResumen, ConfirmarReproceso],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './inbox-page.html',
  styleUrl: './inbox-page.css',
})
export class InboxPage {
  private readonly inboxService = inject(InboxService);
  private readonly dialogo = viewChild.required(ConfirmarReproceso);

  /** Handoff §2 header: "30 de agosto de 2026 · ¿Qué necesito atender hoy?" (today's date, es-PE). */
  protected readonly encabezadoFecha =
    new Intl.DateTimeFormat('es-PE', { day: 'numeric', month: 'long', year: 'numeric' }).format(
      new Date()
    ) + ' · ¿Qué necesito atender hoy?';

  readonly estadoDerivado = signal<EstadoDerivado>('TODOS');
  readonly orden = signal<OrdenFecha>('desc');
  /** Handoff §2: the date range starts on the first day of the current month … */
  readonly desde = signal<string | null>(
    fechaIso(new Date(new Date().getFullYear(), new Date().getMonth(), 1))
  );
  /** … and ends today. */
  readonly hasta = signal<string | null>(fechaIso(new Date()));
  readonly proveedor = signal<string | null>(null);
  readonly pagina = signal<number | null>(null);

  /** Optimistic guard (design.md "Double click on reprocesar" edge case), container-owned. */
  readonly reprocesandoId = signal<number | null>(null);
  private procesamientoIdPendienteDeConfirmar: number | null = null;

  readonly items = this.inboxService.items;
  readonly loading = this.inboxService.loading;
  readonly error = this.inboxService.error;
  readonly resumen = this.inboxService.resumen;
  readonly totalPaginas = this.inboxService.totalPaginas;

  constructor() {
    effect(() => {
      const estadoDerivado = this.estadoDerivado();
      const orden = this.orden();
      const desde = this.desde();
      const hasta = this.hasta();
      const proveedor = this.proveedor();
      const pagina = this.pagina();
      // `InboxService.cargar` re-throws on failure for its own spec; the container only needs the
      // error signal it sets, so the rejection is swallowed here (no unhandled promise rejection).
      void this.inboxService
        .cargar({
          estadoDerivado,
          orden,
          desde,
          hasta,
          proveedor,
          pagina: pagina ?? undefined,
        })
        .catch(() => undefined);
    });
  }

  chipsEstado(r: ResumenBandeja): ChipEstadoFiltro[] {
    return [
      { valor: 'TODOS', etiqueta: 'Todos', conteo: r.total, tono: 'todos' },
      { valor: 'PENDIENTE', etiqueta: 'Pendiente', conteo: r.pendientes, tono: 'pendiente' },
      { valor: 'VALIDADA', etiqueta: 'Validada', conteo: r.validadas, tono: 'validada' },
      { valor: 'ERROR', etiqueta: 'Error', conteo: r.conError, tono: 'error' },
      { valor: 'ALERTA', etiqueta: 'Alerta', conteo: r.alertas, tono: 'alerta' },
      { valor: 'DESCARTADA', etiqueta: 'Descartada', conteo: r.descartadas, tono: 'descartada' },
    ];
  }

  onEstadoDerivadoChange(estadoDerivado: EstadoDerivado): void {
    this.pagina.set(null);
    this.estadoDerivado.set(estadoDerivado);
  }

  onOrdenChange(orden: OrdenFecha): void {
    this.orden.set(orden);
  }

  onDesdeChange(desde: string | null): void {
    this.pagina.set(null);
    this.desde.set(desde);
  }

  onHastaChange(hasta: string | null): void {
    this.pagina.set(null);
    this.hasta.set(hasta);
  }

  onProveedorChange(proveedor: string | null): void {
    this.pagina.set(null);
    this.proveedor.set(proveedor);
  }

  onPaginaChange(pagina: number): void {
    this.pagina.set(pagina);
  }

  onReprocesarSolicitado(procesamientoId: number): void {
    this.procesamientoIdPendienteDeConfirmar = procesamientoId;
    this.dialogo().open();
  }

  onConfirmarReproceso(): void {
    const procesamientoId = this.procesamientoIdPendienteDeConfirmar;
    if (procesamientoId === null) {
      return;
    }
    this.reprocesandoId.set(procesamientoId);
    void this.inboxService.reprocesar(procesamientoId).then(() =>
      this.inboxService.cargar(this.inboxService.ultimosFiltros())
    );
  }

  onCancelarReproceso(): void {
    this.procesamientoIdPendienteDeConfirmar = null;
  }
}
