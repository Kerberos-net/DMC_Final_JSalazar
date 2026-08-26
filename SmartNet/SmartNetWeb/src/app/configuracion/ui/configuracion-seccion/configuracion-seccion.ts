import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { CampoConfiguracion } from '../campo-configuracion/campo-configuracion';
import { ConfiguracionEntrada } from '../../models/configuracion.model';

/**
 * Presentational (dumb) component: renders one `Seccion`'s entries as a list of
 * {@link CampoConfiguracion}, and re-emits its `guardar` event upward (spec.md "list and edits
 * sections/keys" — grouping by section is the screen's only structure requirement).
 */
@Component({
  selector: 'app-configuracion-seccion',
  standalone: true,
  imports: [CampoConfiguracion],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './configuracion-seccion.html',
})
export class ConfiguracionSeccion {
  readonly seccion = input.required<string>();
  readonly entradas = input.required<ConfiguracionEntrada[]>();
  readonly errores = input<Record<string, string>>({});
  readonly guardandoClave = input<string | null>(null);

  readonly guardar = output<{ seccion: string; clave: string; valor: string | null }>();

  errorPara(clave: string): string | null {
    return this.errores()[clave] || null;
  }

  onGuardar(evento: { seccion: string; clave: string; valor: string | null }): void {
    this.guardar.emit(evento);
  }
}
