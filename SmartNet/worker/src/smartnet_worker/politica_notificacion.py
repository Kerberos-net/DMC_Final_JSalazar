"""Nucleo puro de la politica de notificacion (BACKLOG #17, design.md D4, ADR 0019): sin DB, sin
HTTP, sin reloj. `debe_notificar` implementa la matriz de disparo por clase de ADR 0010; `redactar`
arma el texto plano que `notificaciones.notificar` envia sin transformarlo.

`clasificacion_previa` es la `Clasificacion` ya escrita en la fila `fact.OutboxEventIntegracion`
ANTES de este intento (leida por el llamador antes de `marcar_fallo`) -- el mecanismo de dedupe de
DIFERIBLE del design.md ("una vez por (OutboxEventId, Integracion), leyendo la Clasificacion ya
escrita"): si la fila ya estaba DIFERIBLE, este intento es un reintento diferido, no una entrada
nueva, y no notifica de nuevo."""

from __future__ import annotations

from smartnet_worker.errores import Clasificacion


def debe_notificar(
    clasificacion: Clasificacion,
    *,
    agotado: bool,
    clasificacion_previa: Clasificacion | None,
) -> bool:
    if clasificacion == Clasificacion.PERMANENTE:
        return True
    if clasificacion == Clasificacion.TRANSITORIO:
        return agotado
    if clasificacion == Clasificacion.DIFERIBLE:
        return clasificacion_previa != Clasificacion.DIFERIBLE
    return False  # OBSOLETO -- nunca notifica (ADR 0010).


def redactar(
    *, integracion: str, factura_id: int, clasificacion: Clasificacion, mensaje_error: str
) -> str:
    return (
        f"[{clasificacion.value}] Fallo de despacho a {integracion} para la factura "
        f"{factura_id}: {mensaje_error}"
    )
