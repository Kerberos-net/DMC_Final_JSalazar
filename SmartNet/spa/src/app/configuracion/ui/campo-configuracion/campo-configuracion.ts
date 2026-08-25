import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';
import { ConfiguracionEntrada } from '../../models/configuracion.model';

/**
 * Presentational (dumb) component: one editable `fact.Configuracion` row (spec.md "list and edits
 * sections/keys"). Client-side validation is NOT authoritative (design D6 "mirrors Tipo but is
 * NOT authoritative -- the server error is rendered by the existing http-error.interceptor" — this
 * component just forwards the container-supplied server error string, it never blocks a save
 * itself).
 */
@Component({
  selector: 'app-campo-configuracion',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './campo-configuracion.html',
})
export class CampoConfiguracion {
  readonly entrada = input.required<ConfiguracionEntrada>();
  readonly error = input<string | null>(null);
  readonly guardando = input(false);

  readonly guardar = output<{ seccion: string; clave: string; valor: string | null }>();

  readonly borrador = signal<string | null>(null);

  onValorInput(valor: string): void {
    this.borrador.set(valor);
  }

  onGuardar(): void {
    const entrada = this.entrada();
    const valor = this.borrador() ?? entrada.valor;
    this.guardar.emit({ seccion: entrada.seccion, clave: entrada.clave, valor: valor === '' ? null : valor });
  }
}
