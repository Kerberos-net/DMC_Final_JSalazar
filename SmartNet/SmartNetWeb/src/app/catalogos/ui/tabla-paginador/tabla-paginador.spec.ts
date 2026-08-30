import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { TablaPaginador } from './tabla-paginador';

/**
 * tasks.md 3.1 (RED first, design D8) -- source-agnostic pagination chrome driven by the
 * `PaginaBandeja<T>` fields (`pagina`, `totalPaginas`, `tamanioPagina`). It fetches nothing: the
 * container maps its outputs to a server re-query (proveedores) or a client-side slice (plan / TC).
 */
describe('TablaPaginador', () => {
  function crear(pagina: number, totalPaginas: number, tamanio = 10) {
    const fixture = TestBed.createComponent(TablaPaginador);
    fixture.componentRef.setInput('pagina', pagina);
    fixture.componentRef.setInput('totalPaginas', totalPaginas);
    fixture.componentRef.setInput('tamanio', tamanio);
    fixture.detectChanges();
    return fixture;
  }

  const q = (fixture: ReturnType<typeof crear>, id: string): HTMLElement =>
    fixture.nativeElement.querySelector(`[data-testid="${id}"]`);

  it('renders "Página X de Y"', () => {
    expect(q(crear(2, 5), 'pag-estado').textContent?.trim()).toBe('Página 2 de 5');
  });

  it('disables Anterior on page 1', () => {
    const fixture = crear(1, 5);
    expect((q(fixture, 'pag-anterior') as HTMLButtonElement).disabled).toBe(true);
    expect((q(fixture, 'pag-siguiente') as HTMLButtonElement).disabled).toBe(false);
  });

  it('disables Siguiente on the last page', () => {
    const fixture = crear(5, 5);
    expect((q(fixture, 'pag-siguiente') as HTMLButtonElement).disabled).toBe(true);
    expect((q(fixture, 'pag-anterior') as HTMLButtonElement).disabled).toBe(false);
  });

  it('emits paginaChange with the previous / next page', () => {
    const fixture = crear(3, 5);
    const paginas: number[] = [];
    fixture.componentInstance.paginaChange.subscribe((p: number) => paginas.push(p));
    q(fixture, 'pag-anterior').click();
    q(fixture, 'pag-siguiente').click();
    expect(paginas).toEqual([2, 4]);
  });

  it('on rows-per-page change emits tamanioChange and resets to page 1', () => {
    const fixture = crear(4, 9, 10);
    const tamanios: number[] = [];
    const paginas: number[] = [];
    fixture.componentInstance.tamanioChange.subscribe((t: number) => tamanios.push(t));
    fixture.componentInstance.paginaChange.subscribe((p: number) => paginas.push(p));
    const select = q(fixture, 'pag-tamanio') as HTMLSelectElement;
    select.value = '50';
    select.dispatchEvent(new Event('change'));
    expect(tamanios).toEqual([50]);
    expect(paginas).toEqual([1]);
  });

  it('offers the canvas rows-per-page set by default', () => {
    const opciones = Array.from((q(crear(1, 3), 'pag-tamanio') as HTMLSelectElement).options).map(
      (o) => o.value
    );
    expect(opciones).toEqual(['6', '10', '20', '50']);
  });
});
