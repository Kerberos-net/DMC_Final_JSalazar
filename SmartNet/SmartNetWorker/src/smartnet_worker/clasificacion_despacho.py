"""Nucleo puro de clasificacion del wrapper de despacho (BACKLOG #17, design.md D2/D3, ADR 0019):
ni DB, ni HTTP, ni reloj -- `instante` siempre llega como parametro. Envuelve `errores.clasificar`/
`errores.proximo_reintento` (BACKLOG #6) con la decision especifica del outbox: cuando agendar el
siguiente intento y cuando una racha TRANSITORIO se considera agotada (-> notificar,
`notificaciones-telegram-correo`).

`decidir` nunca se invoca para un evento OBSOLETO -- `despacho_outbox.despachar_evento` corta antes
del handler (D5); este modulo solo clasifica excepciones que el handler efectivamente lanzo."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime, timedelta
from email.utils import parsedate_to_datetime

from smartnet_worker.errores import Clasificacion, CuotaExcedidaError, clasificar, proximo_reintento

# Mismo tope que `errores._TOPE_INTENTOS_BACKOFF` (BACKLOG #6): una racha TRANSITORIO se considera
# agotada al tercer intento fallido -- ahi es donde `notificaciones-telegram-correo` dispara su
# unica alerta (design.md D4, "TRANSITORIO solo al agotar el tope").
_TOPE_INTENTOS = 3


@dataclass(frozen=True)
class ResultadoDespacho:
    """Veredicto puro de un intento de despacho fallido -- lo que `despacho_outbox.py` necesita
    para escribir `fact.OutboxEventIntegracion` (via `outbox_repo.marcar_fallo`) y decidir si
    dispara una notificacion (`politica_notificacion.debe_notificar`)."""

    estado: str  # 'COMPLETADO' | 'ERROR' | 'OBSOLETO' -- `decidir` solo produce 'ERROR'.
    clasificacion: Clasificacion | None
    proximo_intento_en: datetime | None
    agotado: bool  # TRANSITORIO en el tope -> notificar.


def decidir(error: BaseException, intentos: int, instante: datetime) -> ResultadoDespacho:
    """`intentos` es el numero de intentos fallidos PREVIOS a este (0 en el primer fallo). Un
    `CuotaExcedidaError` siempre clasifica DIFERIBLE (D3); cualquier otra excepcion pasa por
    `errores.clasificar` (PERMANENTE/TRANSITORIO, nunca OBSOLETO -- ver docstring del modulo)."""
    intentos_totales = intentos + 1

    if isinstance(error, CuotaExcedidaError):
        clasificacion = Clasificacion.DIFERIBLE
        proximo_intento_en = (
            instante + error.retry_after
            if error.retry_after is not None
            else proximo_reintento(clasificacion, instante, intentos_totales)
        )
        return ResultadoDespacho(
            estado="ERROR",
            clasificacion=clasificacion,
            proximo_intento_en=proximo_intento_en,
            agotado=False,
        )

    clasificacion = clasificar(error)

    if clasificacion == Clasificacion.PERMANENTE:
        return ResultadoDespacho(
            estado="ERROR", clasificacion=clasificacion, proximo_intento_en=None, agotado=False
        )

    # TRANSITORIO.
    agotado = intentos_totales >= _TOPE_INTENTOS
    proximo_intento_en = (
        None if agotado else proximo_reintento(clasificacion, instante, intentos_totales)
    )
    return ResultadoDespacho(
        estado="ERROR",
        clasificacion=clasificacion,
        proximo_intento_en=proximo_intento_en,
        agotado=agotado,
    )


def retry_after_desde(cabecera: str | None, instante: datetime) -> timedelta | None:
    """Parsea una cabecera HTTP `Retry-After` (RFC 9110 sec 10.2.3): delta-segundos (`"120"`) o
    fecha HTTP (`"Wed, 24 Aug 2026 12:02:00 GMT"`). Recibe la cadena, nunca un objeto `Response` --
    sin tipo HTTP, sin reloj (ADR 0019). Una fecha ya pasada devuelve `timedelta(0)`, nunca un
    delta negativo."""
    if cabecera is None:
        return None

    cabecera = cabecera.strip()
    if cabecera.isdigit():
        return timedelta(seconds=int(cabecera))

    try:
        fecha = parsedate_to_datetime(cabecera)
    except (TypeError, ValueError):
        return None

    delta = fecha - instante
    return delta if delta > timedelta(0) else timedelta(0)
