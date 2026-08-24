/**
 * Mirrors `SmartNet.Api.EntradaAuditoriaRespuesta` (`GET /api/facturas/{id}/historial`,
 * design.md D7) -- newest-first, `200 []` for an unknown factura id (no 404).
 */
export interface EntradaAuditoriaRespuesta {
  readonly entidadTipo: string;
  readonly entidadId: number;
  readonly accion: string;
  readonly campo: string | null;
  readonly valorOriginal: string | null;
  readonly valorNuevo: string | null;
  readonly motivo: string | null;
  readonly usuarioId: number;
  readonly ocurridoEn: string;
}
