import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { InboxService } from './inbox.service';
import { BandejaItem, PaginaBandeja } from '../models/bandeja-item.model';

describe('InboxService', () => {
  let service: InboxService;
  let httpMock: HttpTestingController;

  const itemPromovido: BandejaItem = {
    inboxEventId: 1,
    procesamientoId: 100,
    origen: 'FACTURA',
    estadoConsumo: 'PROMOVIDO',
    creadoEn: '2026-08-10T10:00:00Z',
    facturaId: 42,
    proveedorCodigo: 'P00001',
    rucProveedor: '20100000001',
    indicadores: {
      esProveedorGenerico: false,
      posibleDuplicado: false,
      tieneCamposNoExtraidos: false,
      fechaEnDomingo: false,
      afectacionMixta: false,
    },
    motivoDescarte: null,
    errores: [],
    reprocesarDisponibleEn: null,
  };

  const paginaVacia: PaginaBandeja<BandejaItem> = {
    items: [],
    pagina: 1,
    tamanioPagina: 20,
    totalRegistros: 0,
    totalPaginas: 0,
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

  it('requests GET /api/bandeja with orden and no estado when estado is omitted', async () => {
    const promise = service.cargar({});

    const req = httpMock.expectOne(
      (r) => r.url === '/api/bandeja' && r.params.get('orden') === 'desc'
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.params.has('estado')).toBe(false);
    req.flush({ items: [itemPromovido], pagina: 1, tamanioPagina: 20, totalRegistros: 1, totalPaginas: 1 });

    await promise;
    expect(service.items()).toEqual([itemPromovido]);
    expect(service.loading()).toBe(false);
  });

  it('includes estado, desde, hasta, proveedor, and pagina when set', async () => {
    const promise = service.cargar({
      estado: 'DESCARTADO',
      orden: 'asc',
      desde: '2026-01-01',
      hasta: '2026-01-31',
      proveedor: 'P001',
      pagina: 2,
    });

    const req = httpMock.expectOne(
      (r) =>
        r.url === '/api/bandeja' &&
        r.params.get('orden') === 'asc' &&
        r.params.get('estado') === 'DESCARTADO' &&
        r.params.get('desde') === '2026-01-01' &&
        r.params.get('hasta') === '2026-01-31' &&
        r.params.get('proveedor') === 'P001' &&
        r.params.get('pagina') === '2'
    );
    req.flush(paginaVacia);

    await promise;
  });

  it('caches the last-used filters as ultimosFiltros after each call', async () => {
    const promise = service.cargar({ estado: 'PROMOVIDO', pagina: 3 });
    httpMock.expectOne(() => true).flush(paginaVacia);
    await promise;

    expect(service.ultimosFiltros()).toEqual({ estado: 'PROMOVIDO', pagina: 3 });
  });

  it('exposes pagina/totalPaginas/totalRegistros from the envelope', async () => {
    const promise = service.cargar({});
    httpMock
      .expectOne(() => true)
      .flush({ items: [itemPromovido], pagina: 1, tamanioPagina: 20, totalRegistros: 1, totalPaginas: 1 });
    await promise;

    expect(service.pagina()).toBe(1);
    expect(service.totalPaginas()).toBe(1);
    expect(service.totalRegistros()).toBe(1);
  });

  it('sets loading true while the request is in flight', () => {
    const promise = service.cargar({});
    expect(service.loading()).toBe(true);

    const req = httpMock.expectOne(() => true);
    req.flush(paginaVacia);

    return promise;
  });

  it('sets an error and clears items on request failure', async () => {
    const promise = service.cargar({});
    const req = httpMock.expectOne(() => true);
    req.flush('boom', { status: 500, statusText: 'Server Error' });

    await expect(promise).rejects.toBeTruthy();
    expect(service.error()).not.toBeNull();
    expect(service.loading()).toBe(false);
  });

  it('reprocesar() posts to POST /api/incidencias/{id}/reprocesar', async () => {
    const promise = service.reprocesar(100);

    const req = httpMock.expectOne('/api/incidencias/100/reprocesar');
    expect(req.request.method).toBe('POST');
    req.flush({});

    await promise;
  });
});
