import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { DetallePage } from './detalle-page';
import { FacturaForm } from '../../ui/factura-form/factura-form';
import { PickerProveedor } from '../../ui/picker-proveedor/picker-proveedor';
import { ProveedorService } from '../../../catalogos/data-access/proveedor.service';
import { FacturaRespuesta } from '../../models/factura.model';
import { AsientoRespuesta, FacturaAsientoRespuesta } from '../../models/asiento.model';

describe('DetallePage', () => {
  let httpMock: HttpTestingController;

  const factura: FacturaRespuesta = {
    facturaId: 42,
    estado: 'ABIERTA',
    proveedorCodigo: 'P00123',
    rucProveedor: '20123456789',
    tipoComprobante: 'Factura',
    numero: 'F001-100',
    totalOrig: 118,
    moneda: 'PEN',
    fechaEmision: '2026-08-10',
    motivo: null,
    afectacion: 'Gravada',
    esProveedorGenerico: false,
    posibleDuplicado: false,
    tieneCamposNoExtraidos: false,
    afectacionMixta: false,
    camposNoExtraidos: [],
    glosa: null,
  };

  const asiento: AsientoRespuesta = {
    asientoContableId: 7,
    estado: 'BORRADOR',
    numeroAsiento: null,
    proveedorCodigo: 'P00123',
    fechaContable: '2026-08-10',
    motivoDescripcion: null,
    tipoCambioVenta: null,
    basePEN: 100,
    igvPEN: 18,
    lineas: [
      {
        lineaId: 1,
        orden: 1,
        bloque: 'PRINCIPAL',
        tipo: 'D',
        debe: 118,
        haber: 0,
        cuentaCodigo: '639915',
        cuentaDescripcion: null,
        ctaReflejaCodigo: null,
        ctaPuenteCodigo: null,
      },
    ],
  };

  async function crearPagina(facturaOverride: Partial<FacturaRespuesta> = {}, asientoOverride: Partial<AsientoRespuesta> = {}) {
    const facturaFlush = { ...factura, ...facturaOverride };
    const asientoFlush = { ...asiento, ...asientoOverride };
    await TestBed.configureTestingModule({
      imports: [DetallePage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { paramMap: of({ get: () => '42' }) } },
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(DetallePage);
    fixture.detectChanges();

    httpMock.expectOne('/api/facturas/42').flush(facturaFlush, { headers: { ETag: '"f1"' } });
    httpMock
      .expectOne('/api/facturas/42/asiento')
      .flush({ asientoContableId: 7, asiento: asientoFlush } as FacturaAsientoRespuesta, { headers: { ETag: '"a1"' } });
    httpMock.expectOne('/api/facturas/42/documentos').flush([]);
    httpMock.expectOne('/api/facturas/42/historial').flush([]);
    await Promise.resolve();
    await Promise.resolve();
    fixture.detectChanges();

    return fixture;
  }

  afterEach(() => {
    httpMock.verify();
  });

  it('loads factura, asiento and documentos on init, both loaded before editing', async () => {
    const fixture = await crearPagina();

    expect(fixture.componentInstance.factura()).toEqual(factura);
    expect(fixture.componentInstance.asiento()).toEqual(asiento);
  });

  it('computes cuadre from the loaded asiento líneas', async () => {
    const fixture = await crearPagina();

    expect(fixture.componentInstance.cuadre().debe).toBe(118);
    expect(fixture.componentInstance.cuadre().cuadrado).toBe(false);
  });

  it('on 412 from validar, shows a conflicto-concurrencia problema and offers reload', async () => {
    const fixture = await crearPagina();

    const validarPromise = fixture.componentInstance.validar('2026-08-23');
    const req = httpMock.expectOne((r) => r.url === '/api/facturas/42/validar');
    req.flush(
      { type: 'https://smartnet.local/problemas/precondicion-fallida', title: 't', status: 412, detail: 'd' },
      { status: 412, statusText: 'Precondition Failed' }
    );
    await validarPromise;
    fixture.detectChanges();

    expect(fixture.componentInstance.categoriaProblema()).toBe('conflicto-concurrencia');
  });

  it('on successful validar, clears the problema and reloads factura+asiento', async () => {
    const fixture = await crearPagina();

    const validarPromise = fixture.componentInstance.validar('2026-08-23');
    httpMock.expectOne((r) => r.url === '/api/facturas/42/validar').flush(null);
    await Promise.resolve();
    await Promise.resolve();

    const validada = { ...factura, estado: 'VALIDADA' };
    httpMock.expectOne('/api/facturas/42').flush(validada, { headers: { ETag: '"f2"' } });
    const confirmado = { ...asiento, estado: 'CONFIRMADO' };
    httpMock
      .expectOne('/api/facturas/42/asiento')
      .flush({ asientoContableId: 7, asiento: confirmado }, { headers: { ETag: '"a2"' } });
    httpMock.expectOne('/api/facturas/42/documentos').flush([]);
    httpMock.expectOne('/api/facturas/42/historial').flush([]);
    await validarPromise;
    fixture.detectChanges();

    expect(fixture.componentInstance.problema()).toBeNull();
    expect(fixture.componentInstance.factura()?.estado).toBe('VALIDADA');
  });

  /* tasks.md 4.13 -- fetches historial via `HistorialService` and passes it down. */
  it('loads the historial for the factura on init', async () => {
    const fixture = await crearPagina();

    expect(fixture.componentInstance.historial()).toEqual([]);
  });

  /* tasks.md 4.8 -- forwards `factura-form`'s confirmarAfectacion to FacturaService. */
  it('onConfirmarAfectacion() calls FacturaService.confirmarAfectacion with If-Match', async () => {
    const fixture = await crearPagina();

    const promesa = fixture.componentInstance.onConfirmarAfectacion(true);
    const req = httpMock.expectOne('/api/facturas/42/confirmar-afectacion');
    expect(req.request.headers.get('If-Match')).toBe('"f1"');
    expect(req.request.body).toEqual({ esMixta: true });
    const actualizada = { ...factura, afectacionMixta: true };
    req.flush(actualizada, { headers: { ETag: '"f3"' } });

    await promesa;
    expect(fixture.componentInstance.factura()?.afectacionMixta).toBe(true);
  });

  /* tasks.md 3.3 (RED first), spa-visual-detalle-validacion "Page header with back action, title,
   * estado pill, and top-right actions". */
  describe('page header', () => {
    it('renders back "← Volver", the composed title, an estado pill, and top-right Guardar/Validar', async () => {
      const fixture = await crearPagina();
      const host: HTMLElement = fixture.nativeElement;

      expect(host.querySelector('[data-testid="volver"]')?.textContent).toContain('Volver');
      expect(host.querySelector('[data-testid="detalle-titulo"]')?.textContent?.replace(/\s+/g, ' ').trim())
        .toBe('Factura - F001-100 - P00123');
      expect(host.querySelector('[data-testid="estado-pill"]')?.textContent).toContain('ABIERTA');

      const acciones = host.querySelector('[data-testid="detalle-acciones"]')!;
      expect(acciones.querySelector('[data-testid="guardar-avance"]')).toBeTruthy();
      expect(acciones.querySelector('[data-testid="validar"]')).toBeTruthy();
    });

    it('uses the pendiente chip token for a non-validada estado', async () => {
      const fixture = await crearPagina();
      expect(fixture.nativeElement.querySelector('[data-testid="estado-pill"]').classList).toContain('chip--pendiente');
    });
  });

  /* tasks.md 3.3 -- banners live in the detalle-page container, above the split, NEVER in factura-form. */
  describe('indicator banners placement', () => {
    it('renders the duplicado banner in the page container and not inside factura-form', async () => {
      const fixture = await crearPagina({ posibleDuplicado: true });
      const host: HTMLElement = fixture.nativeElement;

      expect(host.querySelector('app-indicadores-factura [data-testid="indicador-duplicado"]')).toBeTruthy();
      expect(host.querySelector('app-factura-form [data-testid="indicador-duplicado"]')).toBeNull();
      expect(host.querySelector('app-factura-form .alerta--bloqueante')).toBeNull();
    });

    it('renders the P00000 banner above the split', async () => {
      const fixture = await crearPagina({ esProveedorGenerico: true });
      expect(fixture.nativeElement.querySelector('app-indicadores-factura [data-testid="indicador-p00000"]')).toBeTruthy();
    });

    it('renders the TC-faltante banner for a foreign-currency factura with no tipoCambioVenta', async () => {
      const fixture = await crearPagina({ moneda: 'USD' }, { tipoCambioVenta: null });
      expect(fixture.nativeElement.querySelector('app-indicadores-factura [data-testid="indicador-tc-faltante"]')).toBeTruthy();
    });

    it('does not render the TC-faltante banner once a tipoCambioVenta is present', async () => {
      const fixture = await crearPagina({ moneda: 'USD' }, { tipoCambioVenta: 3.755 });
      expect(fixture.nativeElement.querySelector('[data-testid="indicador-tc-faltante"]')).toBeNull();
    });
  });

  /* tasks.md 3.4 (RED first), pantalla-detalle-validacion "Validar is hard-blocked while P00000 or
   * a duplicate is unresolved" -- no ack-checkbox bypass. */
  describe('bloqueosValidar gate', () => {
    it('lists DUPLICADO when posibleDuplicado is true and blocks Validar', async () => {
      const fixture = await crearPagina({ posibleDuplicado: true });
      expect(fixture.componentInstance.bloqueosValidar()).toEqual(['DUPLICADO']);
      expect(fixture.componentInstance.puedeValidar()).toBe(false);
      expect(fixture.nativeElement.querySelector('[data-testid="validar"]').disabled).toBe(true);

      await fixture.componentInstance.validar('2026-08-23');
      httpMock.expectNone((r) => r.url === '/api/facturas/42/validar');
    });

    it('lists PROVEEDOR_GENERICO when esProveedorGenerico is true and blocks Validar', async () => {
      const fixture = await crearPagina({ esProveedorGenerico: true });
      expect(fixture.componentInstance.bloqueosValidar()).toEqual(['PROVEEDOR_GENERICO']);
      expect(fixture.componentInstance.puedeValidar()).toBe(false);

      await fixture.componentInstance.validar('2026-08-23');
      httpMock.expectNone((r) => r.url === '/api/facturas/42/validar');
    });

    it('lists both blockers when both conditions hold', async () => {
      const fixture = await crearPagina({ posibleDuplicado: true, esProveedorGenerico: true });
      expect(fixture.componentInstance.bloqueosValidar()).toEqual(['DUPLICADO', 'PROVEEDOR_GENERICO']);
      expect(fixture.componentInstance.puedeValidar()).toBe(false);
    });

    it('re-enables Validar when neither blocker applies', async () => {
      const fixture = await crearPagina();
      expect(fixture.componentInstance.bloqueosValidar()).toEqual([]);
      expect(fixture.componentInstance.puedeValidar()).toBe(true);
      expect(fixture.nativeElement.querySelector('[data-testid="validar"]').disabled).toBe(false);
    });
  });

  /* tasks.md 8.12 (RED first), spa-picker-proveedor "Opened from factura-form, selection flows
   * through borradorFactura". */
  describe('proveedor picker wiring', () => {
    it('opens the picker dialog when factura-form emits buscarProveedor', async () => {
      const fixture = await crearPagina();
      TestBed.inject(ProveedorService).debounceMs = 5;

      const form = fixture.debugElement.query(By.directive(FacturaForm)).componentInstance as FacturaForm;
      form.buscarProveedor.emit();
      fixture.detectChanges();

      const dialogo: HTMLDialogElement = fixture.nativeElement.querySelector('[data-testid="picker-proveedor"]');
      expect(dialogo.open).toBe(true);
    });

    it('pushes { proveedorCodigo, rucProveedor } into borradorFactura via onCambiosFactura, no PATCH', async () => {
      const fixture = await crearPagina();
      const picker = fixture.debugElement.query(By.directive(PickerProveedor)).componentInstance as PickerProveedor;

      picker.seleccionar.emit({ codigo: 'P00999', ruc: '20999999999' });
      fixture.detectChanges();

      expect(fixture.componentInstance.borradorFactura()).toEqual({
        proveedorCodigo: 'P00999',
        rucProveedor: '20999999999',
      });
      httpMock.expectNone((r) => r.method === 'PATCH');
    });

    it('persists the picked proveedor only on "Guardar avance"', async () => {
      const fixture = await crearPagina();
      const picker = fixture.debugElement.query(By.directive(PickerProveedor)).componentInstance as PickerProveedor;
      picker.seleccionar.emit({ codigo: 'P00999', ruc: null });

      const guardar = fixture.componentInstance.guardarAvance();
      const req = httpMock.expectOne('/api/facturas/42');
      expect(req.request.method).toBe('PATCH');
      expect(req.request.body).toEqual({ proveedorCodigo: 'P00999' });
      req.flush({ ...factura, proveedorCodigo: 'P00999' }, { headers: { ETag: '"f9"' } });
      await Promise.resolve();
      await Promise.resolve();
      // design D5: guardarAvance refetches everything after the PATCH
      httpMock.expectOne('/api/facturas/42').flush({ ...factura, proveedorCodigo: 'P00999' }, { headers: { ETag: '"f9"' } });
      httpMock
        .expectOne('/api/facturas/42/asiento')
        .flush({ asientoContableId: 7, asiento } as FacturaAsientoRespuesta, { headers: { ETag: '"a1"' } });
      httpMock.expectOne('/api/facturas/42/documentos').flush([]);
      httpMock.expectOne('/api/facturas/42/historial').flush([]);
      await guardar;
    });
  });

  /* tasks.md 4.5 / 4.6 (RED first), pantalla-detalle-validacion "Guardar avance ... refetch". */
  describe('guardar avance refetch (design D5)', () => {
    it('refetches the factura after the PATCH so a server-recomputed posibleDuplicado clears without reload', async () => {
      const fixture = await crearPagina({ posibleDuplicado: true });
      expect(fixture.componentInstance.puedeValidar()).toBe(false);

      fixture.componentInstance.onCambiosFactura({ numero: 'F001-999' });
      const guardar = fixture.componentInstance.guardarAvance();

      const patch = httpMock.expectOne('/api/facturas/42');
      expect(patch.request.method).toBe('PATCH');
      expect(patch.request.body).toEqual({ numero: 'F001-999' });
      patch.flush({ ...factura, numero: 'F001-999', posibleDuplicado: true }, { headers: { ETag: '"f2"' } });
      await Promise.resolve();
      await Promise.resolve();

      httpMock
        .expectOne('/api/facturas/42')
        .flush({ ...factura, numero: 'F001-999', posibleDuplicado: false }, { headers: { ETag: '"f3"' } });
      httpMock
        .expectOne('/api/facturas/42/asiento')
        .flush({ asientoContableId: 7, asiento } as FacturaAsientoRespuesta, { headers: { ETag: '"a2"' } });
      httpMock.expectOne('/api/facturas/42/documentos').flush([]);
      httpMock.expectOne('/api/facturas/42/historial').flush([]);
      await guardar;
      fixture.detectChanges();

      expect(fixture.componentInstance.factura()?.posibleDuplicado).toBe(false);
      expect(fixture.componentInstance.puedeValidar()).toBe(true);
      expect(fixture.componentInstance.borradorFactura()).toEqual({});
    });

    it('strips totalOrig from the draft when the base/IGV pair is edited (design D1)', async () => {
      const fixture = await crearPagina({ estado: 'PENDIENTE_VALIDACION' });
      fixture.componentInstance.onCambiosFactura({ totalOrig: 500 });
      fixture.componentInstance.onCambiosFactura({ baseImponible: 400, igv: 72 });
      expect(fixture.componentInstance.borradorFactura()).toEqual({ baseImponible: 400, igv: 72 });

      fixture.componentInstance.onCambiosFactura({ totalOrig: 600 });
      expect(fixture.componentInstance.borradorFactura()).toEqual({ totalOrig: 600 });
    });
  });

  /* tasks.md 4.6 (RED first): missing-TC 409 and the newly-live §7 422 are surfaced distinctly
   * from a 412, local edits are kept, and "Guardar avance" still works. */
  describe('validar conflict routing keeps edits (design D5/D6)', () => {
    it('routes a missing-tipo-de-cambio 409 to the negocio bucket, distinct from a 412, keeping the draft', async () => {
      const fixture = await crearPagina({ moneda: 'USD' }, { tipoCambioVenta: null });
      fixture.componentInstance.onCambiosFactura({ glosa: 'pendiente de TC' });

      const validar = fixture.componentInstance.validar('2026-08-23');
      httpMock
        .expectOne((r) => r.url === '/api/facturas/42/validar')
        .flush(
          { type: 'https://smartnet.local/problemas/conflicto', title: 'Falta tipo de cambio', status: 409, detail: 'd' },
          { status: 409, statusText: 'Conflict' }
        );
      await validar;
      fixture.detectChanges();

      expect(fixture.componentInstance.categoriaProblema()).toBe('negocio');
      expect(fixture.componentInstance.borradorFactura()).toEqual({ glosa: 'pendiente de TC' });
    });

    it('routes a §7 "cargos = base imponible" 422 to the invariante bucket, distinct from a 412, keeping the draft', async () => {
      const fixture = await crearPagina({ estado: 'PENDIENTE_VALIDACION' });
      fixture.componentInstance.onCambiosFactura({ baseImponible: 400, igv: 72 });

      const validar = fixture.componentInstance.validar('2026-08-23');
      httpMock
        .expectOne((r) => r.url === '/api/facturas/42/validar')
        .flush(
          { type: 'https://smartnet.local/problemas/invariante-contable', title: '§7', status: 422, detail: 'cargos != base' },
          { status: 422, statusText: 'Unprocessable Entity' }
        );
      await validar;
      fixture.detectChanges();

      expect(fixture.componentInstance.categoriaProblema()).toBe('invariante');
      expect(fixture.componentInstance.borradorFactura()).toEqual({ baseImponible: 400, igv: 72 });
    });
  });
});
