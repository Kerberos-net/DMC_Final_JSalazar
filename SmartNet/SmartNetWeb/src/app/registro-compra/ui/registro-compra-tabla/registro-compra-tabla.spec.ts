import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it } from 'vitest';
import { RegistroCompraTabla, esInconsistente } from './registro-compra-tabla';
import { LineaRegistro, RegistroCompraCabecera } from '../../models/registro-compra.model';

/**
 * BACKLOG #23 tasks.md 5.1 (RED first) — the inconsistency badge is a PURE presentation check
 * (spa spec req 4, design D6). It lights ONLY when, rounding to the céntimo (no epsilon —
 * REGLAS.md §6 "no hay tolerancia"):
 *   round(basePEN + igvPEN, 2) !== round(netoPEN, 2)   OR
 *   round(sum(debe), 2)        !== round(sum(haber), 2)
 * ANY null term ⇒ NOT inconsistent (absence is not a mismatch) and the amount renders as an em dash.
 * It NEVER imports or calls domain code.
 */
const base = (over: Partial<RegistroCompraCabecera> = {}): RegistroCompraCabecera => ({
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
  ...over,
});

const linea = (tipo: 'D' | 'H', monto: number, orden = 1): LineaRegistro => ({
  orden,
  bloque: 'PRINCIPAL',
  tipo,
  debe: tipo === 'D' ? monto : 0,
  haber: tipo === 'H' ? monto : 0,
  cuentaCodigo: '000000',
  cuentaDescripcion: 'x',
});

describe('esInconsistente (badge)', () => {
  it('is false for a consistent cabecera + balanced detail', () => {
    expect(esInconsistente(base(), [linea('D', 100), linea('H', 100, 2)])).toBe(false);
  });

  it('is true when round(base + igv) !== round(neto) (cabecera descuadre)', () => {
    expect(esInconsistente(base({ netoPEN: 999 }), null)).toBe(true);
  });

  it('is true when round(sum debe) !== round(sum haber) (detalle descuadre)', () => {
    expect(esInconsistente(base(), [linea('D', 100), linea('H', 18, 2)])).toBe(true);
  });

  it('is false for a boleta / no gravada: igv = 0 and base == neto', () => {
    expect(esInconsistente(base({ basePEN: 100, igvPEN: 0, netoPEN: 100 }), null)).toBe(false);
  });

  it('is exact to the céntimo: 100.00 + 18.00 vs 118.01 lights, vs 118.00 does not', () => {
    expect(esInconsistente(base({ netoPEN: 118.01 }), null)).toBe(true);
    expect(esInconsistente(base({ netoPEN: 118.0 }), null)).toBe(false);
  });

  it('is false when any cabecera term is null (absence is not a mismatch)', () => {
    expect(esInconsistente(base({ basePEN: null }), null)).toBe(false);
    expect(esInconsistente(base({ igvPEN: null }), null)).toBe(false);
    expect(esInconsistente(base({ netoPEN: null }), null)).toBe(false);
  });

  it('is false when the detail lines are not loaded yet (null)', () => {
    expect(esInconsistente(base(), null)).toBe(false);
  });

  it('does not light for a percepción that appears on both sides and cancels', () => {
    // cabecera: neto = base + IGV, percepción excluded (§10.4). detail: percepción D and H cancel.
    const lineas = [
      linea('D', 100, 1), // base
      linea('D', 18, 2), // IGV
      linea('D', 2, 3), // percepción (debe)
      linea('H', 120, 4), // abono al proveedor = base + IGV + percepción
    ];
    // cabecera netoPEN = base + IGV (percepción EXCLUDED, §10.4); detail debe (120) == haber (120).
    expect(esInconsistente(base({ basePEN: 100, igvPEN: 18, netoPEN: 118 }), lineas)).toBe(false);
  });
});

describe('RegistroCompraTabla', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      imports: [RegistroCompraTabla],
      providers: [provideRouter([])],
    })
  );

  it('renders one row per cabecera with the proveedor name (or code) and marks inconsistencies', () => {
    const fixture = TestBed.createComponent(RegistroCompraTabla);
    fixture.componentRef.setInput('filas', [
      base({ asientoContableId: 1 }),
      base({ asientoContableId: 2, proveedorNombre: null, proveedorCodigo: 'P99999', netoPEN: 999 }),
    ]);
    fixture.componentRef.setInput('expandido', null);
    fixture.componentRef.setInput('lineasPorAsiento', new Map());
    fixture.detectChanges();

    const filas = fixture.nativeElement.querySelectorAll('tbody tr[data-testid^="registro-fila-"]');
    expect(filas.length).toBe(2);
    expect(fixture.nativeElement.querySelector('[data-testid="registro-fila-1"]').textContent).toContain('ACME SAC');
    expect(fixture.nativeElement.querySelector('[data-testid="registro-fila-2"]').textContent).toContain('P99999');
    expect(fixture.nativeElement.querySelector('[data-testid="badge-1"]')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="badge-2"]')).not.toBeNull();
  });

  it('renders an em dash for a null amount', () => {
    const fixture = TestBed.createComponent(RegistroCompraTabla);
    fixture.componentRef.setInput('filas', [base({ asientoContableId: 5, basePEN: null })]);
    fixture.componentRef.setInput('expandido', null);
    fixture.componentRef.setInput('lineasPorAsiento', new Map());
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="registro-fila-5"]').textContent).toContain('—');
  });

  it('emits alternar with the asiento id when a row toggle is activated', () => {
    const fixture = TestBed.createComponent(RegistroCompraTabla);
    fixture.componentRef.setInput('filas', [base({ asientoContableId: 9 })]);
    fixture.componentRef.setInput('expandido', null);
    fixture.componentRef.setInput('lineasPorAsiento', new Map());
    let emitido: number | null = null;
    fixture.componentInstance.alternar.subscribe((id: number) => (emitido = id));
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('[data-testid="toggle-9"]') as HTMLButtonElement).click();
    expect(emitido).toBe(9);
  });

  it('never renders an edit / anular / reactivar control', () => {
    const fixture = TestBed.createComponent(RegistroCompraTabla);
    fixture.componentRef.setInput('filas', [base()]);
    fixture.componentRef.setInput('expandido', null);
    fixture.componentRef.setInput('lineasPorAsiento', new Map());
    fixture.detectChanges();

    expect(
      fixture.nativeElement.querySelectorAll(
        '[data-testid="editar"], [data-testid="anular"], [data-testid="reactivar"], [data-testid="guardar"]'
      ).length
    ).toBe(0);
  });
});
