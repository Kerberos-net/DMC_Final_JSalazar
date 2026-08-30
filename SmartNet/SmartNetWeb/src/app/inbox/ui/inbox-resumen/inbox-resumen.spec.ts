import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { InboxResumen } from './inbox-resumen';
import { ResumenBandeja } from '../../models/bandeja-item.model';

/**
 * tasks.md 3.6 (RED first) — spec `spa-visual-bandeja` ADDED "inbox-page global summary cards":
 * exactly four display-only cards fed from the aggregate. `descartadas`/`total` travel on the wire
 * but are not rendered; the cards are NOT filter shortcuts and expose no output.
 */
describe('InboxResumen', () => {
  const resumen: ResumenBandeja = {
    pendientes: 12,
    validadas: 40,
    conError: 3,
    alertas: 5,
    descartadas: 7,
    total: 67,
  };

  function crear(valor: ResumenBandeja = resumen) {
    const fixture = TestBed.createComponent(InboxResumen);
    fixture.componentRef.setInput('resumen', valor);
    fixture.detectChanges();
    return fixture;
  }

  it('renders exactly four cards, in order Pendientes / Validadas / Con error / Alertas', () => {
    const fixture = crear();
    const tarjetas = Array.from(
      fixture.nativeElement.querySelectorAll('[data-testid="tarjeta-resumen"]')
    ) as HTMLElement[];

    expect(tarjetas.length).toBe(4);
    expect(tarjetas.map((t) => t.querySelector('[data-testid="tarjeta-etiqueta"]')?.textContent?.trim()))
      .toEqual(['Pendientes', 'Validadas', 'Con error', 'Alertas']);
  });

  it('shows each bucket value from the input', () => {
    const fixture = crear();
    const valores = Array.from(
      fixture.nativeElement.querySelectorAll('[data-testid="tarjeta-valor"]')
    ).map((v) => (v as HTMLElement).textContent?.trim());

    expect(valores).toEqual(['12', '40', '3', '5']);
  });

  it('does not render descartadas or total', () => {
    const fixture = crear();
    expect(fixture.nativeElement.textContent).not.toContain('Descartadas');
    expect(fixture.nativeElement.textContent).not.toContain('67');
  });

  it('is display-only: no button, no output, clicking a card does nothing observable', () => {
    const fixture = crear();
    const root: HTMLElement = fixture.nativeElement;
    expect(root.querySelector('button')).toBeNull();
    expect(root.querySelector('a')).toBeNull();
    expect('resumenSeleccionado' in fixture.componentInstance).toBe(false);

    const tarjeta = root.querySelector('[data-testid="tarjeta-resumen"]') as HTMLElement;
    expect(() => tarjeta.click()).not.toThrow();
  });
});
