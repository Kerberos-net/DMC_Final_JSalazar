import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { CuentaContable, ClavePlanContable } from '../../models/cuenta-contable.model';
import { EstadoOrden, flechaOrden } from '../orden';

interface ColumnaOrdenable {
  readonly clave: ClavePlanContable;
  readonly etiqueta: string;
}

/**
 * BACKLOG #22 PR4, design D8 -- presentational table for the plan contable screen. Columns are
 * "Código" (`cuenta`) and "Denominación" (`descripcion`, spec v2.1 label mapping). Headers are
 * sortable but the component owns no state: it emits `ordenar` with the column key and the
 * container feeds back the already-sorted `filas`. Read-only -- no row action of any kind.
 */
@Component({
  selector: 'app-plan-contable-tabla',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './plan-contable-tabla.html',
  styleUrl: './plan-contable-tabla.css',
})
export class PlanContableTabla {
  readonly filas = input.required<readonly CuentaContable[]>();
  readonly orden = input.required<EstadoOrden<ClavePlanContable> | null>();

  readonly ordenar = output<ClavePlanContable>();

  protected readonly columnas: readonly ColumnaOrdenable[] = [
    { clave: 'cuenta', etiqueta: 'Código' },
    { clave: 'descripcion', etiqueta: 'Denominación' },
  ];

  protected readonly flechas = computed<Record<ClavePlanContable, string>>(() => {
    const actual = this.orden();
    return {
      cuenta: actual ? flechaOrden(actual, 'cuenta') : '',
      descripcion: actual ? flechaOrden(actual, 'descripcion') : '',
    };
  });
}
