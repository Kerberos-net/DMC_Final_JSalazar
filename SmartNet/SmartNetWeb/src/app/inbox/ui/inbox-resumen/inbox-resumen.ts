import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ResumenBandeja } from '../../models/bandeja-item.model';

type TonoTarjeta = 'pendiente' | 'validada' | 'error' | 'alerta';

interface TarjetaResumen {
  readonly etiqueta: string;
  readonly valor: number;
  readonly nota: string;
  readonly tono: TonoTarjeta;
}

/**
 * BACKLOG #21, design D8 / handoff §2 — presentational summary strip for the dashboard. Four
 * display-only cards fed from the global `resumen` aggregate (`spa-visual-bandeja` ADDED
 * requirement). No `output`: the cards do NOT act as filter shortcuts. `descartadas`/`total` are
 * on the input but deliberately not rendered. Each card carries the handoff's semantic tone (the
 * number ink + the caption dot) resolved from the estado tokens.
 */
@Component({
  selector: 'app-inbox-resumen',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './inbox-resumen.html',
  styleUrl: './inbox-resumen.css',
})
export class InboxResumen {
  readonly resumen = input.required<ResumenBandeja>();

  readonly tarjetas = computed<TarjetaResumen[]>(() => {
    const r = this.resumen();
    return [
      { etiqueta: 'Pendientes', valor: r.pendientes, nota: 'Requieren revisión', tono: 'pendiente' },
      { etiqueta: 'Validadas', valor: r.validadas, nota: 'Registradas en el sistema', tono: 'validada' },
      { etiqueta: 'Con error', valor: r.conError, nota: 'Acción requerida', tono: 'error' },
      { etiqueta: 'Alertas', valor: r.alertas, nota: 'Duplicados o datos faltantes', tono: 'alerta' },
    ];
  });
}
