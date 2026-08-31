import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { RegistroCompraPage } from './registro-compra-page';
import { DescargaXlsx } from '../../../catalogos/data-access/descarga-xlsx';
import { PaginaRegistroCompra } from '../../models/registro-compra.model';
import { mesActual } from '../../../shared/formato';

/**
 * BACKLOG #23 tasks.md 5.2 (RED first) — container for the registro de compra screen (spa spec
 * req 2/5/6). Defaults the period to the current LOCAL month; a period change re-queries and resets
 * to page 1; paging is server-side via `tabla-paginador`; an API 400 shows a non-blocking message;
 * an explicit empty state appears when `totalRegistros === 0`; a loading indicator shows in flight.
 */
describe('RegistroCompraPage', () => {
  let http: HttpTestingController;

  const respuesta: PaginaRegistroCompra = {
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

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RegistroCompraPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function montar(over: Partial<PaginaRegistroCompra> = {}) {
    const fixture = TestBed.createComponent(RegistroCompraPage);
    fixture.detectChanges();
    await tick();
    http.expectOne((r) => r.url === '/api/registro-compra').flush({ ...respuesta, ...over });
    await tick();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current LOCAL month on init', async () => {
    const fixture = TestBed.createComponent(RegistroCompraPage);
    fixture.detectChanges();
    await tick();

    const req = http.expectOne((r) => r.url === '/api/registro-compra');
    expect(req.request.params.get('periodo')).toBe(mesActual());
    req.flush(respuesta);
    await tick();
  });

  it('re-queries and resets to page 1 on a period change', async () => {
    const fixture = await montar();

    fixture.componentInstance.onPeriodo('2026-05');
    await tick();

    const req = http.expectOne((r) => r.url === '/api/registro-compra');
    expect(req.request.params.get('periodo')).toBe('2026-05');
    expect(req.request.params.get('pagina')).toBe('1');
    req.flush({ ...respuesta, totalRegistros: 0, totalPaginas: 0, items: [] });
    await tick();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="registro-vacio"]')).not.toBeNull();
  });

  it('steps to the next server page from the footer', async () => {
    const fixture = await montar();

    (fixture.nativeElement.querySelector('[data-testid="pag-siguiente"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await tick();

    const req = http.expectOne((r) => r.url === '/api/registro-compra');
    expect(req.request.params.get('pagina')).toBe('2');
    req.flush({ ...respuesta, pagina: 2 });
    await tick();
  });

  it('shows a non-blocking message when the API rejects the period with 400', async () => {
    const fixture = TestBed.createComponent(RegistroCompraPage);
    fixture.detectChanges();
    await tick();
    http
      .expectOne((r) => r.url === '/api/registro-compra')
      .flush(null, { status: 400, statusText: 'Bad Request' });
    await tick();
    fixture.detectChanges();

    const banner = fixture.nativeElement.querySelector('[data-testid="registro-error"]');
    expect(banner?.getAttribute('role')).toBe('alert');
    expect(banner?.textContent).toContain('periodo');
  });

  it('shows a loading indicator while the request is in flight', async () => {
    const fixture = TestBed.createComponent(RegistroCompraPage);
    fixture.detectChanges();
    await tick();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="registro-cargando"]')).not.toBeNull();
    http.expectOne((r) => r.url === '/api/registro-compra').flush(respuesta);
    await tick();
  });

  it('exports the current period via descarga-xlsx', async () => {
    const descarga = TestBed.inject(DescargaXlsx);
    const spy = vi.spyOn(descarga, 'descargar').mockResolvedValue(undefined);
    const fixture = await montar();

    (fixture.nativeElement.querySelector('[data-testid="boton-exportar"]') as HTMLButtonElement).click();

    expect(spy).toHaveBeenCalledWith('/api/registro-compra/export', { periodo: mesActual() });
  });

  it('expands a row and fetches its lines once', async () => {
    const fixture = await montar();

    (fixture.nativeElement.querySelector('[data-testid="toggle-1"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    await tick();

    const req = http.expectOne('/api/registro-compra/1');
    req.flush({
      cabecera: respuesta.items[0],
      lineas: [
        { orden: 1, bloque: 'PRINCIPAL', tipo: 'D', debe: 118, haber: 0, cuentaCodigo: '60', cuentaDescripcion: 'x' },
        { orden: 2, bloque: 'PRINCIPAL', tipo: 'H', debe: 0, haber: 118, cuentaCodigo: '42', cuentaDescripcion: 'y' },
      ],
    });
    await tick();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="detalle-fila-1"]')).not.toBeNull();
  });

  it('never renders a create / edit / delete / save control', async () => {
    const fixture = await montar();
    expect(
      fixture.nativeElement.querySelectorAll(
        '[data-testid="crear"], [data-testid="editar"], [data-testid="eliminar"], [data-testid="guardar"], [data-testid="anular"]'
      ).length
    ).toBe(0);
  });
});
