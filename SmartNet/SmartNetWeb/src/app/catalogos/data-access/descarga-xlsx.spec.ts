import { TestBed } from '@angular/core/testing';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { DescargaXlsx, nombreDesdeContentDisposition } from './descarga-xlsx';

/**
 * tasks.md 3.3 (RED first, design D8) -- shared blob-download helper. `http.get` with
 * `responseType: 'blob', observe: 'response'` (NOT window.open: a 401 there is a blank tab that
 * bypasses httpErrorInterceptor's session-expiry redirect). Reads the server filename from
 * Content-Disposition, anchor-downloads, and always revokes the object URL.
 */
describe('DescargaXlsx', () => {
  let servicio: DescargaXlsx;
  let http: HttpTestingController;
  let createObjectURL: ReturnType<typeof vi.fn>;
  let revokeObjectURL: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    createObjectURL = vi.fn(() => 'blob:mock');
    revokeObjectURL = vi.fn();
    URL.createObjectURL = createObjectURL as unknown as typeof URL.createObjectURL;
    URL.revokeObjectURL = revokeObjectURL as unknown as typeof URL.revokeObjectURL;
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    servicio = TestBed.inject(DescargaXlsx);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('GETs as a blob observing the full response and forwards params', async () => {
    const promesa = servicio.descargar('/api/catalogos/plan-contable/exportacion', { q: 'caja' });
    const req = http.expectOne((r) => r.url === '/api/catalogos/plan-contable/exportacion');
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');
    expect(req.request.params.get('q')).toBe('caja');
    req.flush(new Blob(['x']), {
      headers: { 'Content-Disposition': 'attachment; filename="plan-contable-2026-08-30.xlsx"' },
    });
    await promesa;
    expect(createObjectURL).toHaveBeenCalledOnce();
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:mock');
  });

  it('toggles descargando around the request', async () => {
    expect(servicio.descargando()).toBe(false);
    const promesa = servicio.descargar('/api/x');
    expect(servicio.descargando()).toBe(true);
    http
      .expectOne('/api/x')
      .flush(new Blob(['x']), { headers: { 'Content-Disposition': 'attachment; filename="x.xlsx"' } });
    await promesa;
    expect(servicio.descargando()).toBe(false);
  });

  it('resets descargando even when the request fails (401)', async () => {
    const promesa = servicio.descargar('/api/x');
    http.expectOne('/api/x').flush(null, { status: 401, statusText: 'Unauthorized' });
    await expect(promesa).rejects.toBeTruthy();
    expect(servicio.descargando()).toBe(false);
  });

  it('nombreDesdeContentDisposition parses quoted and RFC 5987 forms', () => {
    expect(
      nombreDesdeContentDisposition('attachment; filename="plan-contable-2026-08-30.xlsx"')
    ).toBe('plan-contable-2026-08-30.xlsx');
    expect(nombreDesdeContentDisposition("attachment; filename*=UTF-8''tipos-cambio.xlsx")).toBe(
      'tipos-cambio.xlsx'
    );
    expect(nombreDesdeContentDisposition(null)).toBe('export.xlsx');
  });
});
