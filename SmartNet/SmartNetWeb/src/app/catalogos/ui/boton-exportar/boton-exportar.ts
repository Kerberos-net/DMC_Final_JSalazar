import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/**
 * BACKLOG #22, design D8 -- presentational "Exportar a Excel" button. It emits the intent only;
 * the blob GET + browser download live in `data-access/descarga-xlsx.ts`. The `descargando` input
 * disables the button in flight. Green sheet glyph is a CSS div (no svg / img -- sidebar glyph
 * precedent).
 */
@Component({
  selector: 'app-boton-exportar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './boton-exportar.html',
  styleUrl: './boton-exportar.css',
})
export class BotonExportar {
  readonly descargando = input<boolean>(false);
  readonly exportar = output<void>();
}
