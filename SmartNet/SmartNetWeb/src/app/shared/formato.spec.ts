import { dosDecimales, fechaIso, importeOpcional, mesActual, rangoMesActual } from './formato';

describe('formato', () => {
  describe('dosDecimales', () => {
    it('always renders exactly 2 decimals, never 3', () => {
      expect(dosDecimales(118)).toBe('118.00');
      expect(dosDecimales(1234.5)).toBe('1234.50');
      expect(dosDecimales(0)).toBe('0.00');
      expect(dosDecimales(1.2345).split('.')[1]).toHaveLength(2);
      expect(dosDecimales(9.999).split('.')[1]).toHaveLength(2);
    });
  });

  describe('importeOpcional', () => {
    it('renders an em dash when the amount is absent', () => {
      expect(importeOpcional(null)).toBe('—');
      expect(importeOpcional(undefined)).toBe('—');
    });

    it('renders a 2-decimal amount when present', () => {
      expect(importeOpcional(90)).toBe('90.00');
    });
  });

  describe('fechaIso', () => {
    it('formats a Date as LOCAL yyyy-MM-dd, never UTC', () => {
      expect(fechaIso(new Date(2026, 0, 5))).toBe('2026-01-05');
      expect(fechaIso(new Date(2026, 11, 31))).toBe('2026-12-31');
    });
  });

  describe('mesActual', () => {
    it('returns the given date month as LOCAL yyyy-MM', () => {
      expect(mesActual(new Date(2026, 7, 17))).toBe('2026-08');
      expect(mesActual(new Date(2026, 0, 1))).toBe('2026-01');
    });

    it('does not roll into the next month at a late-evening local boundary that UTC would shift', () => {
      // 31 Dec 2026 23:00 LOCAL — toISOString() would report January (next year) in most TZs.
      expect(mesActual(new Date(2026, 11, 31, 23, 0, 0))).toBe('2026-12');
    });

    it('defaults to today when no date is passed', () => {
      const hoy = new Date();
      expect(mesActual()).toBe(
        `${hoy.getFullYear()}-${String(hoy.getMonth() + 1).padStart(2, '0')}`
      );
    });
  });

  describe('rangoMesActual', () => {
    it('spans the first day of the given month .. that day, in LOCAL time', () => {
      expect(rangoMesActual(new Date(2026, 7, 17))).toEqual({
        desde: '2026-08-01',
        hasta: '2026-08-17',
      });
    });

    it('defaults to today when no date is passed', () => {
      const hoy = new Date();
      const esperado = {
        desde: `${hoy.getFullYear()}-${String(hoy.getMonth() + 1).padStart(2, '0')}-01`,
        hasta: `${hoy.getFullYear()}-${String(hoy.getMonth() + 1).padStart(2, '0')}-${String(
          hoy.getDate()
        ).padStart(2, '0')}`,
      };
      expect(rangoMesActual()).toEqual(esperado);
    });
  });
});
