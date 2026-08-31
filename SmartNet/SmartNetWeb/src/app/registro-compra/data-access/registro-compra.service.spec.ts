import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { RegistroCompraService } from './registro-compra.service';
import { PaginaRegistroCompra } from '../models/registro-compra.model';

/**
 * BACKLOG #23 tasks.md 4.1 (RED first) — server state for the registro de compra screen
 * (spa spec req 2/6/7). Filtering (`periodo`), paging (`pagina`) and rows-per-page (`tamanioPagina`)
 * are ALL server-side: every request-signal change re-queries `GET /api/registro-compra`. A period
 * change resets to page 1. Errors are non-blocking and never leave a stale page shown as current.
 */
describe('RegistroCompraService', () => {
  let servicio: RegistroCompraService;
  let http: HttpTestingController;

  const pagina1: PaginaRegistroCompra = {
    items: [
      {
        asientoContableId: 1,
        numeroComprobante: 'F001-1',
        numeroAsiento: '02-2026-08-000001',
        origenLibro: '02',
        proveedorCodigo: 'P00123',
        proveedorNombre: 'ACME SAC',
        glosa: 'Compra',
        fechaContable: '2026-08-10',
        tipoCambioVenta: null,
        basePEN: 100,
        igvPEN: 18,
        netoPEN: 118,
      },
    ],
    pagina: 1,
    tamanioPagina: 20,
    totalRegistros: 25,
    totalPaginas: 2,
  };

  const tick = () => new Promise((r) => setTimeout(r, 0));
  const pedidos = () => http.match((r) => r.url === '/api/registro-compra');

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    servicio = TestBed.inject(RegistroCompraService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('requests the period with pagina + tamanioPagina and exposes the envelope fields', async () => {
    servicio.cargar('2026-08');
    await tick();

    const req = http.expectOne((r) => r.url === '/api/registro-compra');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('periodo')).toBe('2026-08');
    expect(req.request.params.get('pagina')).toBe('1');
    expect(req.request.params.get('tamanioPagina')).toBe('20');
    req.flush(pagina1);
    await tick();

    expect(servicio.items().map((i) => i.asientoContableId)).toEqual([1]);
    expect(servicio.pagina()).toBe(1);
    expect(servicio.totalPaginas()).toBe(2);
    expect(servicio.totalRegistros()).toBe(25);
    expect(servicio.tamanioPagina()).toBe(20);
  });

  it('toggles cargando around the request', async () => {
    servicio.cargar('2026-08');
    await tick();
    expect(servicio.cargando()).toBe(true);

    http.expectOne((r) => r.url === '/api/registro-compra').flush(pagina1);
    await tick();
    expect(servicio.cargando()).toBe(false);
  });

  it('re-queries and resets to page 1 on a period change', async () => {
    servicio.cargar('2026-08');
    await tick();
    http.expectOne((r) => r.url === '/api/registro-compra').flush(pagina1);
    await tick();

    servicio.irAPagina(2);
    await tick();
    http.expectOne((r) => r.params.get('pagina') === '2').flush({ ...pagina1, pagina: 2 });
    await tick();

    servicio.cambiarPeriodo('2026-07');
    await tick();
    const req = http.expectOne((r) => r.url === '/api/registro-compra');
    expect(req.request.params.get('periodo')).toBe('2026-07');
    expect(req.request.params.get('pagina')).toBe('1');
    req.flush({ ...pagina1, totalRegistros: 0, totalPaginas: 0, items: [] });
    await tick();
    expect(servicio.totalRegistros()).toBe(0);
  });

  it('coalesces a size-then-page-1 burst into a single request', async () => {
    servicio.cargar('2026-08');
    await tick();
    http.expectOne((r) => r.url === '/api/registro-compra').flush(pagina1);
    await tick();

    servicio.cambiarTamanio(50);
    servicio.irAPagina(1);
    await tick();

    const reqs = pedidos();
    expect(reqs.length).toBe(1);
    expect(reqs[0].request.params.get('tamanioPagina')).toBe('50');
    reqs[0].flush({ ...pagina1, tamanioPagina: 50 });
    await tick();
  });

  it('surfaces an error and does not keep the previous page shown as current', async () => {
    servicio.cargar('2026-08');
    await tick();
    http.expectOne((r) => r.url === '/api/registro-compra').flush(pagina1);
    await tick();
    expect(servicio.items().length).toBe(1);

    servicio.cambiarPeriodo('2026-09');
    await tick();
    http
      .expectOne((r) => r.url === '/api/registro-compra')
      .flush(null, { status: 500, statusText: 'Server Error' });
    await tick();

    expect(servicio.error()).not.toBeNull();
    expect(servicio.items()).toEqual([]);
    expect(servicio.cargando()).toBe(false);
  });
});
