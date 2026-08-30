import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { ProveedoresTabla } from './proveedores-tabla';

/**
 * tasks.md 6.4 (RED first, design D8) -- presentational table for the proveedores catalogo screen.
 * Columns: "Código" (`codigo`), "Razón social" (`nombre`), "RUC" (`ruc`, nullable). Headers emit
 * `ordenar` with the server sort key; the container feeds back rows already sorted by the server.
 * Read-only -- no row action.
 */
describe('ProveedoresTabla', () => {
  function crear(orden: unknown = null) {
    const fixture = TestBed.createComponent(ProveedoresTabla);
    fixture.componentRef.setInput('filas', [
      { codigo: 'P00000', nombre: 'Proveedor por identificar', ruc: null },
      { codigo: 'P00012', nombre: 'ACME SAC', ruc: '20123456789' },
    ]);
    fixture.componentRef.setInput('orden', orden);
    fixture.detectChanges();
    return fixture;
  }

  it('renders codigo, razon social and RUC for every row', () => {
    const fixture = crear();
    const celdas = Array.from(fixture.nativeElement.querySelectorAll('tbody tr')).map((tr: any) =>
      Array.from(tr.querySelectorAll('td')).map((td: any) => td.textContent.trim())
    );
    expect(celdas[0]).toEqual(['P00000', 'Proveedor por identificar', '—']);
    expect(celdas[1]).toEqual(['P00012', 'ACME SAC', '20123456789']);
  });

  it('emits ordenar with the server sort key when a header is activated', () => {
    const fixture = crear();
    const claves: string[] = [];
    fixture.componentInstance.ordenar.subscribe((c: string) => claves.push(c));

    (fixture.nativeElement.querySelector('[data-testid="orden-codigo"]') as HTMLElement).click();
    (fixture.nativeElement.querySelector('[data-testid="orden-proveedor"]') as HTMLElement).click();
    (fixture.nativeElement.querySelector('[data-testid="orden-ruc"]') as HTMLElement).click();

    expect(claves).toEqual(['codigo', 'proveedor', 'ruc']);
  });

  it('marks the active sort column only', () => {
    const fixture = crear({ campo: 'ruc', direccion: 'desc' });
    const th = fixture.nativeElement.querySelector('[data-testid="orden-ruc"]');
    expect(th.textContent).toContain('▼');
    expect(th.getAttribute('aria-sort')).toBe('descending');
    expect(
      fixture.nativeElement.querySelector('[data-testid="orden-codigo"]').getAttribute('aria-sort')
    ).toBe('none');
  });
});
