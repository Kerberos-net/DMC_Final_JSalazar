import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { BotonExportar } from './boton-exportar';

/**
 * tasks.md 3.5 (RED first, design D8) -- presentational "Exportar a Excel" button. It only emits
 * the intent; the blob GET + browser download live in `data-access/descarga-xlsx.ts`. CSS-div
 * green sheet glyph (no svg / img -- sidebar glyph precedent).
 */
describe('BotonExportar', () => {
  function crear(descargando = false) {
    const fixture = TestBed.createComponent(BotonExportar);
    fixture.componentRef.setInput('descargando', descargando);
    fixture.detectChanges();
    return fixture;
  }

  it('emits exportar on click', () => {
    const fixture = crear();
    let veces = 0;
    fixture.componentInstance.exportar.subscribe(() => (veces += 1));
    fixture.nativeElement.querySelector('button').click();
    expect(veces).toBe(1);
  });

  it('is disabled and shows progress copy while descargando', () => {
    const boton = crear(true).nativeElement.querySelector('button') as HTMLButtonElement;
    expect(boton.disabled).toBe(true);
    expect(boton.textContent).toContain('Generando');
  });

  it('renders a CSS glyph, never an <img> or <svg>', () => {
    const root: HTMLElement = crear().nativeElement;
    expect(root.querySelector('img')).toBeNull();
    expect(root.querySelector('svg')).toBeNull();
    expect(root.querySelector('[data-testid="boton-exportar-glifo"]')).not.toBeNull();
  });
});
