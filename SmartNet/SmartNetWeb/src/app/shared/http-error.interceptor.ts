import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { SessionService } from './session.service';

/**
 * Global 401 handler (design.md D5: "adds withInterceptors 401 handler"). The `__Host-session`
 * cookie (BACKLOG #2) is HttpOnly and same-origin — the browser attaches it automatically, so
 * this interceptor never touches the cookie itself. It only reacts to the API's own signal that
 * the session is gone (`OnRedirectToLogin` in `Program.cs` maps an expired/missing cookie to a
 * bare 401, never a login-page redirect).
 *
 * A 401's body is never forwarded to subscribers: it carries no `ProblemaDetails` contract (it
 * is an auth failure, not a business error) and MUST NOT reach the DOM. Every other status
 * passes through unchanged so the detalle feature (BACKLOG #12 Phase 5) can read its
 * `ProblemaDetails` body (422/409/412) to drive its own UX.
 *
 * EXCEPTION (BACKLOG #12 Phase 5, LoginPage): `POST /api/sesion` (the login submission itself)
 * IS exempted from this handling. A 401 from THAT specific request is a "wrong credentials"
 * business response WITH a `ProblemaDetails` body (`SesionEndpoints.PostSesionAsync` — design.md
 * Decision 6), not a "session expired" signal -- stripping its body/redirecting to `/login` (where
 * the user already is) would make login failures silently unreadable.
 */
const ES_LOGIN = (req: { url: string; method: string }): boolean =>
  req.url === '/api/sesion' && req.method === 'POST';

export const httpErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const session = inject(SessionService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && !ES_LOGIN(req)) {
        session.limpiar();
        void router.navigate(['/login']);

        return throwError(
          () =>
            new HttpErrorResponse({
              status: error.status,
              statusText: error.statusText,
              url: error.url ?? undefined,
            })
        );
      }

      return throwError(() => error);
    })
  );
};
