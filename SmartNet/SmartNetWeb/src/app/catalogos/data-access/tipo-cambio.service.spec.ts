import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TipoCambioService } from './tipo-cambio.service';
import { TipoCambioRespuesta } from '../models/tipo-cambio.model';

/**
 * tasks.md 8.1 (RED first) -- the tipo de cambio screen fetches a bounded date range from
 * `GET /api/tipos-cambio?desde=&hasta=` (api spec req 5). Every range change re-queries; sort is
 * client-side (design D8). A 400 (invalid range) surfaces a non-blocking message and clears the
 * list -- stale rows must never be shown as if they matched the new range (spa spec req 4).
 */
describe('TipoCambioService', () => {
  let servicio: TipoCambioService;
  let http: HttpTestingController;

  const respuesta: TipoCambioRespuesta = {
    items: [
      { fecha: '2026-08-01', origen: 'SBS', compra: 3.75, venta: 3.78, fechaConsulta: '2026-08-01T10:00:00' },
      { fecha: '2026-08-01', origen: 'MANUAL', compra: 3.74, venta: 3.79, fechaConsulta: '2026-08-01T09:00:00' },
    ],
  };

  const tick = () => new Promise((r) => setTimeout(r, 0));

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    servicio = TestBed.inject(TipoCambioService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('GETs the range and exposes items as a signal', async () => {
    const promesa = servicio.cargar('2026-08-01', '2026-08-31');
    const req = http.expectOne((r) => r.url === '/api/tipos-cambio');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('desde')).toBe('2026-08-01');
    expect(req.request.params.get('hasta')).toBe('2026-08-31');
    req.flush(respuesta);
    await promesa;

    expect(servicio.items().map((t) => t.origen)).toEqual(['SBS', 'MANUAL']);
    expect(servicio.error()).toBeNull();
  });

  it('re-queries on every range change', async () => {
    const p1 = servicio.cargar('2026-08-01', '2026-08-31');
    http.expectOne((r) => r.url === '/api/tipos-cambio').flush(respuesta);
    await p1;

    const p2 = servicio.cargar('2026-07-01', '2026-07-31');
    const req = http.expectOne((r) => r.url === '/api/tipos-cambio');
    expect(req.request.params.get('desde')).toBe('2026-07-01');
    req.flush({ items: [] });
    await p2;

    expect(servicio.items()).toEqual([]);
  });

  it('toggles cargando around the request', async () => {
    expect(servicio.cargando()).toBe(false);
    const promesa = servicio.cargar('2026-08-01', '2026-08-31');
    expect(servicio.cargando()).toBe(true);
    http.expectOne((r) => r.url === '/api/tipos-cambio').flush(respuesta);
    await promesa;
    expect(servicio.cargando()).toBe(false);
  });

  it('surfaces a validation message and clears items on a 400 (no stale-as-current)', async () => {
    const p1 = servicio.cargar('2026-08-01', '2026-08-31');
    http.expectOne((r) => r.url === '/api/tipos-cambio').flush(respuesta);
    await p1;
    expect(servicio.items().length).toBe(2);

    const p2 = servicio.cargar('2026-08-31', '2026-08-01');
    http.expectOne((r) => r.url === '/api/tipos-cambio').flush(null, {
      status: 400,
      statusText: 'Bad Request',
    });
    await expect(p2).rejects.toBeTruthy();
    await tick();

    expect(servicio.error()).toBe('El rango de fechas no es válido.');
    expect(servicio.items()).toEqual([]);
  });

  it('surfaces a generic message on a non-400 failure', async () => {
    const promesa = servicio.cargar('2026-08-01', '2026-08-31');
    http.expectOne((r) => r.url === '/api/tipos-cambio').flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await expect(promesa).rejects.toBeTruthy();
    await tick();

    expect(servicio.error()).toBe('No se pudo cargar el tipo de cambio.');
    expect(servicio.items()).toEqual([]);
  });
});
