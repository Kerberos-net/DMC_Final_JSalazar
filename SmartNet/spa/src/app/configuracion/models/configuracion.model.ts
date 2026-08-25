/**
 * design D6 — mirror of `ConfiguracionEntradaRespuesta` (SmartNet.Api/ConfiguracionEndpoints.cs):
 * one row of `fact.Configuracion` (007_publicacion.sql:24-40). `Tipo` drives client-side validation
 * hints only — the server (`ValorDeConfiguracion.Validar`) is authoritative (spec.md configuracion-api-spa).
 */
export type TipoConfiguracion = 'TEXTO' | 'ENTERO' | 'DECIMAL' | 'BOOLEANO' | 'FECHA' | 'LISTA';

export interface ConfiguracionEntrada {
  seccion: string;
  clave: string;
  tipo: TipoConfiguracion;
  valor: string | null;
  valorPorDefecto: string | null;
  descripcion: string;
}

/** `PUT /api/configuracion/{seccion}/{clave}` body — `valor: null` is legal ("use ValorPorDefecto"). */
export interface ActualizarConfiguracionRequest {
  valor: string | null;
}
