import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { PreferenciaTema, TemaService } from '../tema.service';

/**
 * Chrome for the authenticated screens (bandeja, detalle-validacion, configuracion): the product
 * marca and the theme `<select>` in a shared header, with a `<router-outlet>` below for the routed
 * screen. `/login` is deliberately routed OUTSIDE this layout (app.routes.ts) so it renders with no
 * header and no theme control (spa-visual-login spec: "Login renders without the app shell chrome";
 * spa-theme-toggle spec: the control is reachable from the authenticated screens only).
 */
@Component({
  imports: [RouterOutlet],
  selector: 'app-shell-layout',
  styleUrl: './shell-layout.css',
  templateUrl: './shell-layout.html',
})
export class ShellLayout {
  protected readonly tema = inject(TemaService);

  onCambiarTema(preferencia: PreferenciaTema): void {
    this.tema.establecer(preferencia);
  }
}
