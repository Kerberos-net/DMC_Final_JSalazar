import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { EstadoConsumo, OrdenFecha } from '../../models/bandeja-item.model';

/**
 * Presentational (dumb) component: filter-by-`EstadoConsumo` and sort-by-fecha controls.
 * Owns no state — receives current selection as inputs, emits changes as outputs; the
 * container (`InboxPage`) decides what to do with them (spec.md "Filter by outcome" /
 * "Sort by fecha").
 */
@Component({
  selector: 'app-inbox-filter',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './inbox-filter.html',
})
export class InboxFilter {
  readonly estado = input<EstadoConsumo | null>(null);
  readonly orden = input<OrdenFecha>('desc');

  readonly estadoChange = output<EstadoConsumo | null>();
  readonly ordenChange = output<OrdenFecha>();

  onEstadoSelect(value: string): void {
    this.estadoChange.emit(value === '' ? null : (value as EstadoConsumo));
  }

  onOrdenSelect(value: string): void {
    this.ordenChange.emit(value as OrdenFecha);
  }
}
