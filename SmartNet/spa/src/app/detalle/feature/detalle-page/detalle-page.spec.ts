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

  async function crearPagina() {
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

    httpMock.expectOne('/api/facturas/42').flush(factura, { headers: { ETag: '"f1"' } });
    httpMock
      .expectOne('/api/facturas/42/asiento')
      .flush({ asientoContableId: 7, asiento } as FacturaAsientoRespuesta, { headers: { ETag: '"a1"' } });
    httpMock.expectOne('/api/facturas/42/documentos').flush([]);
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
    await validarPromise;
    fixture.detectChanges();

    expect(fixture.componentInstance.problema()).toBeNull();
    expect(fixture.componentInstance.factura()?.estado).toBe('VALIDADA');
  });
});
