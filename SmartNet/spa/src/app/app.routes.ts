import { Routes } from '@angular/router';
import { authGuard } from './shared/auth.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./login/feature/login-page/login-page').then((m) => m.LoginPage),
  },
  {
    path: 'bandeja',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./inbox/feature/inbox-page/inbox-page').then((m) => m.InboxPage),
  },
  {
    path: 'detalle/:id',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./detalle/feature/detalle-page/detalle-page').then((m) => m.DetallePage),
  },
  { path: '', pathMatch: 'full', redirectTo: 'bandeja' },
];
