import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { ClaveTipoCambio, TipoCambioHistorico } from '../../models/tipo-cambio.model';
import { dosDecimales } from '../../../shared/formato';
import { EstadoOrden, flechaOrden } from '../orden';

interface ColumnaOrdenable {
  readonly clave: ClaveTipoCambio;
  readonly etiqueta: string;
  readonly numerica: boolean;
}

/**
 * BACKLOG #22 PR8, design D8 -- presentational table for the tipo de cambio screen. Columns are
 * Fecha (`fecha`), Origen (`origen`), Compra (`compra`) and Venta (`venta`); both origins appear per
 * date and there is no origin selector (spa spec req 4). Headers are sortable but the component owns
 * no state: it emits `ordenar` with the column key and the container feeds back the already-sorted
 * `filas`. Amounts render with exactly 2 decimals (CONVENTIONS.md). Read-only -- no row action.
 */
@Component({
  selector: 'app-tipo-cambio-tabla',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './tipo-cambio-tabla.html',
  styleUrl: './tipo-cambio-tabla.css',
})
export class TipoCambioTabla {
  readonly filas = input.required<readonly TipoCambioHistorico[]>();
  readonly orden = input.required<EstadoOrden<ClaveTipoCambio> | null>();

  readonly ordenar = output<ClaveTipoCambio>();

  protected readonly dosDecimales = dosDecimales;

  protected readonly columnas: readonly ColumnaOrdenable[] = [
    { clave: 'fecha', etiqueta: 'Fecha', numerica: false },
    { clave: 'origen', etiqueta: 'Origen', numerica: false },
    { clave: 'compra', etiqueta: 'Compra', numerica: true },
    { clave: 'venta', etiqueta: 'Venta', numerica: true },
  ];

  protected readonly flechas = computed<Record<ClaveTipoCambio, string>>(() => {
    const actual = this.orden();
    return {
      fecha: actual ? flechaOrden(actual, 'fecha') : '',
      origen: actual ? flechaOrden(actual, 'origen') : '',
      compra: actual ? flechaOrden(actual, 'compra') : '',
      venta: actual ? flechaOrden(actual, 'venta') : '',
    };
  });
}
