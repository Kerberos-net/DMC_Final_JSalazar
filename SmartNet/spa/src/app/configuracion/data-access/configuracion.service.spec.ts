import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ConfiguracionService } from './configuracion.service';
import { ConfiguracionEntrada } from '../models/configuracion.model';

describe('ConfiguracionService', () => {
  let service: ConfiguracionService;
  let httpMock: HttpTestingController;

  const entradaTelegram: ConfiguracionEntrada = {
    seccion: 'TELEGRAM',
    clave: 'DESTINO_CHAT_ID',
    tipo: 'TEXTO',
    valor: null,
    valorPorDefecto: null,
    descripcion: 'Chat de Telegram al que se envian las alertas.',
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ConfiguracionService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('starts with an empty list, not loading, no error', () => {
    expect(service.entradas()).toEqual([]);
    expect(service.loading()).toBe(false);
    expect(service.error()).toBeNull();
  });

  it('cargar() requests GET /api/configuracion without seccion when omitted', async () => {
    const promise = service.cargar();

    const req = httpMock.expectOne((r) => r.url === '/api/configuracion' && !r.params.has('seccion'));
    expect(req.request.method).toBe('GET');
    req.flush([entradaTelegram]);

    await promise;
    expect(service.entradas()).toEqual([entradaTelegram]);
  });

  it('cargar(seccion) requests GET /api/configuracion?seccion=', async () => {
    const promise = service.cargar('TELEGRAM');

    const req = httpMock.expectOne((r) => r.url === '/api/configuracion' && r.params.get('seccion') === 'TELEGRAM');
    req.flush([entradaTelegram]);

    await promise;
  });

  it('sets loading true while the request is in flight', () => {
    const promise = service.cargar();
    expect(service.loading()).toBe(true);

    const req = httpMock.expectOne(() => true);
    req.flush([]);

    return promise;
  });

  it('sets an error and rethrows on request failure', async () => {
    const promise = service.cargar();
    const req = httpMock.expectOne(() => true);
    req.flush('boom', { status: 500, statusText: 'Server Error' });

    await expect(promise).rejects.toBeTruthy();
    expect(service.error()).not.toBeNull();
    expect(service.loading()).toBe(false);
  });

  it('actualizar() PUTs to /api/configuracion/{seccion}/{clave} with { valor }', async () => {
    const promise = service.actualizar('TELEGRAM', 'DESTINO_CHAT_ID', '-100200300');

    const req = httpMock.expectOne('/api/configuracion/TELEGRAM/DESTINO_CHAT_ID');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ valor: '-100200300' });
    req.flush({});

    await promise;
  });

  it('actualizar() updates the in-memory entrada on success without a refetch', async () => {
    const promiseCargar = service.cargar('TELEGRAM');
    httpMock.expectOne(() => true).flush([entradaTelegram]);
    await promiseCargar;

    const promiseActualizar = service.actualizar('TELEGRAM', 'DESTINO_CHAT_ID', '-1');
    httpMock.expectOne('/api/configuracion/TELEGRAM/DESTINO_CHAT_ID').flush({});
    await promiseActualizar;

    expect(service.entradas()[0].valor).toBe('-1');
  });

  it('actualizar() propagates a rejected write and does not touch the in-memory entrada', async () => {
    const promiseCargar = service.cargar('TELEGRAM');
    httpMock.expectOne(() => true).flush([entradaTelegram]);
    await promiseCargar;

    const promiseActualizar = service.actualizar('TELEGRAM', 'DESTINO_CHAT_ID', 'x');
    httpMock
      .expectOne('/api/configuracion/TELEGRAM/DESTINO_CHAT_ID')
      .flush(
        { type: 'x', title: 'invalido', status: 422 },
        { status: 422, statusText: 'Unprocessable Entity' }
      );

    await expect(promiseActualizar).rejects.toBeTruthy();
    expect(service.entradas()[0].valor).toBeNull();
  });
});
