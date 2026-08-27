import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { contraste, contrasteSobre } from './contraste';
import { tokensPorTema, type Tema } from './paleta';

/**
 * tasks.md 1.3 (RED first, design.md D2/D3) -- regression guard driven by the REAL tokens parsed
 * from `src/styles.css`, not hard-coded hex. Every text/ink token is checked against ALL FOUR
 * surface levels in BOTH themes, so the 3.82:1 dark `--accento-texto` regression (design.md D3)
 * turns this suite RED. Floors: 4.5:1 text, 3:1 non-text UI (spec.md `spa-design-tokens`).
 */
const RUTA_STYLES = resolve(dirname(fileURLToPath(import.meta.url)), '../../styles.css');
const CSS = readFileSync(RUTA_STYLES, 'utf8');

const TEXTO = 4.5;
const NO_TEXTO = 3;

const SUPERFICIES = [
  '--fondo-app',
  '--fondo-superficie',
  '--fondo-superficie-2',
  '--fondo-sidebar',
] as const;

/** Tokens that render as text/ink over a surface -> full AA text floor on every surface. */
const TINTAS_TEXTO = [
  '--texto-principal',
  '--texto-secundario',
  '--accento-texto',
  '--alerta-ink',
  '--conflicto-ink',
  '--error-ink',
] as const;

/** Tokens that only ever act as non-text UI (borders, decorative) -> 3:1 floor (design.md D3). */
const TINTAS_NO_TEXTO = ['--borde-control', '--texto-terciario'] as const;

/** Ink over its own tinted background. */
const PARES_TINTA_FONDO: readonly [string, string, number][] = [
  ['--accento-texto', '--accento-suave', TEXTO],
  ['--estado-pendiente-ink', '--estado-pendiente-fondo', TEXTO],
  ['--alerta-ink', '--alerta-fondo', TEXTO],
  ['--conflicto-ink', '--conflicto-fondo', TEXTO],
  ['--error-ink', '--error-fondo', TEXTO],
];

describe('contraste -- sanidad de la formula', () => {
  it('devuelve 21 para negro vs blanco', () => {
    expect(contraste('#000000', '#FFFFFF')).toBeCloseTo(21, 0);
  });

  it('devuelve 1 para colores identicos', () => {
    expect(contraste('#B45309', '#B45309')).toBe(1);
  });

  it('es simetrico sin importar el orden', () => {
    expect(contraste('#16191D', '#F7F8FA')).toBeCloseTo(contraste('#F7F8FA', '#16191D'), 5);
  });
});

for (const tema of ['claro', 'oscuro'] as Tema[]) {
  describe(`contraste -- tema ${tema}`, () => {
    const tokens = tokensPorTema(CSS, tema);
    const hex = (nombre: string): string => {
      const v = tokens.get(nombre);
      expect(v, `token ${nombre} ausente en tema ${tema}`).toMatch(/^#[0-9a-f]{3,8}$/i);
      return v!;
    };

    describe.each(TINTAS_TEXTO.flatMap((tinta) => SUPERFICIES.map((sup) => [tinta, sup] as const)))(
      '%s sobre %s',
      (tinta, sup) => {
        it(`>= ${TEXTO}:1`, () => {
          expect(contraste(hex(tinta), hex(sup))).toBeGreaterThanOrEqual(TEXTO);
        });
      },
    );

    describe.each(
      TINTAS_NO_TEXTO.flatMap((tinta) => SUPERFICIES.map((sup) => [tinta, sup] as const)),
    )('%s sobre %s', (tinta, sup) => {
      it(`>= ${NO_TEXTO}:1`, () => {
        expect(contraste(hex(tinta), hex(sup))).toBeGreaterThanOrEqual(NO_TEXTO);
      });
    });

    describe.each(PARES_TINTA_FONDO)('%s sobre %s', (tinta, fondo, piso) => {
      it(`>= ${piso}:1`, () => {
        expect(contraste(hex(tinta), hex(fondo))).toBeGreaterThanOrEqual(piso);
      });
    });

    it('etiqueta blanca sobre el fill --accento >= 4.5:1', () => {
      expect(contraste(hex('--accento-contraste'), hex('--accento'))).toBeGreaterThanOrEqual(TEXTO);
    });

    it('el borde hairline translucido compuesto sobre cada superficie es un hex valido', () => {
      const hairline = tokens.get('--borde-hairline')!;
      for (const sup of SUPERFICIES) {
        const ratio = contrasteSobre(hairline, hex(sup));
        expect(ratio).toBeGreaterThanOrEqual(1);
        expect(ratio).toBeLessThanOrEqual(21);
      }
    });
  });
}
