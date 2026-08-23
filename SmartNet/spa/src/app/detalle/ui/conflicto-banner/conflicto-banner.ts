import { KeyValuePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { ProblemaDetails } from '../../../shared/problema.model';
import { CategoriaProblema } from '../../data-access/problema-ux';

/**
 * Presentational (dumb) component: renders the outcome of a "Guardar avance"/"Validar" write per
 * design D6's three UX buckets. Owns no state — the container decides WHEN a `problema` exists and
 * what `recargar` should do (spec.md: refetch factura/asiento/If-Match, discard local edits).
 */
@Component({
  selector: 'app-conflicto-banner',
  standalone: true,
  imports: [KeyValuePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './conflicto-banner.html',
})
export class ConflictoBanner {
  readonly problema = input<ProblemaDetails | null>(null);
  readonly categoria = input<CategoriaProblema | null>(null);

  readonly recargar = output<void>();

  get esConflictoDeConcurrencia(): boolean {
    return this.categoria() === 'conflicto-concurrencia';
  }
}
