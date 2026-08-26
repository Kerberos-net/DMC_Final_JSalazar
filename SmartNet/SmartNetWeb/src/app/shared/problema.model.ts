/**
 * RFC 9457 Problem Details shape returned by the API's business-error responses (design.md D6:
 * "discriminates on the `type` URI, not the status code" — 409 is produced by BOTH
 * `CasoConflicto` and Global 3/4 invariants). Consumed by the detalle feature (BACKLOG #12
 * Phase 5) to map `type` to the correct UX: 412 blocking reload, 422 inline field errors,
 * 409 business-precondition banner keyed by `type`.
 */
export interface ProblemaDetails {
  type: string;
  title: string;
  status: number;
  detail?: string;
  errors?: Record<string, string[]>;
  importeEsperado?: number;
  importeReal?: number;
}
