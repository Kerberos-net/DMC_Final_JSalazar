import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { FacturaService } from './factura.service';
import { FacturaRespuesta } from '../models/factura.model';

describe('FacturaService', () => {
  let service: FacturaService;
  let httpMock: HttpTestingController;

  const factura: FacturaRespuesta = {
    facturaId: 42,
    estado: 'ABIERTA',
    proveedorCodigo: 'P00123',
    rucProveedor: '20123456789',
    tipoComprobante: 'Factura',
    numero: 'F001-100',
    totalOrig: 118,
    moneda: 'PEN',
    fechaEmision: '2026-08-10',
    motivo: null,
    afectacion: 'Gravada',
    esProveedorGenerico: false,
    posibleDuplicado: false,
    tieneCamposNoExtraidos: false,
    afectacionMixta: false,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(FacturaService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('cargar() GETs the factura and mirrors its ETag from the response header', async () => {
    const promise = service.cargar(42);

    const req = httpMock.expectOne('/api/facturas/42');
    expect(req.request.method).toBe('GET');
    req.flush(factura, { headers: { ETag: '"AAAAAAAAB9E="' } });

    await promise;
    expect(service.factura()).toEqual(factura);
    expect(service.etag()).toBe('"AAAAAAAAB9E="');
  });

  it('guardar() PATCHes with the mirrored ETag as If-Match and updates state from the response', async () => {
    const cargaPromise = service.cargar(42);
    httpMock.expectOne('/api/facturas/42').flush(factura, { headers: { ETag: '"v1"' } });
    await cargaPromise;

    const guardarPromise = service.guardar(42, { rucProveedor: '20999999999' });

    const req = httpMock.expectOne('/api/facturas/42');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.headers.get('If-Match')).toBe('"v1"');
    expect(req.request.body).toEqual({ rucProveedor: '20999999999' });
    const actualizada = { ...factura, rucProveedor: '20999999999' };
    req.flush(actualizada, { headers: { ETag: '"v2"' } });

    await guardarPromise;
    expect(service.factura()).toEqual(actualizada);
    expect(service.etag()).toBe('"v2"');
  });

  it('confirmarAfectacion() posts to /confirmar-afectacion with If-Match and updates state', async () => {
    const cargaPromise = service.cargar(42);
    httpMock.expectOne('/api/facturas/42').flush(factura, { headers: { ETag: '"v1"' } });
    await cargaPromise;

    const confirmarPromise = service.confirmarAfectacion(42, true);

    const req = httpMock.expectOne('/api/facturas/42/confirmar-afectacion');
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('If-Match')).toBe('"v1"');
    expect(req.request.body).toEqual({ esMixta: true });
    const actualizada = { ...factura, afectacionMixta: true };
    req.flush(actualizada, { headers: { ETag: '"v2"' } });

    await confirmarPromise;
    expect(service.factura()).toEqual(actualizada);
    expect(service.etag()).toBe('"v2"');
  });

  it('validar() posts to /validar with fechaCorteContable as a query param', async () => {
    const promise = service.validar(42, '2026-08-23');

    const req = httpMock.expectOne(
      (r) => r.url === '/api/facturas/42/validar' && r.params.get('fechaCorteContable') === '2026-08-23'
    );
    expect(req.request.method).toBe('POST');
    req.flush(null);

    await expect(promise).resolves.toBeUndefined();
  });
});
