import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { AsientoDetalle } from './asiento-detalle';
import { LineaRegistro } from '../../models/registro-compra.model';

/**
 * BACKLOG #23 (spa spec req 3) — read-only presentation of an asiento's detail lines, ordered by
 * `orden`. No edit / anular / reactivar control. An empty (but loaded) line set shows an explicit
 * "sin lineas contables" message.
 */
const linea = (orden: number, tipo: 'D' | 'H', monto: number): LineaRegistro => ({
  orden,
  bloque: 'PRINCIPAL',
  tipo,
  debe: tipo === 'D' ? monto : 0,
  haber: tipo === 'H' ? monto : 0,
  cuentaCodigo: `40${orden}`,
  cuentaDescripcion: `Cuenta ${orden}`,
});

describe('AsientoDetalle', () => {
  it('lists the lines in orden order with cuenta and debe/haber', () => {
    const fixture = TestBed.createComponent(AsientoDetalle);
    fixture.componentRef.setInput('lineas', [linea(2, 'H', 118), linea(1, 'D', 118)]);
    fixture.detectChanges();

    const filas = Array.from(fixture.nativeElement.querySelectorAll('tbody tr')) as HTMLElement[];
    expect(filas.map((f) => f.querySelector('td')?.textContent?.trim())).toEqual(['1', '2']);
    expect(fixture.nativeElement.textContent).toContain('401');
  });

  it('shows an explicit empty message when the loaded line set is empty', () => {
    const fixture = TestBed.createComponent(AsientoDetalle);
    fixture.componentRef.setInput('lineas', []);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[data-testid="sin-lineas"]')?.textContent).toContain(
      'lineas contables'
    );
  });

  it('renders no mutation control', () => {
    const fixture = TestBed.createComponent(AsientoDetalle);
    fixture.componentRef.setInput('lineas', [linea(1, 'D', 1), linea(2, 'H', 1)]);
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelectorAll('button, input, select, [contenteditable]').length).toBe(0);
  });
});
