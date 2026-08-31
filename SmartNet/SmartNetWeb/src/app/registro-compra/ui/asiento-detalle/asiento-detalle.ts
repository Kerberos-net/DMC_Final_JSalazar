import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { LineaRegistro } from '../../models/registro-compra.model';
import { dosDecimales } from '../../../shared/formato';

/**
 * BACKLOG #23 (spa spec req 3) — presentational, read-only view of one asiento's detail lines,
 * ordered by `orden`. No mutation control of any kind (spec req 7). `null` = not loaded yet
 * (the container shows its own spinner); `[]` = loaded and genuinely empty → explicit message.
 */
@Component({
  selector: 'app-asiento-detalle',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './asiento-detalle.html',
  styleUrl: './asiento-detalle.css',
})
export class AsientoDetalle {
  readonly lineas = input.required<readonly LineaRegistro[] | null>();

  protected readonly ordenadas = computed(() => {
    const actuales = this.lineas();
    return actuales ? [...actuales].sort((a, b) => a.orden - b.orden) : null;
  });

  protected readonly fmt = (valor: number): string => (valor === 0 ? '' : dosDecimales(valor));
}
