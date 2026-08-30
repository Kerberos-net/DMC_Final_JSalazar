import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PlanContablePage } from './plan-contable-page';
import { DescargaXlsx } from '../../data-access/descarga-xlsx';
import { PlanContableRespuesta } from '../../models/cuenta-contable.model';

/**
 * tasks.md 4.2 (RED first) -- container for the plan contable screen. It fetches the full plan
 * once, then filters + sorts + paginates entirely client-side (design D7/D8, api spec req 4). The
 * "Exportar a Excel" action delegates to the shared `descarga-xlsx` helper with the current filter
 * term. Query-only: no create/edit/delete/save control (spa spec req 5).
 */
describe('PlanContablePage', () => {
  let http: HttpTestingController;

  const respuesta: PlanContableRespuesta = {
    items: [
      { cuenta: '10', descripcion: 'Efectivo y equivalentes de efectivo', nivel: 1, esHojaImputable: false },
      { cuenta: '101', descripcion: 'Caja', nivel: null, esHojaImputable: true },
      { cuenta: '104', descripcion: 'Cuentas corrientes en instituciones financieras', nivel: null, esHojaImputable: true },
      { cuenta: '42', descripcion: 'Cuentas por pagar comerciales - Terceros', nivel: 1, esHojaImputable: false },
    ],
  };

  const tick = () => new Promise((r) => setTimeout(r, 0));

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PlanContablePage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function montar(over: Partial<PlanContableRespuesta> = {}) {
    const fixture = TestBed.createComponent(PlanContablePage);
    fixture.detectChanges();
    http.expectOne('/api/catalogos/plan-contable').flush({ ...respuesta, ...over });
    await tick();
    fixture.detectChanges();
    return fixture;
  }

  const filas = (fixture: Awaited<ReturnType<typeof montar>>) =>
    Array.from(fixture.nativeElement.querySelectorAll('tbody tr')).map((tr: any) =>
      tr.querySelector('td')?.textContent.trim()
    );

  it('fetches the full plan on init and renders every account', async () => {
    const fixture = await montar();
    expect(filas(fixture)).toEqual(['10', '101', '104', '42']);
    // no server pagination control: the footer is the client-side paginador only
    expect(fixture.nativeElement.querySelector('h1')?.textContent?.trim()).toBe('Plan contable');
  });

  it('filters client-side over codigo or denominacion with no new request', async () => {
    const fixture = await montar();

    fixture.componentInstance.onFiltro('caja');
    fixture.detectChanges();

    expect(filas(fixture)).toEqual(['101']);
    http.expectNone('/api/catalogos/plan-contable');
  });

  it('sorts client-side when a column header is activated, no new request', async () => {
    const fixture = await montar();

    const header = fixture.nativeElement.querySelector(
      '[data-testid="orden-descripcion"]'
    ) as HTMLElement;
    header.click();
    fixture.detectChanges();

    expect(filas(fixture)).toEqual(['101', '104', '42', '10']);
    header.click();
    fixture.detectChanges();
    expect(filas(fixture)).toEqual(['10', '42', '104', '101']);

    http.expectNone('/api/catalogos/plan-contable');
  });

  it('paginates the filtered list client-side', async () => {
    const fixture = await montar();
    fixture.componentInstance.onTamanio(2);
    fixture.detectChanges();

    expect(filas(fixture)).toEqual(['10', '101']);
    expect(
      fixture.nativeElement.querySelector('[data-testid="pag-estado"]')?.textContent?.trim()
    ).toBe('Página 1 de 2');

    fixture.componentInstance.onPagina(2);
    fixture.detectChanges();
    expect(filas(fixture)).toEqual(['104', '42']);
    http.expectNone('/api/catalogos/plan-contable');
  });

  it('resets to page 1 when the filter changes', async () => {
    const fixture = await montar();
    fixture.componentInstance.onTamanio(2);
    fixture.componentInstance.onPagina(2);
    fixture.detectChanges();

    fixture.componentInstance.onFiltro('cuenta');
    fixture.detectChanges();

    expect(
      fixture.nativeElement.querySelector('[data-testid="pag-estado"]')?.textContent?.trim()
    ).toBe('Página 1 de 1');
    expect(filas(fixture)).toEqual(['104', '42']);
  });

  it('exports the current filtered set via descarga-xlsx', async () => {
    const descarga = TestBed.inject(DescargaXlsx);
    const spy = vi.spyOn(descarga, 'descargar').mockResolvedValue(undefined);
    const fixture = await montar();

    fixture.componentInstance.onFiltro('caja');
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('[data-testid="boton-exportar"]') as HTMLButtonElement).click();

    expect(spy).toHaveBeenCalledWith('/api/catalogos/plan-contable/exportacion', { q: 'caja' });
  });

  it('surfaces a load error and renders no rows', async () => {
    const fixture = TestBed.createComponent(PlanContablePage);
    fixture.detectChanges();
    http.expectOne('/api/catalogos/plan-contable').flush(null, { status: 500, statusText: 'Server Error' });
    await tick();
    fixture.detectChanges();

    const banner = fixture.nativeElement.querySelector('[data-testid="plan-error"]');
    expect(banner?.getAttribute('role')).toBe('alert');
    expect(fixture.nativeElement.querySelectorAll('tbody tr').length).toBe(0);
  });

  it('never renders a create/edit/delete/save control', async () => {
    const fixture = await montar();
    const controles = fixture.nativeElement.querySelectorAll(
      '[data-testid="crear"], [data-testid="editar"], [data-testid="eliminar"], [data-testid="guardar"]'
    );
    expect(controles.length).toBe(0);
  });
});
