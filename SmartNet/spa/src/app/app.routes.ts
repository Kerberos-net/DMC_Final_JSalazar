import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: 'bandeja',
    loadComponent: () =>
      import('./inbox/feature/inbox-page/inbox-page').then((m) => m.InboxPage),
  },
  { path: '', pathMatch: 'full', redirectTo: 'bandeja' },
];
