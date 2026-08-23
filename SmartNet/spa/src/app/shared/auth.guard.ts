import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { SessionService } from './session.service';

/**
 * Route guard gating navigation to protected routes on an active session
 * (`GET /api/sesion` — BACKLOG #2's cookie auth). Redirects to `/login` when the session
 * check fails; otherwise lets the navigation proceed.
 */
export const authGuard: CanActivateFn = async () => {
  const session = inject(SessionService);
  const router = inject(Router);

  const autenticado = await session.verificar();
  return autenticado ? true : router.createUrlTree(['/login']);
};
