import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { InboxService } from '../../data-access/inbox.service';
import { InboxFilter } from '../../ui/inbox-filter/inbox-filter';
import { InboxList } from '../../ui/inbox-list/inbox-list';
import { EstadoConsumo, OrdenFecha } from '../../models/bandeja-item.model';

/**
 * Container (smart) component: owns the filter/orden signals (ADR 0009 -- "los filtros de la
 * bandeja son signals; la consulta se deriva de ellos") and the one real side effect this
 * screen has -- fetching `GET /api/bandeja` through {@link InboxService} whenever a filter
 * signal changes. Delegates all rendering to the presentational `InboxFilter`/`InboxList`.
 */
@Component({
  selector: 'app-inbox-page',
  standalone: true,
  imports: [InboxFilter, InboxList],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './inbox-page.html',
})
export class InboxPage {
  private readonly inboxService = inject(InboxService);

  readonly estado = signal<EstadoConsumo | null>(null);
  readonly orden = signal<OrdenFecha>('desc');

  readonly items = this.inboxService.items;
  readonly loading = this.inboxService.loading;
  readonly error = this.inboxService.error;

  constructor() {
    effect(() => {
      const estado = this.estado();
      const orden = this.orden();
      void this.inboxService.cargar(estado, orden);
    });
  }

  onEstadoChange(estado: EstadoConsumo | null): void {
    this.estado.set(estado);
  }

  onOrdenChange(orden: OrdenFecha): void {
    this.orden.set(orden);
  }
}
