/** Mirrors `SmartNet.Api.DocumentoRespuesta` (BACKLOG #12 — `GET /api/facturas/{id}/documentos`). */
export type OrigenDocumento = 'INGESTA' | 'MANUAL';

export interface DocumentoRespuesta {
  readonly id: string;
  readonly origen: OrigenDocumento;
  readonly nombreArchivo: string;
  readonly mimeType: string;
  readonly fecha: string;
}
