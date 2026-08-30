import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

/**
 * design D5 — presentational sidebar for the authenticated shell (`spa-shell-nav`). Owns no state:
 * the collapsed flag comes in as an input and the collapse toggle emits `alternar`; `ShellLayout`
 * (the container) is the only injector of `SidebarService`. Lists ONLY destinations that have a
 * real route today — `Bandeja` and, after one hairline divider, `Configuración`. Glyphs are hand
 * built from `<div>` per `DESIGN.md` (no `<svg>`, no icon font).
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

  readonly alternar = output<void>();
}
