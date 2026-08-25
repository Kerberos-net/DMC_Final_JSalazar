"""RED primero (BACKLOG #17, Fase 2, tasks.md 2.1): `smartnet_worker.clasificacion_despacho`
todavia no existe. Nucleo puro (ADR 0019): sin DB, sin HTTP, sin reloj -- `instante` siempre llega
como parametro (mismo principio que `errores.proximo_reintento`).

Cubre las cuatro clases de ADR 0010 sobre el resultado de `decidir`: TRANSITORIO recupera dentro de
3 intentos y se agota en el tope (design.md D2/D3), DIFERIBLE honra `retry_after` verbatim o cae a
`errores.proximo_reintento`, PERMANENTE nunca agenda reintento (reusa `errores.clasificar`).
OBSOLETO no tiene productor aqui: `despachar_evento` corta antes del handler
(`despacho_outbox.py`, ya cubierto por `test_despacho_outbox.py`) -- `decidir` nunca lo produce."""

from __future__ import annotations

from datetime import UTC, datetime, timedelta

from smartnet_worker.clasificacion_despacho import (
    ResultadoDespacho,
    decidir,
    retry_after_desde,
)
from smartnet_worker.errores import Clasificacion, CuotaExcedidaError

_AHORA = datetime(2026, 8, 24, 12, 0, 0, tzinfo=UTC)


def test_transitorio_recupera_dentro_de_3_intentos_agenda_backoff():
    resultado = decidir(ValueError("timeout"), intentos=0, instante=_AHORA)

    assert isinstance(resultado, ResultadoDespacho)
    assert resultado.estado == "ERROR"
    assert resultado.clasificacion == Clasificacion.TRANSITORIO
    assert resultado.agotado is False
    assert resultado.proximo_intento_en == _AHORA + timedelta(seconds=2)


def test_transitorio_se_agota_en_el_tope_de_3_intentos():
    resultado = decidir(ValueError("timeout"), intentos=2, instante=_AHORA)

    assert resultado.clasificacion == Clasificacion.TRANSITORIO
    assert resultado.agotado is True
    assert resultado.proximo_intento_en is None


def test_permanente_nunca_agenda_reintento():
    from lxml.etree import XMLSyntaxError

    try:
        from lxml import etree

        etree.fromstring(b"<no-cierra>")
    except XMLSyntaxError as error:
        excepcion = error
    else:  # pragma: no cover
        raise AssertionError("se esperaba XMLSyntaxError")

    resultado = decidir(excepcion, intentos=0, instante=_AHORA)

    assert resultado.clasificacion == Clasificacion.PERMANENTE
    assert resultado.agotado is False
    assert resultado.proximo_intento_en is None
    assert resultado.estado == "ERROR"


def test_diferible_honra_retry_after_verbatim():
    error = CuotaExcedidaError(retry_after=timedelta(seconds=120))

    resultado = decidir(error, intentos=0, instante=_AHORA)

    assert resultado.clasificacion == Clasificacion.DIFERIBLE
    assert resultado.proximo_intento_en == _AHORA + timedelta(seconds=120)
    assert resultado.agotado is False


def test_diferible_sin_retry_after_cae_a_proximo_reintento():
    error = CuotaExcedidaError(retry_after=None)

    resultado = decidir(error, intentos=0, instante=_AHORA)

    assert resultado.clasificacion == Clasificacion.DIFERIBLE
    assert resultado.proximo_intento_en == _AHORA + timedelta(seconds=2)


def test_retry_after_desde_delta_segundos():
    assert retry_after_desde("120", _AHORA) == timedelta(seconds=120)


def test_retry_after_desde_fecha_http():
    cabecera = "Wed, 24 Aug 2026 12:02:00 GMT"
    assert retry_after_desde(cabecera, _AHORA) == timedelta(seconds=120)


def test_retry_after_desde_none_devuelve_none():
    assert retry_after_desde(None, _AHORA) is None


def test_retry_after_desde_fecha_pasada_devuelve_cero():
    cabecera = "Wed, 24 Aug 2026 11:00:00 GMT"
    assert retry_after_desde(cabecera, _AHORA) == timedelta(seconds=0)
