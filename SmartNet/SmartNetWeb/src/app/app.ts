import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

/**
 * Root component: a bare routing host. All chrome for the authenticated screens lives in
 * `ShellLayout` (app.routes.ts nests those routes under it); `/login` renders on its own with no
 * header and no theme control.
 */
@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  template: '<router-outlet></router-outlet>',
})
export class App {}
