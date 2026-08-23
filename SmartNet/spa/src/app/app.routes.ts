import { Routes } from '@angular/router';
import { authGuard } from './shared/auth.guard';

export const routes: Routes = [
  {
    path: 'bandeja',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./inbox/feature/inbox-page/inbox-page').then((m) => m.InboxPage),
  },
  { path: '', pathMatch: 'full', redirectTo: 'bandeja' },
];
