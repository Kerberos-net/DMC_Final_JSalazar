import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { SessionService } from './session.service';

describe('SessionService', () => {
  let service: SessionService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(SessionService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('starts with no user and not authenticated', () => {
    expect(service.usuario()).toBeNull();
    expect(service.autenticado()).toBe(false);
  });

  it('verificar() sets the user and reports authenticated on 200', async () => {
    const promise = service.verificar();

    const req = httpMock.expectOne('/api/sesion');
    expect(req.request.method).toBe('GET');
    req.flush({ nombreUsuario: 'ana.torres' });

    const resultado = await promise;
    expect(resultado).toBe(true);
    expect(service.usuario()).toBe('ana.torres');
    expect(service.autenticado()).toBe(true);
  });

  it('verificar() clears the user and reports not authenticated on 401', async () => {
    const promise = service.verificar();

    const req = httpMock.expectOne('/api/sesion');
    req.flush('Unauthorized', { status: 401, statusText: 'Unauthorized' });

    const resultado = await promise;
    expect(resultado).toBe(false);
    expect(service.usuario()).toBeNull();
    expect(service.autenticado()).toBe(false);
  });

  it('limpiar() clears a previously known user', async () => {
    const promise = service.verificar();
    const req = httpMock.expectOne('/api/sesion');
    req.flush({ nombreUsuario: 'ana.torres' });
    await promise;

    service.limpiar();

    expect(service.usuario()).toBeNull();
    expect(service.autenticado()).toBe(false);
  });
});
