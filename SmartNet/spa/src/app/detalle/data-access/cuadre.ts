import { LineaRespuesta } from '../models/asiento.model';

/**
 * spec.md pantalla-detalle-validacion "Editing a línea inline" + design D7's `computed()` cuadre
 * recompute — pure function, no I/O, so it is unit-testable without mocks (Extract-Before-Mock).
 * `REGLAS.md`'s cuadre invariant is enforced server-side (`SumaDebeIgualHaber`, 422); this only
 * mirrors the same sum client-side so the screen can show a live indicator before the user submits.
 */
export interface Cuadre {
  readonly debe: number;
  readonly haber: number;
  readonly cuadrado: boolean;
}

const TOLERANCIA = 0.005;

export function calcularCuadre(lineas: readonly LineaRespuesta[]): Cuadre {
  const debe = lineas.reduce((acumulado, linea) => acumulado + linea.debe, 0);
  const haber = lineas.reduce((acumulado, linea) => acumulado + linea.haber, 0);
  return { debe, haber, cuadrado: Math.abs(debe - haber) < TOLERANCIA };
}
