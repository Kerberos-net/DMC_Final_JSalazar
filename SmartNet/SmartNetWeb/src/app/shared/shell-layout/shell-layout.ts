import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { PreferenciaTema, TemaService } from '../tema.service';
import { SidebarService } from '../sidebar.service';
import { Sidebar } from './sidebar/sidebar';

/**
 * Chrome for the authenticated screens (bandeja, detalle-validacion, configuracion): the macOS
 * sidebar navigation plus a header with the product marca and the theme `<select>`, with a
 * `<router-outlet>` for the routed screen. `/login` is deliberately routed OUTSIDE this layout
 * (app.routes.ts) so it renders with no chrome (spa-visual-login spec: "Login renders without the
 * app shell chrome"; spa-shell-nav spec). This container is the only injector of `SidebarService`
 * and `TemaService` — `Sidebar` stays presentational.
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

  onCambiarTema(preferencia: PreferenciaTema): void {
    this.tema.establecer(preferencia);
  }
}
