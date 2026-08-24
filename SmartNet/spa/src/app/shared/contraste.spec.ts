import { describe, expect, it } from 'vitest';
import { contraste } from './contraste';

/**
 * tasks.md 3.2 (RED first, design.md D5/palette) -- regression guard against the exact hex pairs
 * design.md's "Palette and verified contrast" table already computed. Floors: 4.5:1 text, 3:1
 * non-text UI, per spec.md `spa-design-tokens` "WCAG AA contrast compliance per token pair".
 */
describe('contraste', () => {
  it('returns 21 for pure black vs pure white', () => {
    expect(contraste('#000000', '#FFFFFF')).toBeCloseTo(21, 0);
  });

  it('returns 1 for identical colors', () => {
    expect(contraste('#B45309', '#B45309')).toBe(1);
  });

  it('is symmetric regardless of argument order', () => {
    expect(contraste('#16191D', '#F7F8FA')).toBeCloseTo(contraste('#F7F8FA', '#16191D'), 5);
  });

  const TEXTO = 4.5;
  const NO_TEXTO = 3;

  describe.each([
    // Tema claro -- fondo-app #F7F8FA / fondo-superficie #FFFFFF
    ['claro texto-principal / fondo-app', '#16191D', '#F7F8FA', TEXTO],
    ['claro texto-secundario / fondo-app', '#5A6270', '#F7F8FA', TEXTO],
    ['claro borde-control / fondo-app', '#767E8C', '#F7F8FA', NO_TEXTO],
    ['claro alerta-ink / fondo-app', '#B45309', '#F7F8FA', TEXTO],
    ['claro alerta-texto / alerta-fondo', '#7C3D00', '#FDF0E1', TEXTO],
    ['claro conflicto-ink / fondo-app', '#6D28D9', '#F7F8FA', TEXTO],
    ['claro conflicto-ink / conflicto-fondo', '#6D28D9', '#F1EBFD', TEXTO],
    ['claro error-ink / fondo-app', '#B91C1C', '#F7F8FA', TEXTO],
    ['claro error-ink / error-fondo', '#B91C1C', '#FDECEC', TEXTO],
    ['claro estado-pendiente-texto / estado-pendiente-fondo', '#363C46', '#EAECF0', TEXTO],
    ['claro estado-pendiente-borde / fondo-app', '#767E8C', '#F7F8FA', NO_TEXTO],
    ['claro estado-validada-texto / estado-validada-fondo', '#14532D', '#DCF3E3', TEXTO],
    ['claro estado-validada-borde / fondo-app', '#1E7A44', '#F7F8FA', NO_TEXTO],
    ['claro accion-texto / accion-fondo', '#FFFFFF', '#1F2937', TEXTO],
    ['claro focus-ring / fondo-app', '#1D4ED8', '#F7F8FA', NO_TEXTO],
    // Tema oscuro -- fondo-app #12151A / fondo-superficie #1A1F26
    ['oscuro texto-principal / fondo-app', '#E8EBF0', '#12151A', TEXTO],
    ['oscuro texto-principal / superficie', '#E8EBF0', '#1A1F26', TEXTO],
    ['oscuro texto-secundario / fondo-app', '#A2ABBA', '#12151A', TEXTO],
    ['oscuro texto-secundario / superficie', '#A2ABBA', '#1A1F26', TEXTO],
    ['oscuro borde-control / fondo-app', '#6E7787', '#12151A', NO_TEXTO],
    ['oscuro borde-control / superficie', '#6E7787', '#1A1F26', NO_TEXTO],
    ['oscuro alerta-ink / superficie', '#E8A33D', '#1A1F26', TEXTO],
    ['oscuro alerta-ink / alerta-fondo', '#E8A33D', '#3A2A14', TEXTO],
    ['oscuro conflicto-ink / superficie', '#B79BF5', '#1A1F26', TEXTO],
    ['oscuro conflicto-ink / conflicto-fondo', '#B79BF5', '#241C3D', TEXTO],
    ['oscuro error-ink / superficie', '#F58787', '#1A1F26', TEXTO],
    ['oscuro error-ink / error-fondo', '#F58787', '#3A1E1E', TEXTO],
    ['oscuro estado-pendiente-borde / fondo-app', '#8A93A3', '#12151A', NO_TEXTO],
    ['oscuro estado-validada-borde / fondo-app', '#58C97F', '#12151A', NO_TEXTO],
    ['oscuro accion-texto / accion-fondo', '#12151A', '#E8EBF0', TEXTO],
    ['oscuro focus-ring / fondo-app', '#7FA9FF', '#12151A', NO_TEXTO],
  ])('%s meets its WCAG AA floor', (_nombre, hexA, hexB, piso) => {
    it(`contraste(${hexA}, ${hexB}) >= ${piso}`, () => {
      expect(contraste(hexA, hexB)).toBeGreaterThanOrEqual(piso);
    });
  });
});
