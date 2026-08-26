import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router } from '@angular/router';
import { of } from 'rxjs';
import { vi } from 'vitest';
import { LoginPage } from './login-page';

describe('LoginPage', () => {
  let httpMock: HttpTestingController;
  let navigateSpy: ReturnType<typeof vi.fn>;

  async function crearPagina(returnUrl: string | null = null) {
    navigateSpy = vi.fn().mockResolvedValue(true);

    await TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: Router, useValue: { navigate: navigateSpy } },
        {
          provide: ActivatedRoute,
          useValue: { queryParamMap: of({ get: () => returnUrl }) },
        },
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(LoginPage);
    fixture.detectChanges();
    return fixture;
  }

  afterEach(() => {
    httpMock.verify();
  });

  it('submits nombreUsuario/clave and navigates to the default bandeja on success', async () => {
    const fixture = await crearPagina();

    const usuarioInput: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="nombreUsuario"]');
    const claveInput: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="clave"]');
    usuarioInput.value = 'ana.torres';
    usuarioInput.dispatchEvent(new Event('input'));
    claveInput.value = 's3cr3t';
    claveInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('[data-testid="enviar"]') as HTMLButtonElement).click();

    const req = httpMock.expectOne('/api/sesion');
    expect(req.request.body).toEqual({ nombreUsuario: 'ana.torres', clave: 's3cr3t' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await Promise.resolve();
    await Promise.resolve();

    expect(navigateSpy).toHaveBeenCalledWith(['/bandeja']);
  });

  it('navigates to the original protected route (returnUrl) on success when present', async () => {
    const fixture = await crearPagina('/detalle/42');

    (fixture.nativeElement.querySelector('[data-testid="nombreUsuario"]') as HTMLInputElement).value = 'a';
    fixture.nativeElement
      .querySelector('[data-testid="nombreUsuario"]')
      .dispatchEvent(new Event('input'));
    (fixture.nativeElement.querySelector('[data-testid="clave"]') as HTMLInputElement).value = 'b';
    fixture.nativeElement.querySelector('[data-testid="clave"]').dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('[data-testid="enviar"]') as HTMLButtonElement).click();
    httpMock.expectOne('/api/sesion').flush(null, { status: 204, statusText: 'No Content' });
    await Promise.resolve();
    await Promise.resolve();

    expect(navigateSpy).toHaveBeenCalledWith(['/detalle/42']);
  });

  it('shows the problem detail on a 401 and does not navigate', async () => {
    const fixture = await crearPagina();

    (fixture.nativeElement.querySelector('[data-testid="nombreUsuario"]') as HTMLInputElement).value = 'a';
    fixture.nativeElement
      .querySelector('[data-testid="nombreUsuario"]')
      .dispatchEvent(new Event('input'));
    (fixture.nativeElement.querySelector('[data-testid="clave"]') as HTMLInputElement).value = 'wrong';
    fixture.nativeElement.querySelector('[data-testid="clave"]').dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('[data-testid="enviar"]') as HTMLButtonElement).click();
    httpMock.expectOne('/api/sesion').flush(
      { type: 't', title: 'Credenciales inválidas', status: 401, detail: 'El usuario o la clave no son válidos.' },
      { status: 401, statusText: 'Unauthorized' }
    );
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    const alerta = fixture.nativeElement.querySelector('[role="alert"]');
    expect(alerta.textContent).toContain('El usuario o la clave no son válidos.');
    expect(navigateSpy).not.toHaveBeenCalled();
  });
});
