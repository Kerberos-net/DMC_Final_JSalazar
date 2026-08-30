import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ProveedoresPage } from './proveedores-page';
import { CatalogoProveedorService } from '../../data-access/catalogo-proveedor.service';
import { DescargaXlsx } from '../../data-access/descarga-xlsx';
import { PaginaProveedores } from '../../models/proveedor-catalogo.model';

/**
 * tasks.md 6.2 (RED first) -- container for the proveedores catalogo screen (spa spec req 2). Every
 * narrowing is server-side: a column header click, a search keystroke, a page step and a
 * rows-per-page change each re-query `GET /api/catalogos/proveedores?modo=catalogo`. Sort and search
 * reset to page 1; search keeps the active sort. "Exportar a Excel" delegates to `descarga-xlsx`
 * with the current search + sort. Query-only: no create/edit/delete/save control.
 */
describe('ProveedoresPage', () => {
  let http: HttpTestingController;

  const respuesta: PaginaProveedores = {
    items: [
      { codigo: 'P00000', nombre: 'Proveedor por identificar', ruc: null },
      { codigo: 'P00012', nombre: 'ACME SAC', ruc: '20123456789' },
    ],
    pagina: 1,
    tamanioPagina: 20,
    totalRegistros: 40,
    totalPaginas: 2,
  };

  const tick = () => new Promise((r) => setTimeout(r, 0));

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ProveedoresPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
    TestBed.inject(CatalogoProveedorService).debounceMs = 0;
  });

  afterEach(() => http.verify());

  async function montar(over: Partial<PaginaProveedores> = {}) {
    const fixture = TestBed.createComponent(ProveedoresPage);
    fixture.detectChanges();
    await tick();
    http.expectOne((r) => r.url === '/api/catalogos/proveedores').flush({ ...respuesta, ...over });
    await tick();
    fixture.detectChanges();
    return fixture;
  }

  const filas = (fixture: Awaited<ReturnType<typeof montar>>) =>
    Array.from(fixture.nativeElement.querySelectorAll('tbody tr')).map((tr: any) =>
      tr.querySelector('td')?.textContent.trim()
    );

  it('lists the current page including P00000 and titles the screen', async () => {
    const fixture = await montar();
    expect(filas(fixture)).toEqual(['P00000', 'P00012']);
    expect(fixture.nativeElement.querySelector('h1')?.textContent?.trim()).toBe('Proveedores');
    expect(
      fixture.nativeElement.querySelector('[data-testid="pag-estado"]')?.textContent?.trim()
    ).toBe('Página 1 de 2');
  });

  it('re-queries the server when a column header is activated (server sort, not a client sort)', async () => {
    const fixture = await montar();
    expect(filas(fixture)).toEqual(['P00000', 'P00012']);

    (fixture.nativeElement.querySelector('[data-testid="orden-codigo"]') as HTMLElement).click();
    fixture.detectChanges();
    await tick();

    const req = http.expectOne((r) => r.url === '/api/catalogos/proveedores');
    expect(req.request.params.get('orden')).toBe('codigo');
    expect(req.request.params.get('direccion')).toBe('asc');
    expect(req.request.params.get('pagina')).toBe('1');
    // the server decides the order; the component must NOT reorder locally
    req.flush({
      ...respuesta,
      items: [
        { codigo: 'P00012', nombre: 'ACME SAC', ruc: '20123456789' },
        { codigo: 'P00000', nombre: 'Proveedor por identificar', ruc: null },
      ],
    });
    await tick();
    fixture.detectChanges();
    expect(filas(fixture)).toEqual(['P00012', 'P00000']);
  });

  it('sends q and keeps the active sort, resetting to page 1, on search', async () => {
    const fixture = await montar();

    (fixture.nativeElement.querySelector('[data-testid="orden-ruc"]') as HTMLElement).click();
    fixture.detectChanges();
    await tick();
    http.expectOne((r) => r.url === '/api/catalogos/proveedores').flush(respuesta);
    await tick();

    fixture.componentInstance.onFiltro('acme');
    await tick();

    const req = http.expectOne((r) => r.url === '/api/catalogos/proveedores');
    expect(req.request.params.get('q')).toBe('acme');
    expect(req.request.params.get('orden')).toBe('ruc');
    expect(req.request.params.get('pagina')).toBe('1');
    req.flush({ ...respuesta, totalRegistros: 1, totalPaginas: 1 });
    await tick();
  });

  it('re-queries the next page from the footer', async () => {
    const fixture = await montar();

    (fixture.nativeElement.querySelector('[data-testid="pag-siguiente"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await tick();

    const req = http.expectOne((r) => r.url === '/api/catalogos/proveedores');
    expect(req.request.params.get('pagina')).toBe('2');
    req.flush({ ...respuesta, pagina: 2 });
    await tick();
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelector('[data-testid="pag-estado"]')?.textContent?.trim()
    ).toBe('Página 2 de 2');
  });

  it('re-queries once with the new page size, reset to page 1', async () => {
    const fixture = await montar();

    const select = fixture.nativeElement.querySelector('[data-testid="pag-tamanio"]') as HTMLSelectElement;
    select.value = '50';
    select.dispatchEvent(new Event('change'));
    fixture.detectChanges();
    await tick();

    const reqs = http.match((r) => r.url === '/api/catalogos/proveedores');
    expect(reqs.length).toBe(1);
    expect(reqs[0].request.params.get('tamanio')).toBe('50');
    expect(reqs[0].request.params.get('pagina')).toBe('1');
    reqs[0].flush({ ...respuesta, tamanioPagina: 50 });
    await tick();
  });

  it('exports the current search + sort via descarga-xlsx', async () => {
    const descarga = TestBed.inject(DescargaXlsx);
    const spy = vi.spyOn(descarga, 'descargar').mockResolvedValue(undefined);
    const fixture = await montar();

    (fixture.nativeElement.querySelector('[data-testid="orden-ruc"]') as HTMLElement).click();
    fixture.detectChanges();
    await tick();
    http.expectOne((r) => r.url === '/api/catalogos/proveedores').flush(respuesta);
    await tick();

    fixture.componentInstance.onFiltro('acme');
    await tick();
    http.expectOne((r) => r.url === '/api/catalogos/proveedores').flush(respuesta);
    await tick();

    (fixture.nativeElement.querySelector('[data-testid="boton-exportar"]') as HTMLButtonElement).click();

    expect(spy).toHaveBeenCalledWith('/api/catalogos/proveedores/exportacion', {
      q: 'acme',
      orden: 'ruc',
      direccion: 'asc',
    });
  });

  it('surfaces a load error and renders no rows', async () => {
    const fixture = TestBed.createComponent(ProveedoresPage);
    fixture.detectChanges();
    await tick();
    http
      .expectOne((r) => r.url === '/api/catalogos/proveedores')
      .flush(null, { status: 500, statusText: 'Server Error' });
    await tick();
    fixture.detectChanges();

    const banner = fixture.nativeElement.querySelector('[data-testid="proveedores-error"]');
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
