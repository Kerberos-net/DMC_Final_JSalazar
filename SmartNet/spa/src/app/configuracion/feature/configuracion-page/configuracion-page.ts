import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ConfiguracionService } from '../../data-access/configuracion.service';
import { ConfiguracionSeccion } from '../../ui/configuracion-seccion/configuracion-seccion';
import { ConfiguracionEntrada } from '../../models/configuracion.model';
import { ProblemaDetails } from '../../../shared/problema.model';

/**
 * Container (smart) component: owns the "one section-grouped screen" the spec.md
 * configuracion-api-spa scenarios describe — loads every section on init, and calls
 * `ConfiguracionService.actualizar` per edited key (design D6). A server 422/404 is kept as a
 * PER-CLAVE error (spec.md "the screen displays the validation error and does not show the
 * invalid value as saved") rather than one global banner, so one rejected field never hides
 * another field's already-saved state.
 */
@Component({
  selector: 'app-configuracion-page',
  standalone: true,
  imports: [ConfiguracionSeccion],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './configuracion-page.html',
})
export class ConfiguracionPage {
  private readonly configuracionService = inject(ConfiguracionService);

  readonly entradas = this.configuracionService.entradas;
  readonly loading = this.configuracionService.loading;

  readonly guardandoClave = signal<string | null>(null);
  readonly erroresPorClave = signal<Record<string, string>>({});

  readonly secciones = computed<{ seccion: string; entradas: ConfiguracionEntrada[] }[]>(() => {
    const porSeccion = new Map<string, ConfiguracionEntrada[]>();
    for (const entrada of this.entradas()) {
      const lista = porSeccion.get(entrada.seccion) ?? [];
      lista.push(entrada);
      porSeccion.set(entrada.seccion, lista);
    }
    return [...porSeccion.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([seccion, entradas]) => ({ seccion, entradas }));
  });

  constructor() {
    void this.configuracionService.cargar();
  }

  async onGuardar(evento: { seccion: string; clave: string; valor: string | null }): Promise<void> {
    this.guardandoClave.set(evento.clave);
    this.erroresPorClave.set({ ...this.erroresPorClave(), [evento.clave]: '' });

    try {
      await this.configuracionService.actualizar(evento.seccion, evento.clave, evento.valor);
      const { [evento.clave]: _omitido, ...resto } = this.erroresPorClave();
      this.erroresPorClave.set(resto);
    } catch (err) {
      this.erroresPorClave.set({ ...this.erroresPorClave(), [evento.clave]: this.mensajeDeError(err) });
    } finally {
      this.guardandoClave.set(null);
    }
  }

  private mensajeDeError(err: unknown): string {
    if (err instanceof HttpErrorResponse && err.error) {
      const problema = err.error as ProblemaDetails;
      return problema.detail ?? problema.title ?? 'No se pudo guardar el valor.';
    }
    return 'No se pudo guardar el valor.';
  }
}
