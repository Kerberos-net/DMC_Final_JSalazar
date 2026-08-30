import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { CatalogoProveedorService } from './catalogo-proveedor.service';
import { ProveedorService } from './proveedor.service';
import { PaginaProveedores } from '../models/proveedor-catalogo.model';

/**
 * tasks.md 6.1 (RED first) -- server state for the proveedores catalogo screen (spa spec req 2,
 * design D6/D7). Unlike the plan contable screen, filtering, sorting and paging are ALL server-side:
 * every request-signal change (`q`, `orden`, `direccion`, `pagina`, `tamanio`) re-queries
 * `GET /api/catalogos/proveedores?modo=catalogo`. The search box is debounced, mirroring the picker
 * `ProveedorService`. This is a NEW service: it must never write the picker singleton's signals.
 */
describe('CatalogoProveedorService', () => {
  let servicio: CatalogoProveedorService;
  let http: HttpTestingController;

  const pagina1: PaginaProveedores = {
    items: [
      { codigo: 'P00000', nombre: 'Proveedor por identificar', ruc: null },
      { codigo: 'P00012', nombre: 'ACME SAC', ruc: '20123456789' },
    ],
    pagina: 1,
    tamanioPagina: 20,
    totalRegistros: 42,
    totalPaginas: 3,
  };

  const tick = () => new Promise((r) => setTimeout(r, 0));
  const pedidos = () =>
    http.match((r) => r.url === '/api/catalogos/proveedores');

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    servicio = TestBed.inject(CatalogoProveedorService);
    servicio.debounceMs = 0;
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('requests catalogo mode with every paging + sort param and exposes the PaginaBandeja fields', async () => {
    servicio.cargar();
    await tick();

    const req = http.expectOne((r) => r.url === '/api/catalogos/proveedores');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('modo')).toBe('catalogo');
    expect(req.request.params.get('pagina')).toBe('1');
    expect(req.request.params.get('orden')).toBe('proveedor');
    expect(req.request.params.get('direccion')).toBe('asc');
    expect(req.request.params.get('tamanio')).toBe('20');
    req.flush(pagina1);
    await tick();

    expect(servicio.items().map((p) => p.codigo)).toEqual(['P00000', 'P00012']);
    expect(servicio.pagina()).toBe(1);
    expect(servicio.totalPaginas()).toBe(3);
    expect(servicio.totalRegistros()).toBe(42);
    expect(servicio.tamanioPagina()).toBe(20);
  });

  it('debounces the search box, sends q and resets to page 1', async () => {
    servicio.cargar();
    await tick();
    http.expectOne((r) => r.url === '/api/catalogos/proveedores').flush(pagina1);
    await tick();

    servicio.irAPagina(3);
    await tick();
    http.expectOne((r) => r.params.get('pagina') === '3').flush({ ...pagina1, pagina: 3 });
    await tick();

    servicio.buscar('  acme ');
    await tick();
    const req = http.expectOne((r) => r.url === '/api/catalogos/proveedores');
    expect(req.request.params.get('q')).toBe('acme');
    expect(req.request.params.get('pagina')).toBe('1');
    req.flush({ ...pagina1, totalRegistros: 1, totalPaginas: 1 });
    await tick();
    expect(servicio.totalRegistros()).toBe(1);
  });

  it('re-queries on a sort change, keeps it, and resets to page 1', async () => {
    servicio.cargar();
    await tick();
    http.expectOne((r) => r.url === '/api/catalogos/proveedores').flush(pagina1);
    await tick();

    servicio.ordenar('ruc', 'desc');
    await tick();
    const req = http.expectOne((r) => r.url === '/api/catalogos/proveedores');
    expect(req.request.params.get('orden')).toBe('ruc');
    expect(req.request.params.get('direccion')).toBe('desc');
    expect(req.request.params.get('pagina')).toBe('1');
    req.flush(pagina1);
    await tick();
  });

  it('re-queries on a page-size change and binds tamanioPagina from the response', async () => {
    servicio.cargar();
    await tick();
    http.expectOne((r) => r.url === '/api/catalogos/proveedores').flush(pagina1);
    await tick();

    servicio.cambiarTamanio(50);
    await tick();
    const req = http.expectOne((r) => r.url === '/api/catalogos/proveedores');
    expect(req.request.params.get('tamanio')).toBe('50');
    expect(req.request.params.get('pagina')).toBe('1');
    req.flush({ ...pagina1, tamanioPagina: 50, totalPaginas: 1 });
    await tick();
    expect(servicio.tamanioPagina()).toBe(50);
  });

  it('coalesces a size-then-page-1 burst into a single request', async () => {
    servicio.cargar();
    await tick();
    http.expectOne((r) => r.url === '/api/catalogos/proveedores').flush(pagina1);
    await tick();

    servicio.cambiarTamanio(50);
    servicio.irAPagina(1);
    await tick();

    const reqs = pedidos();
    expect(reqs.length).toBe(1);
    reqs.forEach((r) => r.flush({ ...pagina1, tamanioPagina: 50 }));
    await tick();
  });

  it('never writes the picker ProveedorService signals', async () => {
    const picker = TestBed.inject(ProveedorService);
    servicio.cargar();
    await tick();
    http.expectOne((r) => r.url === '/api/catalogos/proveedores').flush(pagina1);
    await tick();

    expect(picker.resultados()).toEqual([]);
    expect(picker.hayMas()).toBe(false);
  });
});
