import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { InboxService } from './inbox.service';
import { BandejaItem } from '../models/bandeja-item.model';

describe('InboxService', () => {
  let service: InboxService;
  let httpMock: HttpTestingController;

  const itemPromovido: BandejaItem = {
    inboxEventId: 1,
    estadoConsumo: 'PROMOVIDO',
    creadoEn: '2026-08-10T10:00:00Z',
    facturaId: 42,
    indicadores: {
      esProveedorGenerico: false,
      posibleDuplicado: false,
      tieneCamposNoExtraidos: false,
      fechaEnDomingo: false,
      afectacionMixta: false,
    },
    motivoDescarte: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(InboxService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('starts with an empty list, not loading, no error', () => {
    expect(service.items()).toEqual([]);
    expect(service.loading()).toBe(false);
    expect(service.error()).toBeNull();
  });

  it('requests GET /api/bandeja with orden and no estado when estado is null', async () => {
    const promise = service.cargar(null, 'desc');

    const req = httpMock.expectOne(
      (r) => r.url === '/api/bandeja' && r.params.get('orden') === 'desc'
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.params.has('estado')).toBe(false);
    req.flush([itemPromovido]);

    await promise;
    expect(service.items()).toEqual([itemPromovido]);
    expect(service.loading()).toBe(false);
  });

  it('includes estado in the query when a filter is set', async () => {
    const promise = service.cargar('DESCARTADO', 'asc');

    const req = httpMock.expectOne(
      (r) =>
        r.url === '/api/bandeja' &&
        r.params.get('orden') === 'asc' &&
        r.params.get('estado') === 'DESCARTADO'
    );
    req.flush([]);

    await promise;
  });

  it('sets loading true while the request is in flight', () => {
    const promise = service.cargar(null, 'desc');
    expect(service.loading()).toBe(true);

    const req = httpMock.expectOne(() => true);
    req.flush([]);

    return promise;
  });

  it('sets an error and clears items on request failure', async () => {
    const promise = service.cargar(null, 'desc');
    const req = httpMock.expectOne(() => true);
    req.flush('boom', { status: 500, statusText: 'Server Error' });

    await expect(promise).rejects.toBeTruthy();
    expect(service.error()).not.toBeNull();
    expect(service.loading()).toBe(false);
  });
});
