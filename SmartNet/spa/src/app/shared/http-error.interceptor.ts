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
 */
export const httpErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const session = inject(SessionService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401) {
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
