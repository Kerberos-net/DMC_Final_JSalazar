import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { TemaEfectivo } from '../../tema.service';

interface DestinoNav {
  readonly testid: string;
  readonly etiqueta: string;
  readonly glifo: string;
  readonly ruta?: string;
}

/**
 * design D5 — presentational sidebar for the authenticated shell (`spa-shell-nav`). Owns no state:
 * the collapsed flag, the current session user and the effective theme come in as inputs; the
 * collapse toggle emits `alternar` and the theme button emits `alternarTema`. `ShellLayout` (the
 * container) is the only injector of `SidebarService`, `SessionService` and `TemaService`.
 *
 * Replica del handoff (`Gestor de Facturas.dc.html`): cabecera con hamburguesa + marca, grupo
 * primario y grupo utilitario separados por un divisor, y al pie una tarjeta "Apariencia" con un
 * botón sol/luna que alterna claro↔oscuro más la fila de perfil. Los destinos sin ruta se
 * renderizan inertes ("Disponible próximamente"). Glifos hechos a mano con `<div>`/`<span>`.
 */
@Component({
  selector: 'app-sidebar',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './sidebar.html',
  styleUrl: './sidebar.css',
})
export class Sidebar {
  readonly colapsado = input.required<boolean>();
  readonly usuario = input<string | null>(null);
  readonly temaEfectivo = input.required<TemaEfectivo>();

  readonly alternar = output<void>();
  readonly alternarTema = output<void>();

  readonly nombreUsuario = computed(() => this.usuario() ?? 'Asistente contable');
  readonly esOscuro = computed(() => this.temaEfectivo() === 'oscuro');
  readonly etiquetaTema = computed(() =>
    this.esOscuro() ? 'Cambiar a tema claro' : 'Cambiar a tema oscuro'
  );

  protected readonly primarios: readonly DestinoNav[] = [
    { testid: 'nav-bandeja', etiqueta: 'Bandeja principal', glifo: 'bandeja', ruta: '/bandeja' },
    { testid: 'nav-registro', etiqueta: 'Registro de compra', glifo: 'registro' },
    { testid: 'nav-proveedores', etiqueta: 'Proveedores', glifo: 'proveedores' },
    { testid: 'nav-plan-contable', etiqueta: 'Plan contable', glifo: 'plan' },
  ];

  protected readonly utilitarios: readonly DestinoNav[] = [
    { testid: 'nav-errores', etiqueta: 'Errores y notificaciones', glifo: 'errores' },
    { testid: 'nav-sincronizacion', etiqueta: 'Sincronización', glifo: 'sincronizacion' },
    {
      testid: 'nav-configuracion',
      etiqueta: 'Configuración',
      glifo: 'configuracion',
      ruta: '/configuracion',
    },
  ];
}
