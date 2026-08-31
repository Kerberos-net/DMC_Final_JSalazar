import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { RegistroCompraDetalleService } from './registro-compra-detalle.service';
import { RegistroCompraDetalle } from '../models/registro-compra.model';

/**
 * BACKLOG #23 tasks.md 4.4 (RED first) — per-`asientoId` lazy fetch of an asiento's lines
 * (`GET /api/registro-compra/{asientoId}`), memoised so re-expanding a row issues NO second request.
 * The cache is dropped whenever the listing's period or page changes (the container calls
 * `limpiar()`), because a stale asiento may no longer be in view.
 */
describe('RegistroCompraDetalleService', () => {
  let servicio: RegistroCompraDetalleService;
  let http: HttpTestingController;

  const detalle = (id: number): RegistroCompraDetalle => ({
    cabecera: {
      asientoContableId: id,
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
    lineas: [
      { orden: 1, bloque: 'PRINCIPAL', tipo: 'D', debe: 100, haber: 0, cuentaCodigo: '639915', cuentaDescripcion: 'Otros' },
      { orden: 2, bloque: 'PRINCIPAL', tipo: 'H', debe: 0, haber: 100, cuentaCodigo: '421001', cuentaDescripcion: 'CxP' },
    ],
  });

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    servicio = TestBed.inject(RegistroCompraDetalleService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('fetches an asiento detail by id and returns its lines', async () => {
    const promesa = servicio.obtener(7);
    const req = http.expectOne('/api/registro-compra/7');
    expect(req.request.method).toBe('GET');
    req.flush(detalle(7));

    const lineas = await promesa;
    expect(lineas.map((l) => l.orden)).toEqual([1, 2]);
  });

  it('memoises: re-expanding the same asiento issues no second request', async () => {
    const p1 = servicio.obtener(7);
    http.expectOne('/api/registro-compra/7').flush(detalle(7));
    await p1;

    const p2 = servicio.obtener(7);
    http.expectNone('/api/registro-compra/7');
    expect((await p2).length).toBe(2);
  });

  it('drops the cache on limpiar(), so the next expand re-fetches', async () => {
    const p1 = servicio.obtener(7);
    http.expectOne('/api/registro-compra/7').flush(detalle(7));
    await p1;

    servicio.limpiar();

    const p2 = servicio.obtener(7);
    http.expectOne('/api/registro-compra/7').flush(detalle(7));
    expect((await p2).length).toBe(2);
  });
});
