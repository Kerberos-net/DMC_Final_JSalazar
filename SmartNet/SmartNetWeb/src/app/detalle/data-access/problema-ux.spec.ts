import { describe, expect, it } from 'vitest';
import { categorizarProblema } from './problema-ux';
import { ProblemaDetails } from '../../shared/problema.model';

function problema(status: number, type = 'https://smartnet.local/problemas/x'): ProblemaDetails {
  return { type, title: 't', status, detail: 'd' };
}

describe('categorizarProblema', () => {
  it('maps 412 to conflicto-concurrencia (design D6: blocking reload banner)', () => {
    expect(categorizarProblema(problema(412))).toBe('conflicto-concurrencia');
  });

  it('maps 422 to invariante (design D6: inline field errors)', () => {
    expect(categorizarProblema(problema(422))).toBe('invariante');
  });

  it('maps 409 to negocio (design D6: business-precondition banner)', () => {
    expect(categorizarProblema(problema(409))).toBe('negocio');
  });

  it('maps 428 to precondicion-cliente (design D6: client bug, never a user state)', () => {
    expect(categorizarProblema(problema(428))).toBe('precondicion-cliente');
  });
});
