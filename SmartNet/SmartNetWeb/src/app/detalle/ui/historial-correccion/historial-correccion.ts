import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { EntradaAuditoriaRespuesta } from '../../models/historial.model';

/**
 * Presentational (dumb) component: correction-history panel next to the asiento (design D4 --
 * native `<details>`/`<summary>`, closed by default, zero Angular state; caret drawn in CSS).
 * spa-visual-detalle-validacion spec: distinct empty-state treatment, no alert token.
 */
@Component({
  selector: 'app-historial-correccion',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './historial-correccion.html',
  styleUrl: './historial-correccion.css',
})
export class HistorialCorreccion {
  readonly historial = input.required<readonly EntradaAuditoriaRespuesta[]>();
}
