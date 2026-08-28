import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ErrorProcesamiento } from '../../models/bandeja-item.model';

/**
 * Presentational (dumb) component: projects `fact.ProcesamientoError` history (design D1/D3,
 * inbox-screen spec.md "Panel de errores renders ProcesamientoError history"). Embedded by
 * `inbox-list` inside a native `<details>` (design D8) -- this component itself renders nothing
 * when `errores()` is empty, so the caller's `<details>` never wraps a broken/empty panel.
 */
@Component({
  selector: 'app-panel-errores',
  standalone: true,
  imports: [DatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './panel-errores.html',
  styleUrl: './panel-errores.css',
})
export class PanelErrores {
  readonly errores = input.required<readonly ErrorProcesamiento[]>();
}
