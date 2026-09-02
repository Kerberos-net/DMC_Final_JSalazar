import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { AsientoService } from './asiento.service';
import { AsientoRespuesta, FacturaAsientoRespuesta, LineaAsientoRequest } from '../models/asiento.model';

describe('AsientoService', () => {
  let service: AsientoService;
  let httpMock: HttpTestingController;

  const asiento: AsientoRespuesta = {
    asientoContableId: 7,
    estado: 'BORRADOR',
    numeroAsiento: null,
    proveedorCodigo: 'P00123',
    fechaContable: '2026-08-10',
    motivoDescripcion: null,
    tipoCambioVenta: null,
    basePEN: 100,
    igvPEN: 18,
    lineas: [
      {
        lineaId: 1,
        orden: 1,
        bloque: 'PRINCIPAL',
        tipo: 'D',
        debe: 118,
        haber: 0,
        cuentaCodigo: '639915',
        cuentaDescripcion: null,
        ctaReflejaCodigo: null,
        ctaPuenteCodigo: null,
      },
    ],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AsientoService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('cargarPorFactura() GETs /facturas/{id}/asiento and mirrors the nested asiento + its ETag', async () => {
    const promise = service.cargarPorFactura(42);

    const req = httpMock.expectOne('/api/facturas/42/asiento');
    expect(req.request.method).toBe('GET');
    const cuerpo: FacturaAsientoRespuesta = { asientoContableId: 7, asiento };
    req.flush(cuerpo, { headers: { ETag: '"a1"' } });

    await promise;
    expect(service.asiento()).toEqual(asiento);
    expect(service.etag()).toBe('"a1"');
  });

  it('actualizarLinea() PATCHes the línea by lineaId with If-Match, and replaces state from the response', async () => {
    const cargaPromise = service.cargarPorFactura(42);
    httpMock
      .expectOne('/api/facturas/42/asiento')
      .flush({ asientoContableId: 7, asiento }, { headers: { ETag: '"a1"' } });
    await cargaPromise;

    const linea: LineaAsientoRequest = {
      orden: 1,
      bloque: 'PRINCIPAL',
      tipo: 'D',
      debe: 100,
      haber: 0,
      cuentaCodigo: '639915',
      cuentaDescripcion: null,
      ctaReflejaCodigo: null,
      ctaPuenteCodigo: null,
    };
    const promise = service.actualizarLinea(7, 1, linea);

    const req = httpMock.expectOne('/api/asientos/7/lineas/1');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.headers.get('If-Match')).toBe('"a1"');
    expect(req.request.body).toEqual(linea);
    const actualizado = { ...asiento, lineas: [{ ...asiento.lineas[0], debe: 100 }] };
    req.flush(actualizado, { headers: { ETag: '"a2"' } });

    await promise;
    expect(service.asiento()).toEqual(actualizado);
    expect(service.etag()).toBe('"a2"');
  });

  it('agregarLinea() POSTs a new línea with If-Match, then reloads the full asiento', async () => {
    const cargaPromise = service.cargarPorFactura(42);
    httpMock
      .expectOne('/api/facturas/42/asiento')
      .flush({ asientoContableId: 7, asiento }, { headers: { ETag: '"a1"' } });
    await cargaPromise;

    const nueva: LineaAsientoRequest = {
      orden: 2,
      bloque: 'PRINCIPAL',
      tipo: 'H',
      debe: 0,
      haber: 100,
      cuentaCodigo: '421001',
      cuentaDescripcion: null,
      ctaReflejaCodigo: null,
      ctaPuenteCodigo: null,
    };
    const promise = service.agregarLinea(7, nueva);

    const postReq = httpMock.expectOne('/api/asientos/7/lineas');
    expect(postReq.request.method).toBe('POST');
    expect(postReq.request.headers.get('If-Match')).toBe('"a1"');
    postReq.flush({ lineaId: 2 }, { status: 201, statusText: 'Created', headers: { ETag: '"a2"' } });
    await Promise.resolve();
    await Promise.resolve();

    const getReq = httpMock.expectOne('/api/asientos/7');
    expect(getReq.request.method).toBe('GET');
    const conNueva = { ...asiento, lineas: [...asiento.lineas, { ...nueva, lineaId: 2 }] };
    getReq.flush(conNueva, { headers: { ETag: '"a2"' } });

    await promise;
    expect(service.asiento()).toEqual(conNueva);
    expect(service.etag()).toBe('"a2"');
  });

  it('recomponer() POSTs /asientos/{id}/recomponer with If-Match and threads the new ETag + asiento', async () => {
    const cargaPromise = service.cargarPorFactura(42);
    httpMock
      .expectOne('/api/facturas/42/asiento')
      .flush({ asientoContableId: 7, asiento }, { headers: { ETag: '"a1"' } });
    await cargaPromise;

    const promise = service.recomponer(7);

    const req = httpMock.expectOne('/api/asientos/7/recomponer');
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('If-Match')).toBe('"a1"');
    expect(req.request.body).toBeNull();
    const regenerado = { ...asiento, lineas: [] };
    req.flush(regenerado, { headers: { ETag: '"a2"' } });

    await promise;
    expect(service.asiento()).toEqual(regenerado);
    expect(service.etag()).toBe('"a2"');
  });

  it('recomponer() sends the optional { cuentaCodigo } body when provided', async () => {
    const cargaPromise = service.cargarPorFactura(42);
    httpMock
      .expectOne('/api/facturas/42/asiento')
      .flush({ asientoContableId: 7, asiento }, { headers: { ETag: '"a1"' } });
    await cargaPromise;

    const promise = service.recomponer(7, '631111');
    const req = httpMock.expectOne('/api/asientos/7/recomponer');
    expect(req.request.body).toEqual({ cuentaCodigo: '631111' });
    req.flush({ ...asiento }, { headers: { ETag: '"a2"' } });
    await promise;
    expect(service.etag()).toBe('"a2"');
  });

  it('eliminarLinea() DELETEs the línea by lineaId with If-Match, and replaces state from the response', async () => {
    const cargaPromise = service.cargarPorFactura(42);
    httpMock
      .expectOne('/api/facturas/42/asiento')
      .flush({ asientoContableId: 7, asiento }, { headers: { ETag: '"a1"' } });
    await cargaPromise;

    const promise = service.eliminarLinea(7, 1);

    const req = httpMock.expectOne('/api/asientos/7/lineas/1');
    expect(req.request.method).toBe('DELETE');
    expect(req.request.headers.get('If-Match')).toBe('"a1"');
    const sinLineas = { ...asiento, lineas: [] };
    req.flush(sinLineas, { headers: { ETag: '"a2"' } });

    await promise;
    expect(service.asiento()).toEqual(sinLineas);
  });
});
