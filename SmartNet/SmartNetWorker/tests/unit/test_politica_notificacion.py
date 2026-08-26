"""RED primero (BACKLOG #17, Fase 3, tasks.md 3.1): `smartnet_worker.politica_notificacion`
todavia no existe. Nucleo puro (ADR 0019): `debe_notificar`/`redactar` no tocan DB/HTTP/reloj.

Matriz de disparo (design.md D4): TRANSITORIO solo al agotar el tope, PERMANENTE inmediato,
DIFERIBLE una vez por (evento, integracion) -- dedupe leyendo la `Clasificacion` ya escrita en la
fila --, OBSOLETO nunca."""

from __future__ import annotations

from smartnet_worker.errores import Clasificacion
from smartnet_worker.politica_notificacion import debe_notificar, redactar


def test_transitorio_no_notifica_mientras_quedan_reintentos():
    resultado = debe_notificar(Clasificacion.TRANSITORIO, agotado=False, clasificacion_previa=None)
    assert resultado is False


def test_transitorio_notifica_al_agotar_el_tope():
    resultado = debe_notificar(Clasificacion.TRANSITORIO, agotado=True, clasificacion_previa=None)
    assert resultado is True


def test_permanente_notifica_inmediato():
    resultado = debe_notificar(Clasificacion.PERMANENTE, agotado=False, clasificacion_previa=None)
    assert resultado is True


def test_diferible_notifica_solo_en_la_primera_entrada():
    # clasificacion_previa=None -> es la primera vez que esta fila cae en DIFERIBLE.
    resultado = debe_notificar(Clasificacion.DIFERIBLE, agotado=False, clasificacion_previa=None)
    assert resultado is True


def test_diferible_no_repite_si_ya_estaba_diferible():
    # clasificacion_previa=DIFERIBLE -> la fila ya habia notificado al entrar; el reintento
    # diferido posterior (exito o nuevo fallo) no dispara una segunda alerta.
    resultado = debe_notificar(
        Clasificacion.DIFERIBLE, agotado=False, clasificacion_previa=Clasificacion.DIFERIBLE
    )
    assert resultado is False


def test_obsoleto_nunca_notifica():
    assert debe_notificar(Clasificacion.OBSOLETO, agotado=False, clasificacion_previa=None) is False


def test_redactar_incluye_integracion_factura_clasificacion_y_mensaje():
    texto = redactar(
        integracion="DRIVE",
        factura_id=100,
        clasificacion=Clasificacion.PERMANENTE,
        mensaje_error="XML invalido",
    )

    assert "DRIVE" in texto
    assert "100" in texto
    assert "PERMANENTE" in texto
    assert "XML invalido" in texto
