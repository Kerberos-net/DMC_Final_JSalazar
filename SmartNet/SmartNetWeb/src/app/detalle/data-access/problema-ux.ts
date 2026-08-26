import { ProblemaDetails } from '../../shared/problema.model';

/**
 * design D6: "Conflict UX discriminates on the RFC 9457 `type` URI, not the status code" for the
 * exact banner MESSAGE (a 409 can be `CasoConflicto` or a Global 3/4 invariant) — but the top-level
 * UX BUCKET the three spec.md scenarios distinguish (412 reload / 422 inline / 409 banner) already
 * follows the status code one-to-one, so this pure mapping is enough to route to the right
 * component; `ProblemaDetails.type`/`.title`/`.detail` still carry the exact message text.
 */
export type CategoriaProblema = 'conflicto-concurrencia' | 'invariante' | 'negocio' | 'precondicion-cliente';

export function categorizarProblema(problema: ProblemaDetails): CategoriaProblema {
  switch (problema.status) {
    case 412:
      return 'conflicto-concurrencia';
    case 422:
      return 'invariante';
    case 428:
      return 'precondicion-cliente';
    default:
      return 'negocio';
  }
}
