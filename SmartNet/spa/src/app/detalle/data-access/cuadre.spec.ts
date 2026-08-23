import { describe, expect, it } from 'vitest';
import { calcularCuadre } from './cuadre';
import { LineaRespuesta } from '../models/asiento.model';

function linea(parcial: Partial<LineaRespuesta>): LineaRespuesta {
  return {
    lineaId: 1,
    orden: 1,
    bloque: 'PRINCIPAL',
    tipo: 'D',
    debe: 0,
    haber: 0,
    cuentaCodigo: '639915',
    cuentaDescripcion: null,
    ctaReflejaCodigo: null,
    ctaPuenteCodigo: null,
    ...parcial,
  };
}

describe('calcularCuadre', () => {
  it('sums debe/haber and reports cuadrado=true when they match', () => {
    const lineas = [
      linea({ lineaId: 1, tipo: 'D', debe: 118, haber: 0 }),
      linea({ lineaId: 2, tipo: 'H', debe: 0, haber: 118 }),
    ];

    const resultado = calcularCuadre(lineas);

    expect(resultado.debe).toBe(118);
    expect(resultado.haber).toBe(118);
    expect(resultado.cuadrado).toBe(true);
  });

  it('reports cuadrado=false when debe and haber differ', () => {
    const lineas = [
      linea({ lineaId: 1, tipo: 'D', debe: 100, haber: 0 }),
      linea({ lineaId: 2, tipo: 'H', debe: 0, haber: 118 }),
    ];

    const resultado = calcularCuadre(lineas);

    expect(resultado.debe).toBe(100);
    expect(resultado.haber).toBe(118);
    expect(resultado.cuadrado).toBe(false);
  });
});
