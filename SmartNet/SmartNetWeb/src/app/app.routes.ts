import { Routes } from '@angular/router';
import { authGuard } from './shared/auth.guard';
import { ShellLayout } from './shared/shell-layout/shell-layout';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./login/feature/login-page/login-page').then((m) => m.LoginPage),
  },
  {
    path: '',
    component: ShellLayout,
    children: [
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
      {
        path: 'configuracion',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./configuracion/feature/configuracion-page/configuracion-page').then(
            (m) => m.ConfiguracionPage
          ),
      },
      {
        path: 'catalogos/proveedores',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./catalogos/feature/proveedores-page/proveedores-page').then(
            (m) => m.ProveedoresPage
          ),
      },
      {
        path: 'catalogos/plan-contable',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./catalogos/feature/plan-contable-page/plan-contable-page').then(
            (m) => m.PlanContablePage
          ),
      },
      {
        path: 'catalogos/tipo-cambio',
        canActivate: [authGuard],
        loadComponent: () =>
          import('./catalogos/feature/tipo-cambio-page/tipo-cambio-page').then(
            (m) => m.TipoCambioPage
          ),
      },
      { path: '', pathMatch: 'full', redirectTo: 'bandeja' },
    ],
  },
];
