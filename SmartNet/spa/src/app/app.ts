import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { PreferenciaTema, TemaService } from './shared/tema.service';

/**
 * design.md D1/Open Question 2: the theme control lives in this shell header, reachable from
 * every screen including login (spa-theme-toggle spec: "reachable from every in-scope screen").
 */
@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App {
  protected readonly tema = inject(TemaService);

  onCambiarTema(preferencia: PreferenciaTema): void {
    this.tema.establecer(preferencia);
  }
}
