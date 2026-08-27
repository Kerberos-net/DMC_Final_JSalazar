/**
 * Pure token utilities backing the WCAG palette guard (design.md D2 -- "the guard must READ
 * styles.css or Strict TDD is theatre"). This module performs NO I/O: `paleta.spec.ts` does the
 * `node:fs` read of `src/styles.css` and feeds the raw text in here. Mirrors ADR 0019 on the SPA
 * side -- parsing is pure, the filesystem boundary lives in the spec.
 */

export type MapaTokens = Map<string, string>;
export type Tema = 'claro' | 'oscuro';

/** Strip `/* ... *\/` comments so declaration-like text inside prose is never parsed as a token. */
export function sinComentarios(css: string): string {
  return css.replace(/\/\*[\s\S]*?\*\//g, '');
}

/** Parse every `--nombre: valor` custom-property declaration from a CSS block body. */
export function leerTokens(css: string): MapaTokens {
  const mapa: MapaTokens = new Map();
  const re = /(--[a-z0-9-]+)\s*:\s*([^;{}]+?)\s*(?=[;}]|$)/gi;
  let m: RegExpExecArray | null;
  const limpio = sinComentarios(css);
  while ((m = re.exec(limpio)) !== null) {
    mapa.set(m[1].trim(), m[2].trim());
  }
  return mapa;
}

/**
 * Resolve the effective token map for one theme: base `:root` declarations plus the
 * theme-specific `:root[data-tema='...']` block, in source order. The opposite theme's block is
 * skipped so light never inherits dark literals.
 */
export function tokensPorTema(css: string, tema: Tema): MapaTokens {
  const mapa: MapaTokens = new Map();
  for (const bloque of sinComentarios(css).matchAll(/([^{}]+)\{([^{}]*)\}/g)) {
    const selector = bloque[1].trim();
    if (!selector.includes(':root')) continue;
    const esOscuro = /data-tema=['"]oscuro['"]/.test(selector);
    const esClaro = /data-tema=['"]claro['"]/.test(selector);
    if (tema === 'claro' && esOscuro) continue;
    if (tema === 'oscuro' && esClaro) continue;
    for (const [k, v] of leerTokens(bloque[2])) mapa.set(k, v);
  }
  return resolver(mapa);
}

/** Recursively expand `var(--x[, fallback])` references against the given map. */
export function resolver(mapa: MapaTokens): MapaTokens {
  const expandir = (valor: string, prof: number): string => {
    if (prof > 20) return valor.trim();
    const m = valor.match(/var\(\s*(--[a-z0-9-]+)\s*(?:,\s*([^()]+))?\)/i);
    if (!m) return valor.trim();
    const referido = mapa.has(m[1]) ? expandir(mapa.get(m[1])!, prof + 1) : (m[2] ?? '').trim();
    return expandir(valor.replace(m[0], referido), prof + 1);
  };
  const out: MapaTokens = new Map();
  for (const [k, v] of mapa) out.set(k, expandir(v, 0));
  return out;
}

function canal(valor: string): number {
  const t = valor.trim();
  if (t.endsWith('%')) return Math.round((parseFloat(t) / 100) * 255);
  return Math.round(parseFloat(t));
}

function alfa(valor: string): number {
  const t = valor.trim();
  return t.endsWith('%') ? parseFloat(t) / 100 : parseFloat(t);
}

function hexARgb(hex: string): { r: number; g: number; b: number } {
  let h = hex.trim().replace('#', '');
  if (h.length === 3)
    h = h
      .split('')
      .map((c) => c + c)
      .join('');
  return {
    r: parseInt(h.substring(0, 2), 16),
    g: parseInt(h.substring(2, 4), 16),
    b: parseInt(h.substring(4, 6), 16),
  };
}

/**
 * Flatten a translucent `rgb()/rgba()` color over an opaque hex background (the alpha compositing
 * the new translucent hairline borders need before they can be contrast-rated). Returns `#rrggbb`.
 */
export function componer(rgba: string, fondoHex: string): string {
  const m = rgba.match(/rgba?\(([^)]+)\)/i);
  if (!m) return rgba.trim();
  const partes = m[1]
    .split(/[,/]/)
    .map((x) => x.trim())
    .filter((x) => x.length > 0);
  const r = canal(partes[0]);
  const g = canal(partes[1]);
  const b = canal(partes[2]);
  const a = partes[3] !== undefined ? alfa(partes[3]) : 1;
  const f = hexARgb(fondoHex);
  const mezcla = (c: number, cf: number) =>
    Math.min(255, Math.max(0, Math.round(c * a + cf * (1 - a))));
  return (
    '#' +
    [mezcla(r, f.r), mezcla(g, f.g), mezcla(b, f.b)]
      .map((n) => n.toString(16).padStart(2, '0'))
      .join('')
  );
}
