import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { OrdenFecha } from '../../models/bandeja-item.model';

/**
 * Presentational (dumb) component: date-range / `proveedor` / sort-by-fecha controls (BACKLOG #13,
 * inbox-screen spec.md). Owns no state — receives current selection as inputs, emits changes as
 * outputs; the container (`InboxPage`) decides what to do with them. `desde`/`hasta`/`proveedor`
 * emit only on `change`/Enter, never per keystroke (design.md edge cases table).
 *
 * The estado control moved OUT of this bar to the estado-chip row next to the "Bandeja de facturas"
 * title (BACKLOG #21 follow-up, `estadoDerivado` filter) — this bar no longer carries it.
 */
@Component({
  selector: 'app-inbox-filter',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './inbox-filter.html',
  styleUrl: './inbox-filter.css',
})
export class InboxFilter {
  readonly orden = input<OrdenFecha>('desc');
  readonly desde = input<string | null>(null);
  readonly hasta = input<string | null>(null);
  readonly proveedor = input<string | null>(null);

  readonly ordenChange = output<OrdenFecha>();
  readonly desdeChange = output<string | null>();
  readonly hastaChange = output<string | null>();
  readonly proveedorChange = output<string | null>();

  onOrdenSelect(value: string): void {
    this.ordenChange.emit(value as OrdenFecha);
  }

  onDesdeChange(value: string): void {
    this.desdeChange.emit(value === '' ? null : value);
  }

  onHastaChange(value: string): void {
    this.hastaChange.emit(value === '' ? null : value);
  }

  onProveedorChange(value: string): void {
    this.proveedorChange.emit(value === '' ? null : value);
  }
}
