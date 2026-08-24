import { TestBed } from '@angular/core/testing';
import { FacturaForm } from './factura-form';
import { FacturaRespuesta } from '../../models/factura.model';

describe('FacturaForm', () => {
  const factura: FacturaRespuesta = {
    facturaId: 42,
    estado: 'ABIERTA',
    proveedorCodigo: 'P00123',
    rucProveedor: '20123456789',
    tipoComprobante: 'Factura',
    numero: 'F001-100',
    totalOrig: 118,
    moneda: 'USD',
    fechaEmision: '2026-08-10',
    motivo: null,
    afectacion: 'Gravada',
    esProveedorGenerico: false,
    posibleDuplicado: false,
    tieneCamposNoExtraidos: false,
    afectacionMixta: false,
  };

  const createComponent = (f: FacturaRespuesta, tipoCambioVenta: number | null = null, editable = true) => {
    const fixture = TestBed.createComponent(FacturaForm);
    fixture.componentRef.setInput('factura', f);
    fixture.componentRef.setInput('tipoCambioVenta', tipoCambioVenta);
    fixture.componentRef.setInput('editable', editable);
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [FacturaForm] }).compileComponents();
  });

  it('shows the factura header fields', () => {
    const fixture = createComponent(factura);
    const proveedorInput: HTMLInputElement = fixture.nativeElement.querySelector(
      '[data-testid="campo-proveedorCodigo"]'
    );
    expect(proveedorInput.value).toBe('P00123');
    expect(fixture.nativeElement.textContent).toContain('F001-100');
  });

  it('shows TipoCambioVenta when present (foreign currency)', () => {
    const fixture = createComponent(factura, 3.755);
    expect(fixture.nativeElement.textContent).toContain('3.755');
  });

  it('does not show a TipoCambioVenta row for a PEN factura (null)', () => {
    const fixture = createComponent(factura, null);
    expect(fixture.nativeElement.querySelector('[data-testid="tipo-cambio-venta"]')).toBeNull();
  });

  it('emits cambios with only the edited field when an editable input changes', () => {
    const fixture = createComponent(factura);
    let emitido: unknown = null;
    fixture.componentInstance.cambios.subscribe((c) => (emitido = c));

    const input: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="campo-rucProveedor"]');
    input.value = '20999999999';
    input.dispatchEvent(new Event('input'));

    expect(emitido).toEqual({ rucProveedor: '20999999999' });
  });

  it('disables editable inputs when editable=false (e.g. CONFIRMADO asiento)', () => {
    const fixture = createComponent(factura, null, false);
    const input: HTMLInputElement = fixture.nativeElement.querySelector('[data-testid="campo-rucProveedor"]');
    expect(input.disabled).toBe(true);
  });

  /* tasks.md 4.6 (RED first), spa-visual-detalle-validacion: bloqueante iff
   * posibleDuplicado || esProveedorGenerico; informativa iff tieneCamposNoExtraidos ||
   * afectacionMixta === null. */
  describe('indicator alert bindings', () => {
    it('renders .alerta--bloqueante for a duplicate invoice', () => {
      const fixture = createComponent({ ...factura, posibleDuplicado: true });
      expect(fixture.nativeElement.querySelector('.alerta--bloqueante')).toBeTruthy();
    });

    it('renders .alerta--bloqueante for an unregistered provider (P00000)', () => {
      const fixture = createComponent({ ...factura, esProveedorGenerico: true });
      expect(fixture.nativeElement.querySelector('.alerta--bloqueante')).toBeTruthy();
    });

    it('renders .alerta--informativa for OCR fields not extracted', () => {
      const fixture = createComponent({ ...factura, tieneCamposNoExtraidos: true });
      expect(fixture.nativeElement.querySelector('.alerta--informativa')).toBeTruthy();
    });

    it('renders .alerta--informativa for an unverified afectación (afectacionMixta === null)', () => {
      const fixture = createComponent({ ...factura, afectacionMixta: null });
      expect(fixture.nativeElement.querySelector('.alerta--informativa')).toBeTruthy();
    });

    it('renders neither alert treatment when no indicator is active', () => {
      const fixture = createComponent(factura);
      expect(fixture.nativeElement.querySelector('.alerta--bloqueante')).toBeNull();
      expect(fixture.nativeElement.querySelector('.alerta--informativa')).toBeNull();
    });
  });

  /* tasks.md 4.8 (RED+GREEN): afectación-confirmation control visible iff
   * AfectacionMixta === null; confirming emits `confirmarAfectacion`. */
  describe('afectación confirmation control', () => {
    it('is visible when afectacionMixta is null (unverified)', () => {
      const fixture = createComponent({ ...factura, afectacionMixta: null });
      expect(fixture.nativeElement.querySelector('[data-testid="confirmar-afectacion"]')).toBeTruthy();
    });

    it('is not rendered once afectacionMixta has a value', () => {
      const fixture = createComponent({ ...factura, afectacionMixta: true });
      expect(fixture.nativeElement.querySelector('[data-testid="confirmar-afectacion"]')).toBeNull();
    });

    it('emits confirmarAfectacion with the chosen esMixta value', () => {
      const fixture = createComponent({ ...factura, afectacionMixta: null });
      let emitido: unknown = 'sin-emitir';
      fixture.componentInstance.confirmarAfectacion.subscribe((v) => (emitido = v));

      (fixture.nativeElement.querySelector('[data-testid="confirmar-afectacion-mixta"]') as HTMLButtonElement).click();

      expect(emitido).toBe(true);
    });
  });
});
