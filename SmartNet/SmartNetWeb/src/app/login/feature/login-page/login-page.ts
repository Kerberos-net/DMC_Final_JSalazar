import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { SessionService } from '../../../shared/session.service';
import { ProblemaDetails } from '../../../shared/problema.model';

/**
 * PR5 addition (BACKLOG #12, task added by the project owner, not in the original tasks.md):
 * the `/login` page `authGuard`/`httpErrorInterceptor` (PR4) redirect to but that never existed
 * (apply-progress PR4's documented gap). Calls `POST /api/sesion` (`SesionEndpoints.cs`), and on
 * success updates `SessionService` and navigates to the original protected route (`?returnUrl=`,
 * set by the guard) or `/bandeja` by default. On 401/422 shows the `ProblemaDetails.detail`.
 */
@Component({
  selector: 'app-login-page',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login-page.html',
  styleUrl: './login-page.css',
})
export class LoginPage {
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly nombreUsuario = signal('');
  readonly clave = signal('');
  readonly enviando = signal(false);
  readonly problema = signal<ProblemaDetails | null>(null);

  private returnUrl: string | null = null;

  constructor() {
    this.route.queryParamMap.subscribe((params) => {
      this.returnUrl = params.get('returnUrl');
    });
  }

  async enviar(): Promise<void> {
    this.enviando.set(true);
    this.problema.set(null);
    try {
      await this.session.iniciarSesion(this.nombreUsuario(), this.clave());
      await this.router.navigate([this.returnUrl ?? '/bandeja']);
    } catch (err) {
      if (err instanceof HttpErrorResponse && err.error) {
        this.problema.set(err.error as ProblemaDetails);
      } else {
        this.problema.set({
          type: 'about:blank',
          title: 'Error',
          status: 0,
          detail: 'No se pudo iniciar sesión. Inténtelo de nuevo.',
        });
      }
    } finally {
      this.enviando.set(false);
    }
  }
}
