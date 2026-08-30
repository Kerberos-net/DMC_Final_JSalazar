import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { TipoCambioTabla } from './tipo-cambio-tabla';
import { TipoCambioHistorico } from '../../models/tipo-cambio.model';
import { EstadoOrden } from '../orden';

/**
 * tasks.md 8.4 (RED first, design D8) -- presentational table for the tipo de cambio screen:
 * columns fecha / origen / compra / venta, both origins per date, no origin selector. Sortable
 * headers emit the intent only; the container owns the client-side sort signal and feeds back the
 * ordered rows. Read-only -- no row action of any kind. Amounts render with exactly 2 decimals.
 */
describe('TipoCambioTabla', () => {
  const filas: TipoCambioHistorico[] = [
    { fecha: '2026-08-01', origen: 'SBS', compra: 3.75, venta: 3.78, fechaConsulta: '2026-08-01T10:00:00' },
    { fecha: '2026-08-01', origen: 'MANUAL', compra: 3.74, venta: 3.8, fechaConsulta: '2026-08-01T09:00:00' },
  ];

  function crear(orden: EstadoOrden<'fecha' | 'origen' | 'compra' | 'venta'> | null = null) {
    const fixture = TestBed.createComponent(TipoCambioTabla);
    fixture.componentRef.setInput('filas', filas);
    fixture.componentRef.setInput('orden', orden);
    fixture.detectChanges();
    return fixture;
  }

  it('renders a row per (fecha, origen) with 2-decimal amounts', () => {
    const root: HTMLElement = crear().nativeElement;
    const celdas = Array.from(root.querySelectorAll('tbody tr'))
      .map((tr) => Array.from(tr.querySelectorAll('td')).map((td) => td.textContent?.trim()));
    expect(celdas).toEqual([
      ['2026-08-01', 'SBS', '3.75', '3.78'],
      ['2026-08-01', 'MANUAL', '3.74', '3.80'],
    ]);
  });

  it('renders no origin selector', () => {
    const root: HTMLElement = crear().nativeElement;
    expect(root.querySelector('select')).toBeNull();
  });

  it('emits ordenar with the column key when a sortable header is activated', () => {
    const fixture = crear();
    const claves: string[] = [];
    fixture.componentInstance.ordenar.subscribe((c: string) => claves.push(c));

    (fixture.nativeElement.querySelector('[data-testid="orden-fecha"]') as HTMLElement).click();
    (fixture.nativeElement.querySelector('[data-testid="orden-venta"]') as HTMLElement).click();

    expect(claves).toEqual(['fecha', 'venta']);
  });

  it('marks the active sort column with a direction arrow', () => {
    const root: HTMLElement = crear({ campo: 'compra', direccion: 'desc' }).nativeElement;
    expect(
      root.querySelector('[data-testid="orden-compra"] .tabla-catalogo__flecha')?.textContent
    ).toBe('▼');
  });

  it('never renders a create/edit/delete/save control', () => {
    const root: HTMLElement = crear().nativeElement;
    expect(
      root.querySelectorAll(
        '[data-testid="crear"], [data-testid="editar"], [data-testid="eliminar"], [data-testid="guardar"]'
      ).length
    ).toBe(0);
  });
});
