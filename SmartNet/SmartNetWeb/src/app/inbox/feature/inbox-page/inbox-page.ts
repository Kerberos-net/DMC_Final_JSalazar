import { ChangeDetectionStrategy, Component, effect, inject, signal, viewChild } from '@angular/core';
import { InboxService } from '../../data-access/inbox.service';
import { InboxFilter } from '../../ui/inbox-filter/inbox-filter';
import { InboxList } from '../../ui/inbox-list/inbox-list';
import { InboxResumen } from '../../ui/inbox-resumen/inbox-resumen';
import { ConfirmarReproceso } from '../../ui/confirmar-reproceso/confirmar-reproceso';
import { EstadoConsumo, OrdenFecha } from '../../models/bandeja-item.model';

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

  readonly estado = signal<EstadoConsumo | null>(null);
  readonly orden = signal<OrdenFecha>('desc');
  readonly desde = signal<string | null>(null);
  readonly hasta = signal<string | null>(null);
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
      const estado = this.estado();
      const orden = this.orden();
      const desde = this.desde();
      const hasta = this.hasta();
      const proveedor = this.proveedor();
      const pagina = this.pagina();
      // `InboxService.cargar` re-throws on failure for its own spec; the container only needs the
      // error signal it sets, so the rejection is swallowed here (no unhandled promise rejection).
      void this.inboxService
        .cargar({
          estado,
          orden,
          desde,
          hasta,
          proveedor,
          pagina: pagina ?? undefined,
        })
        .catch(() => undefined);
    });
  }

  onEstadoChange(estado: EstadoConsumo | null): void {
    this.pagina.set(null);
    this.estado.set(estado);
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
