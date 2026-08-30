import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ResumenBandeja } from '../../models/bandeja-item.model';

interface TarjetaResumen {
  readonly etiqueta: string;
  readonly valor: number;
}

/**
 * BACKLOG #21, design D8 — presentational summary strip for the dashboard. Four display-only
 * cards fed from the global `resumen` aggregate (`spa-visual-bandeja` ADDED requirement). No
 * `output`: the cards do NOT act as filter shortcuts. `descartadas`/`total` are on the input but
 * deliberately not rendered.
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
      { etiqueta: 'Pendientes', valor: r.pendientes },
      { etiqueta: 'Validadas', valor: r.validadas },
      { etiqueta: 'Con error', valor: r.conError },
      { etiqueta: 'Alertas', valor: r.alertas },
    ];
  });
}
