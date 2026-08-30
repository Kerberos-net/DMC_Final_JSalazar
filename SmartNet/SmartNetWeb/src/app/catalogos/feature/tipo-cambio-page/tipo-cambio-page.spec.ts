import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { TipoCambioPage } from './tipo-cambio-page';
import { DescargaXlsx } from '../../data-access/descarga-xlsx';
import { TipoCambioRespuesta } from '../../models/tipo-cambio.model';

/**
 * tasks.md 8.2 (RED first) -- container for the tipo de cambio screen (spa spec req 1,4,5). The
 * date range defaults to the first day of the current month .. today in LOCAL time (never
 * `toISOString`, which is UTC and can shift the day at the boundaries -- `shared/formato.ts`).
 * Range changes re-query; column sort is client-side. A 400 shows a non-blocking message and no
 * stale rows. Query-only: no create/edit/delete/save control.
 */
describe('TipoCambioPage', () => {
  let http: HttpTestingController;

  const respuesta: TipoCambioRespuesta = {
    items: [
      { fecha: '2026-08-02', origen: 'SBS', compra: 3.76, venta: 3.79, fechaConsulta: '2026-08-02T10:00:00' },
      { fecha: '2026-08-01', origen: 'SBS', compra: 3.75, venta: 3.78, fechaConsulta: '2026-08-01T10:00:00' },
      { fecha: '2026-08-01', origen: 'MANUAL', compra: 3.74, venta: 3.8, fechaConsulta: '2026-08-01T09:00:00' },
    ],
  };

  const tick = () => new Promise((r) => setTimeout(r, 0));

  const local = (d: Date) =>
    `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TipoCambioPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function montar() {
    const fixture = TestBed.createComponent(TipoCambioPage);
    fixture.detectChanges();
    const req = http.expectOne((r) => r.url === '/api/tipos-cambio');
    req.flush(respuesta);
    await tick();
    fixture.detectChanges();
    return { fixture, req };
  }

  const filas = (fixture: { nativeElement: HTMLElement }) =>
    Array.from(fixture.nativeElement.querySelectorAll('tbody tr')).map((tr) =>
      Array.from(tr.querySelectorAll('td')).map((td) => td.textContent?.trim())
    );

  it('defaults the range to first-of-month .. today in LOCAL time', async () => {
    const { req } = await montar();
    const hoy = new Date();
    const primero = new Date(hoy.getFullYear(), hoy.getMonth(), 1);
    expect(req.request.params.get('desde')).toBe(local(primero));
    expect(req.request.params.get('hasta')).toBe(local(hoy));
  });

  it('renders fecha/origen/compra/venta for both origins', async () => {
    const { fixture } = await montar();
    expect(filas(fixture)).toEqual([
      ['2026-08-02', 'SBS', '3.76', '3.79'],
      ['2026-08-01', 'SBS', '3.75', '3.78'],
      ['2026-08-01', 'MANUAL', '3.74', '3.80'],
    ]);
  });

  it('re-queries when the range changes', async () => {
    const { fixture } = await montar();
    fixture.componentInstance.onDesde('2026-07-01');
    fixture.detectChanges();
    const req = http.expectOne((r) => r.url === '/api/tipos-cambio');
    expect(req.request.params.get('desde')).toBe('2026-07-01');
    req.flush({ items: [] });
    await tick();
  });

  it('sorts client-side on a column header with no new request', async () => {
    const { fixture } = await montar();
    const header = fixture.nativeElement.querySelector('[data-testid="orden-compra"]') as HTMLElement;
    header.click();
    fixture.detectChanges();
    expect(filas(fixture).map((f) => f[2])).toEqual(['3.74', '3.75', '3.76']);
    header.click();
    fixture.detectChanges();
    expect(filas(fixture).map((f) => f[2])).toEqual(['3.76', '3.75', '3.74']);
    http.expectNone((r) => r.url === '/api/tipos-cambio');
  });

  it('shows a non-blocking validation message and no stale rows on a 400', async () => {
    const { fixture } = await montar();
    fixture.componentInstance.onHasta('2020-01-01');
    fixture.detectChanges();
    http.expectOne((r) => r.url === '/api/tipos-cambio').flush(null, {
      status: 400,
      statusText: 'Bad Request',
    });
    await tick();
    fixture.detectChanges();

    const banner = fixture.nativeElement.querySelector('[data-testid="tc-error"]');
    expect(banner?.getAttribute('role')).toBe('alert');
    expect(fixture.nativeElement.querySelectorAll('tbody tr').length).toBe(0);
  });

  it('exports the current range via descarga-xlsx', async () => {
    const descarga = TestBed.inject(DescargaXlsx);
    const spy = vi.spyOn(descarga, 'descargar').mockResolvedValue(undefined);
    const { fixture } = await montar();

    fixture.componentInstance.onDesde('2026-06-01');
    fixture.detectChanges();
    http.expectOne((r) => r.url === '/api/tipos-cambio').flush({ items: [] });
    await tick();

    (fixture.nativeElement.querySelector('[data-testid="boton-exportar"]') as HTMLButtonElement).click();
    expect(spy).toHaveBeenCalledWith('/api/tipos-cambio/exportacion', {
      desde: '2026-06-01',
      hasta: fixture.componentInstance['hasta'](),
    });
  });

  it('never renders a create/edit/delete/save control', async () => {
    const { fixture } = await montar();
    expect(
      fixture.nativeElement.querySelectorAll(
        '[data-testid="crear"], [data-testid="editar"], [data-testid="eliminar"], [data-testid="guardar"]'
      ).length
    ).toBe(0);
  });
});
