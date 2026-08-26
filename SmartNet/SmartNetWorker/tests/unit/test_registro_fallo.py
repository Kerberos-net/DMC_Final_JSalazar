"""RED primero (BACKLOG #17, Fase 3, tasks.md 3.7): `smartnet_worker.registro_fallo` todavia no
existe. `RegistroDeFalloConNotificacion` es la implementacion real de
`despacho_outbox.RegistroDeFallo` que `cli_outbox.py` inyecta: persiste la clasificacion
(`outbox_repo.marcar_fallo`) y, solo si `politica_notificacion.debe_notificar` lo exige, arma los
canales (via una fabrica inyectada, para no requerir credenciales de Telegram/SMTP salvo que una
notificacion realmente se vaya a enviar) y llama `notificaciones.notificar`."""

from __future__ import annotations

from datetime import UTC, datetime

from smartnet_worker.clasificacion_despacho import ResultadoDespacho
from smartnet_worker.errores import Clasificacion
from smartnet_worker.registro_fallo import RegistroDeFalloConNotificacion

_AHORA = datetime(2026, 8, 24, 12, 0, 0, tzinfo=UTC)


class _FakeOutboxRepo:
    def __init__(self, *, clasificacion_previa: str | None):
        self._clasificacion_previa = clasificacion_previa
        self.llamadas_marcar_fallo: list[dict] = []

    def leer_clasificacion(self, evento_id, destino):
        return self._clasificacion_previa

    def marcar_fallo(self, **kwargs):
        self.llamadas_marcar_fallo.append(kwargs)


def test_permanente_marca_fallo_y_notifica():
    repo = _FakeOutboxRepo(clasificacion_previa=None)
    canales_construidos: list[str] = []
    notificaciones: list[str] = []

    def _fabrica_canales():
        canales_construidos.append("construida")
        return ["TELEGRAM"]

    def _notificar(canales, mensaje, instante, cursor):
        notificaciones.append(mensaje)

    registro = RegistroDeFalloConNotificacion(
        repo, cursor=object(), fabrica_canales=_fabrica_canales, notificar=_notificar
    )
    resultado = ResultadoDespacho(
        estado="ERROR",
        clasificacion=Clasificacion.PERMANENTE,
        proximo_intento_en=None,
        agotado=False,
    )

    registro.registrar(1, "DRIVE", resultado, "XML invalido", _AHORA)

    assert repo.llamadas_marcar_fallo[0]["clasificacion"] == "PERMANENTE"
    assert canales_construidos == ["construida"]
    assert len(notificaciones) == 1


def test_transitorio_sin_agotar_no_notifica_ni_construye_canales():
    repo = _FakeOutboxRepo(clasificacion_previa=None)
    canales_construidos: list[str] = []

    def _fabrica_canales():
        canales_construidos.append("construida")
        return []

    def _notificar(canales, mensaje, instante, cursor):
        raise AssertionError("no deberia notificar")

    registro = RegistroDeFalloConNotificacion(
        repo, cursor=object(), fabrica_canales=_fabrica_canales, notificar=_notificar
    )
    resultado = ResultadoDespacho(
        estado="ERROR",
        clasificacion=Clasificacion.TRANSITORIO,
        proximo_intento_en=_AHORA,
        agotado=False,
    )

    registro.registrar(1, "DRIVE", resultado, "timeout", _AHORA)

    assert repo.llamadas_marcar_fallo[0]["clasificacion"] == "TRANSITORIO"
    assert canales_construidos == []


def test_diferible_ya_estaba_diferible_no_repite_notificacion():
    repo = _FakeOutboxRepo(clasificacion_previa="DIFERIBLE")
    notificaciones: list[str] = []

    registro = RegistroDeFalloConNotificacion(
        repo,
        cursor=object(),
        fabrica_canales=lambda: [],
        notificar=lambda canales, mensaje, instante, cursor: notificaciones.append(mensaje),
    )
    resultado = ResultadoDespacho(
        estado="ERROR",
        clasificacion=Clasificacion.DIFERIBLE,
        proximo_intento_en=_AHORA,
        agotado=False,
    )

    registro.registrar(1, "DRIVE", resultado, "429", _AHORA)

    assert notificaciones == []
