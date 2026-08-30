import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { PlanContableService } from './plan-contable.service';
import { PlanContableRespuesta } from '../models/cuenta-contable.model';

/**
 * tasks.md 4.1 (RED first) -- the plan contable screen fetches the WHOLE plan once from
 * `GET /api/catalogos/plan-contable` (api spec req 4: no server pagination). Filter and column
 * sort are client-side (design D7/D8), so this service issues exactly one request for the life of
 * the screen and never re-queries.
 */
describe('PlanContableService', () => {
  let servicio: PlanContableService;
  let http: HttpTestingController;

  const respuesta: PlanContableRespuesta = {
    items: [
      { cuenta: '10', descripcion: 'Efectivo y equivalentes', nivel: 1, esHojaImputable: false },
      { cuenta: '101', descripcion: 'Caja', nivel: null, esHojaImputable: true },
    ],
  };

  const tick = () => new Promise((r) => setTimeout(r, 0));

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    servicio = TestBed.inject(PlanContableService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('GETs the full plan once and exposes items as a signal', async () => {
    const promesa = servicio.cargar();
    const req = http.expectOne('/api/catalogos/plan-contable');
    expect(req.request.method).toBe('GET');
    req.flush(respuesta);
    await promesa;

    expect(servicio.plan().map((c) => c.cuenta)).toEqual(['10', '101']);
    expect(servicio.plan()[1].esHojaImputable).toBe(true);
    expect(servicio.error()).toBeNull();
  });

  it('does not issue a second request once the plan is loaded', async () => {
    await (() => {
      const p = servicio.cargar();
      http.expectOne('/api/catalogos/plan-contable').flush(respuesta);
      return p;
    })();

    await servicio.cargar();
    http.expectNone('/api/catalogos/plan-contable');
    expect(servicio.plan().length).toBe(2);
  });

  it('toggles cargando around the request', async () => {
    expect(servicio.cargando()).toBe(false);
    const promesa = servicio.cargar();
    expect(servicio.cargando()).toBe(true);
    http.expectOne('/api/catalogos/plan-contable').flush(respuesta);
    await promesa;
    expect(servicio.cargando()).toBe(false);
  });

  it('exposes an error message and stays retryable when the request fails', async () => {
    const promesa = servicio.cargar();
    http.expectOne('/api/catalogos/plan-contable').flush(null, {
      status: 500,
      statusText: 'Server Error',
    });
    await expect(promesa).rejects.toBeTruthy();
    await tick();

    expect(servicio.error()).toBe('No se pudo cargar el plan contable.');
    expect(servicio.plan()).toEqual([]);

    // A prior failure must not latch the "loaded once" guard shut.
    const reintento = servicio.cargar();
    http.expectOne('/api/catalogos/plan-contable').flush(respuesta);
    await reintento;
    expect(servicio.plan().length).toBe(2);
  });
});
