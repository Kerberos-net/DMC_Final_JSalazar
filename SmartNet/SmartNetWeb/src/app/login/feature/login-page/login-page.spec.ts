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

  it('renders the handoff card composition elements in vertical order', async () => {
    const fixture = await crearPagina();
    const root: HTMLElement = fixture.nativeElement;

    const orden = [
      root.querySelector('[data-testid="logo-badge"]'),
      root.querySelector('h1'),
      root.querySelector('[data-testid="subtitulo"]'),
      root.querySelector('[data-testid="nombreUsuario"]'),
      root.querySelector('[data-testid="clave"]'),
      root.querySelector('[data-testid="error-slot"]'),
      root.querySelector('[data-testid="enviar"]'),
      root.querySelector('[data-testid="pie"]'),
    ];

    expect(orden.every((el) => el !== null)).toBe(true);
    for (let i = 0; i < orden.length - 1; i++) {
      const rel = orden[i]!.compareDocumentPosition(orden[i + 1]!);
      expect(rel & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    }

    expect(root.querySelector('h1')!.textContent).toContain('Gestor de Facturas de Compra');
    expect(root.querySelector('[data-testid="subtitulo"]')!.textContent).toContain(
      'Inicia sesion para revisar y validar facturas'
    );
    expect(root.querySelector('[data-testid="pie"]')!.textContent).toContain(
      'Credenciales verificadas contra SQL Server'
    );
  });

  it('presents placeholder-labeled inputs with an accessible name and no visible <label>', async () => {
    const fixture = await crearPagina();
    const root: HTMLElement = fixture.nativeElement;

    expect(root.querySelectorAll('label').length).toBe(0);

    const usuario = root.querySelector('[data-testid="nombreUsuario"]') as HTMLInputElement;
    const clave = root.querySelector('[data-testid="clave"]') as HTMLInputElement;
    expect(usuario.getAttribute('aria-label')).toBeTruthy();
    expect(usuario.getAttribute('placeholder')).toBeTruthy();
    expect(clave.getAttribute('aria-label')).toBeTruthy();
    expect(clave.getAttribute('placeholder')).toBeTruthy();
  });

  it('renders the submit button as a full-width "Ingresar" submit control', async () => {
    const fixture = await crearPagina();
    const boton = fixture.nativeElement.querySelector('[data-testid="enviar"]') as HTMLButtonElement;

    expect(boton.type).toBe('submit');
    expect(boton.textContent?.trim()).toBe('Ingresar');
    expect(boton.classList.contains('login-page__enviar')).toBe(true);
  });

  it('renders the auth error inline, not as a .banner--error block', async () => {
    const fixture = await crearPagina();

    (fixture.nativeElement.querySelector('[data-testid="nombreUsuario"]') as HTMLInputElement).value = 'a';
    fixture.nativeElement.querySelector('[data-testid="nombreUsuario"]').dispatchEvent(new Event('input'));
    (fixture.nativeElement.querySelector('[data-testid="clave"]') as HTMLInputElement).value = 'wrong';
    fixture.nativeElement.querySelector('[data-testid="clave"]').dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('[data-testid="enviar"]') as HTMLButtonElement).click();
    httpMock.expectOne('/api/sesion').flush(
      { type: 't', title: 'x', status: 401, detail: 'El usuario o la clave no son válidos.' },
      { status: 401, statusText: 'Unauthorized' }
    );
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    const slot = fixture.nativeElement.querySelector('[data-testid="error-slot"]') as HTMLElement;
    const alerta = fixture.nativeElement.querySelector('[role="alert"]') as HTMLElement;
    expect(slot.contains(alerta)).toBe(true);
    expect(fixture.nativeElement.querySelector('.banner--error')).toBeNull();
    expect(alerta.textContent).toContain('El usuario o la clave no son válidos.');
  });
});
