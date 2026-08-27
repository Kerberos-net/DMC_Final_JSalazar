import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute } from '@angular/router';
import { of } from 'rxjs';
import { DetallePage } from './detalle-page';
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
  };

  const asiento: AsientoRespuesta = {
    asientoContableId: 7,
    estado: 'BORRADOR',
    numeroAsiento: null,
    proveedorCodigo: 'P00123',
    fechaContable: '2026-08-10',
    motivoDescripcion: null,
    tipoCambioVenta: null,
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
});
