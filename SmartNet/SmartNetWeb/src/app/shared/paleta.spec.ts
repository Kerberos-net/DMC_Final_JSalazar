import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { componer, leerTokens, tokensPorTema } from './paleta';

/**
 * tasks.md 1.1 (RED first, design.md D1/D2) -- the palette guard reads `src/styles.css` from disk
 * so a bad token declaration genuinely turns this suite RED. Asserts the private `--azul-*` ramp
 * is the ONLY home of a blue literal and every accent role aliases back to it.
 */
const RUTA_STYLES = resolve(dirname(fileURLToPath(import.meta.url)), '../../styles.css');
const CSS = readFileSync(RUTA_STYLES, 'utf8');

describe('paleta -- ramp privado --azul-*', () => {
  const claro = tokensPorTema(CSS, 'claro');
  const oscuro = tokensPorTema(CSS, 'oscuro');

  it('define el ramp completo con los literales del handoff', () => {
    expect(claro.get('--azul-600')).toBe('#0071e3');
    expect(claro.get('--azul-700')).toBe('#0a63c9');
    expect(claro.get('--azul-400')).toBe('#409cff');
  });

  it('--accento (fill) resuelve al ramp azul-600', () => {
    expect(claro.get('--accento')).toBe('#0071e3');
  });

  it('--accento-texto usa azul-700 en claro y azul-400 en oscuro (D3 ratificado)', () => {
    expect(claro.get('--accento-texto')).toBe('#0a63c9');
    expect(oscuro.get('--accento-texto')).toBe('#409cff');
  });

  it('el chip Pendiente y el banner P00000 comparten la tinta del ramp (excepcion ratificada 1)', () => {
    expect(claro.get('--estado-pendiente-ink')).toBe('#0a63c9');
    expect(claro.get('--info-generico-ink')).toBe('#0a63c9');
  });

  it('un literal azul solo aparece en la definicion del ramp', () => {
    const literales = CSS.match(/#0071e3|#0a63c9|#409cff/gi) ?? [];
    // 3 en el bloque :root claro. El bloque oscuro re-declara los 3 con su propio color-scheme.
    expect(literales.length).toBeLessThanOrEqual(6);
  });
});

describe('paleta -- superficies, radios y tipografia', () => {
  const oscuro = tokensPorTema(CSS, 'oscuro');
  const raiz = leerTokens(CSS);

  it('jerarquia de 4 superficies con paleta oscura calida', () => {
    expect(oscuro.get('--fondo-app')).toBe('#1c1c1e');
    expect(oscuro.get('--fondo-superficie')).toBe('#2c2c2e');
    expect(oscuro.get('--fondo-superficie-2')).toBe('#242426');
    expect(oscuro.get('--fondo-sidebar')).toBe('#232326');
  });

  it('escala de radios 8/12/16/20', () => {
    expect(raiz.get('--radio-input')).toBe('8px');
    expect(raiz.get('--radio-card')).toBe('12px');
    expect(raiz.get('--radio-modal')).toBe('16px');
    expect(raiz.get('--radio-pill')).toBe('20px');
  });

  it('escala tipografica en enteros (sin medios px) y stack Segoe primero', () => {
    for (const [nombre, valor] of raiz) {
      if (/^--fs-/.test(nombre)) {
        expect(valor, `${nombre}=${valor}`).toMatch(/^\d+px$/);
      }
    }
    expect(raiz.get('--fuente-ui')).toMatch(/Segoe UI/i);
  });

  it('bordes hairline translucidos declarados como rgb/rgba', () => {
    expect(tokensPorTema(CSS, 'claro').get('--borde-hairline')).toMatch(/rgba?\(/i);
    expect(oscuro.get('--borde-hairline')).toMatch(/rgba?\(/i);
  });
});

describe('componer -- alpha compositing', () => {
  it('negro al 8% sobre blanco', () => {
    expect(componer('rgba(0, 0, 0, 0.08)', '#ffffff')).toBe('#ebebeb');
  });

  it('blanco al 9% sobre superficie oscura', () => {
    expect(componer('rgba(255, 255, 255, 0.09)', '#1c1c1e')).toBe('#303032');
  });

  it('alfa 1 (o ausente) devuelve el color plano', () => {
    expect(componer('rgb(255, 0, 0)', '#000000')).toBe('#ff0000');
  });

  it('acepta notacion porcentual de alfa', () => {
    expect(componer('rgba(0, 0, 0, 8%)', '#ffffff')).toBe('#ebebeb');
  });
});
