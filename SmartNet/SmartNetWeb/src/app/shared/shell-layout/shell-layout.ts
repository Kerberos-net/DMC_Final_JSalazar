import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { TemaService } from '../tema.service';
import { SidebarService } from '../sidebar.service';
import { SessionService } from '../session.service';
import { Sidebar } from './sidebar/sidebar';

/**
 * Chrome for the authenticated screens (bandeja, detalle-validacion, configuracion). Per the
 * design handoff (`Gestor de Facturas.dc.html`) there is NO top header bar: the product identity,
 * the sol/luna theme toggle and the profile row all live in the sidebar, and the routed screen
 * owns its own page title. `/login` is deliberately routed OUTSIDE this layout (app.routes.ts) so
 * it renders with no chrome. This container is the only injector of `SidebarService`,
 * `TemaService` and `SessionService` — `Sidebar` stays presentational.
 */
@Component({
  imports: [RouterOutlet, Sidebar],
  selector: 'app-shell-layout',
  styleUrl: './shell-layout.css',
  templateUrl: './shell-layout.html',
})
export class ShellLayout {
  protected readonly tema = inject(TemaService);
  protected readonly sidebar = inject(SidebarService);
  protected readonly session = inject(SessionService);

  /** Sol/luna toggle: flips the effective theme, persisting an explicit claro/oscuro choice. */
  alternarTema(): void {
    this.tema.establecer(this.tema.efectivo() === 'oscuro' ? 'claro' : 'oscuro');
  }
}
