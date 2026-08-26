/**
 * Pure WCAG 2.x relative-luminance contrast ratio (design.md D5, spec.md `spa-design-tokens`
 * "WCAG AA contrast compliance per token pair"). No DOM, no Angular -- a regression guard the
 * palette in `styles.css` must keep satisfying, exercised by `contraste.spec.ts`.
 */
function aLinear(canal: number): number {
  const s = canal / 255;
  return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
}

function luminanciaRelativa(hex: string): number {
  const limpio = hex.replace('#', '');
  const r = parseInt(limpio.substring(0, 2), 16);
  const g = parseInt(limpio.substring(2, 4), 16);
  const b = parseInt(limpio.substring(4, 6), 16);
  return 0.2126 * aLinear(r) + 0.7152 * aLinear(g) + 0.0722 * aLinear(b);
}

/** Ratio in [1, 21]; symmetric in its two arguments (WCAG 2.x formula). */
export function contraste(hexA: string, hexB: string): number {
  const l1 = luminanciaRelativa(hexA);
  const l2 = luminanciaRelativa(hexB);
  const masClaro = Math.max(l1, l2);
  const masOscuro = Math.min(l1, l2);
  return (masClaro + 0.05) / (masOscuro + 0.05);
}
