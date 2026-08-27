import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { ProveedorService } from './proveedor.service';
import { BusquedaProveedoresRespuesta } from './proveedor.model';

describe('ProveedorService', () => {
  let service: ProveedorService;
  let httpMock: HttpTestingController;

  const pagina1: BusquedaProveedoresRespuesta = {
    resultados: [
      { codigo: 'P00011', nombre: 'ACME ANDINA EIRL', ruc: '20100000002' },
      { codigo: 'P00010', nombre: 'ACME PERU SAC', ruc: '20100000001' },
    ],
    hayMas: true,
  };

  const esperar = (ms: number) => new Promise((r) => setTimeout(r, ms));

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ProveedorService);
    service.debounceMs = 5;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('debounces rapid input into a single GET with the last term and parses the readonly signal', async () => {
    service.buscar('AC');
    service.buscar('ACM');
    service.buscar('ACME');

    await esperar(20);

    const req = httpMock.expectOne((r) => r.url === '/api/catalogos/proveedores');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('q')).toBe('ACME');
    expect(req.request.params.get('pagina')).toBe('1');
    req.flush(pagina1);

    await Promise.resolve();
    expect(service.resultados()).toEqual(pagina1.resultados);
    expect(service.hayMas()).toBe(true);
  });

  it('masResultados() requests the next page and appends to the signal', async () => {
    service.buscar('ACME');
    await esperar(20);
    httpMock.expectOne((r) => r.url === '/api/catalogos/proveedores').flush(pagina1);
    await esperar(0);

    const masPromise = service.masResultados();

    const req = httpMock.expectOne((r) => r.url === '/api/catalogos/proveedores');
    expect(req.request.params.get('pagina')).toBe('2');
    req.flush({
      resultados: [{ codigo: 'P00012', nombre: 'ACME SUR SAC', ruc: null }],
      hayMas: false,
    } as BusquedaProveedoresRespuesta);
    await masPromise;

    expect(service.resultados().map((p) => p.codigo)).toEqual(['P00011', 'P00010', 'P00012']);
    expect(service.hayMas()).toBe(false);
  });

  it('does not issue a request for a blank or too-short term and clears results', async () => {
    service.buscar('ACME');
    await esperar(20);
    httpMock.expectOne((r) => r.url === '/api/catalogos/proveedores').flush(pagina1);
    await esperar(0);

    service.buscar('a');
    await esperar(20);
    httpMock.expectNone((r) => r.url === '/api/catalogos/proveedores');
    expect(service.resultados()).toEqual([]);
  });
});
