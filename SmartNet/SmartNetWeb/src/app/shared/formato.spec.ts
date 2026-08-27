import { dosDecimales, importeOpcional } from './formato';

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
});
