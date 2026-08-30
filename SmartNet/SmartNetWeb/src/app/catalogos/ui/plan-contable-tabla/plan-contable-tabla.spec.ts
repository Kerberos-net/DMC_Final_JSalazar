import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { PlanContableTabla } from './plan-contable-tabla';
import { CuentaContable } from '../../models/cuenta-contable.model';
import { EstadoOrden } from '../orden';

/**
 * tasks.md 4.4 (RED first, design D8) -- presentational table for the plan contable screen:
 * columns codigo / denominacion (denominacion <- API `descripcion`). Sortable headers emit the
 * intent only; the container owns the client-side sort signal and feeds back the ordered rows.
 */
describe('PlanContableTabla', () => {
  const filas: CuentaContable[] = [
    { cuenta: '10', descripcion: 'Efectivo y equivalentes', nivel: 1, esHojaImputable: false },
    { cuenta: '101', descripcion: 'Caja', nivel: null, esHojaImputable: true },
  ];

  function crear(orden: EstadoOrden<'cuenta' | 'descripcion'> = { campo: 'cuenta', direccion: 'asc' }) {
    const fixture = TestBed.createComponent(PlanContableTabla);
    fixture.componentRef.setInput('filas', filas);
    fixture.componentRef.setInput('orden', orden);
    fixture.detectChanges();
    return fixture;
  }

  it('renders a row per cuenta with codigo and denominacion', () => {
    const root: HTMLElement = crear().nativeElement;
    const celdas = Array.from(root.querySelectorAll('[data-testid="plan-fila-101"] td')).map((c) =>
      c.textContent?.trim()
    );
    expect(celdas).toEqual(['101', 'Caja']);
    expect(root.querySelectorAll('tbody tr').length).toBe(2);
  });

  it('emits ordenar with the column key when a sortable header is activated', () => {
    const fixture = crear();
    const claves: string[] = [];
    fixture.componentInstance.ordenar.subscribe((c: string) => claves.push(c));

    (fixture.nativeElement.querySelector('[data-testid="orden-cuenta"]') as HTMLElement).click();
    (fixture.nativeElement.querySelector('[data-testid="orden-descripcion"]') as HTMLElement).click();

    expect(claves).toEqual(['cuenta', 'descripcion']);
  });

  it('marks the active sort column with a direction arrow', () => {
    const root: HTMLElement = crear({ campo: 'descripcion', direccion: 'desc' }).nativeElement;
    expect(root.querySelector('[data-testid="orden-descripcion"] .tabla-catalogo__flecha')?.textContent).toBe(
      '▼'
    );
    expect(
      root.querySelector('[data-testid="orden-cuenta"] .tabla-catalogo__flecha')?.textContent?.trim()
    ).toBe('');
  });
});
