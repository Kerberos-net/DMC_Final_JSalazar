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
});
