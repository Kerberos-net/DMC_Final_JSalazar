"""RED primero (BACKLOG #17, Fase 3, tasks.md 3.3): `smartnet_worker.notificaciones` todavia no
existe. `notificar` orquesta canales fake (sin red) -- Telegram primero, correo de respaldo si
Telegram lanza, AMBOS intentos logueados via `estado_integracion.registrar_exito`/`registrar_fallo`
(design.md D4: "un envio de Telegram fallido es visible aunque correo tenga exito")."""

from __future__ import annotations

from datetime import UTC, datetime

from smartnet_worker.notificaciones import notificar

_AHORA = datetime(2026, 8, 24, 12, 0, 0, tzinfo=UTC)


class _FakeCanal:
    def __init__(self, nombre: str, *, falla: bool = False):
        self.nombre = nombre
        self._falla = falla
        self.mensajes_enviados: list[str] = []

    def enviar(self, mensaje: str) -> None:
        self.mensajes_enviados.append(mensaje)
        if self._falla:
            raise RuntimeError(f"{self.nombre} no disponible")


class _FakeCursor:
    def __init__(self):
        self.exitos: list[tuple] = []
        self.fallos: list[tuple] = []

    def execute(self, sql, *parametros):
        sql_norm = sql.lower()
        if "ultimoexito" in sql_norm:
            self.exitos.append(parametros)
        elif "ultimoerror" in sql_norm:
            self.fallos.append(parametros)
        self.rowcount = 1

    def fetchone(self):  # pragma: no cover -- no usado por estado_integracion
        return None


def test_telegram_exitoso_no_intenta_correo():
    telegram = _FakeCanal("TELEGRAM")
    correo = _FakeCanal("CORREO")
    cursor = _FakeCursor()

    notificar([telegram, correo], "hola", _AHORA, cursor)

    assert telegram.mensajes_enviados == ["hola"]
    assert correo.mensajes_enviados == []
    assert len(cursor.exitos) == 1
    assert cursor.exitos[0][-1] == "TELEGRAM"


def test_telegram_falla_cae_a_correo_y_ambos_quedan_logueados():
    telegram = _FakeCanal("TELEGRAM", falla=True)
    correo = _FakeCanal("CORREO")
    cursor = _FakeCursor()

    notificar([telegram, correo], "hola", _AHORA, cursor)

    assert telegram.mensajes_enviados == ["hola"]
    assert correo.mensajes_enviados == ["hola"]
    assert len(cursor.fallos) == 1
    assert cursor.fallos[0][-1] == "TELEGRAM"
    assert len(cursor.exitos) == 1
    assert cursor.exitos[0][-1] == "CORREO"


def test_ambos_canales_fallan_ambos_quedan_logueados_como_fallo():
    telegram = _FakeCanal("TELEGRAM", falla=True)
    correo = _FakeCanal("CORREO", falla=True)
    cursor = _FakeCursor()

    notificar([telegram, correo], "hola", _AHORA, cursor)

    assert len(cursor.fallos) == 2
    assert cursor.exitos == []
