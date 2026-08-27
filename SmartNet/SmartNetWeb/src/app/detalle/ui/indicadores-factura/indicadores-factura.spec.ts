import { TestBed } from '@angular/core/testing';
import { IndicadoresFactura } from './indicadores-factura';

describe('IndicadoresFactura', () => {
  const createComponent = (inputs: {
    posibleDuplicado?: boolean;
    esProveedorGenerico?: boolean;
    tipoCambioFaltante?: boolean;
  }) => {
    const fixture = TestBed.createComponent(IndicadoresFactura);
    fixture.componentRef.setInput('posibleDuplicado', inputs.posibleDuplicado ?? false);
    fixture.componentRef.setInput('esProveedorGenerico', inputs.esProveedorGenerico ?? false);
    fixture.componentRef.setInput('tipoCambioFaltante', inputs.tipoCambioFaltante ?? false);
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [IndicadoresFactura] }).compileComponents();
  });

  it('renders no banner when every condition is false', () => {
    const fixture = createComponent({});
    expect(fixture.nativeElement.querySelector('[data-testid="indicador-duplicado"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="indicador-p00000"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="indicador-tc-faltante"]')).toBeNull();
  });

  it('shows only the duplicado banner (strong amber, role=alert) when posibleDuplicado is true', () => {
    const fixture = createComponent({ posibleDuplicado: true });
    const banner = fixture.nativeElement.querySelector('[data-testid="indicador-duplicado"]');
    expect(banner).toBeTruthy();
    expect(banner.getAttribute('role')).toBe('alert');
    expect(banner.textContent).toContain('duplicada');
    expect(fixture.nativeElement.querySelector('[data-testid="indicador-p00000"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="indicador-tc-faltante"]')).toBeNull();
  });

  it('shows only the P00000 informational banner when esProveedorGenerico is true', () => {
    const fixture = createComponent({ esProveedorGenerico: true });
    const banner = fixture.nativeElement.querySelector('[data-testid="indicador-p00000"]');
    expect(banner).toBeTruthy();
    expect(banner.textContent).toContain('P00000');
    expect(fixture.nativeElement.querySelector('[data-testid="indicador-duplicado"]')).toBeNull();
  });

  it('shows only the TC-faltante banner stating "0.00" when tipoCambioFaltante is true', () => {
    const fixture = createComponent({ tipoCambioFaltante: true });
    const banner = fixture.nativeElement.querySelector('[data-testid="indicador-tc-faltante"]');
    expect(banner).toBeTruthy();
    expect(banner.getAttribute('role')).toBe('alert');
    expect(banner.textContent).toContain('0.00');
  });

  it('shows the three banners at once when every condition is true', () => {
    const fixture = createComponent({
      posibleDuplicado: true,
      esProveedorGenerico: true,
      tipoCambioFaltante: true,
    });
    expect(fixture.nativeElement.querySelectorAll('[data-testid^="indicador-"]').length).toBe(3);
  });
});
