import { TestBed } from '@angular/core/testing';
import {
  HttpClient,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { Router } from '@angular/router';
import { vi } from 'vitest';
import { httpErrorInterceptor } from './http-error.interceptor';
import { SessionService } from './session.service';

describe('httpErrorInterceptor', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let limpiarSpy: ReturnType<typeof vi.fn>;
  let navigateSpy: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    limpiarSpy = vi.fn();
    navigateSpy = vi.fn().mockResolvedValue(true);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([httpErrorInterceptor])),
        provideHttpClientTesting(),
        { provide: SessionService, useValue: { limpiar: limpiarSpy } },
        { provide: Router, useValue: { navigate: navigateSpy } },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('clears the session and redirects to /login on a 401 response', async () => {
    const promise = http.get('/api/facturas/42').subscribe({ error: () => undefined });

    const req = httpMock.expectOne('/api/facturas/42');
    req.flush('Unauthorized body', { status: 401, statusText: 'Unauthorized' });

    await Promise.resolve();

    expect(limpiarSpy).toHaveBeenCalled();
    expect(navigateSpy).toHaveBeenCalledWith(['/login']);
  });

  it('does not clear the session or redirect on a non-401 error', async () => {
    http.get('/api/facturas/42').subscribe({ error: () => undefined });

    const req = httpMock.expectOne('/api/facturas/42');
    req.flush('Server error', { status: 500, statusText: 'Server Error' });

    await Promise.resolve();

    expect(limpiarSpy).not.toHaveBeenCalled();
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('does NOT clear the session or redirect on a 401 from POST /api/sesion (login failure, not session expiry)', async () => {
    let leaked: unknown;
    http.post('/api/sesion', { nombreUsuario: 'x', clave: 'y' }).subscribe({
      error: (err) => {
        leaked = err;
      },
    });

    const req = httpMock.expectOne('/api/sesion');
    req.flush(
      { type: 't', title: 'Credenciales inválidas', status: 401, detail: 'El usuario o la clave no son válidos.' },
      { status: 401, statusText: 'Unauthorized' }
    );

    await Promise.resolve();

    expect(limpiarSpy).not.toHaveBeenCalled();
    expect(navigateSpy).not.toHaveBeenCalled();
    expect(JSON.stringify(leaked)).toContain('no son válidos');
  });

  it('propagates the error without leaking the raw response body to the caller', async () => {
    let leaked: unknown;
    http.get('/api/facturas/42').subscribe({
      error: (err) => {
        leaked = err;
      },
    });

    const req = httpMock.expectOne('/api/facturas/42');
    req.flush('<script>alert(1)</script>', { status: 401, statusText: 'Unauthorized' });

    await Promise.resolve();

    expect(leaked).toBeDefined();
    expect(JSON.stringify(leaked)).not.toContain('<script>');
  });
});
