"""RED primero (BACKLOG #17, Fase 4, tasks.md 4.4): `smartnet_worker.cli_command_queue` todavia no
existe. Cursor/conexion falsos, sin DB real, sin red, sin reloj externo (`ahora` inyectado). Cubre:
dos transacciones cortas por ciclo (reclamo, luego una por comando), `REPROCESAR_DOCUMENTO` exitoso
marca COMPLETADO, y un handler que lanza agenda reintento o termina en ERROR segun
`clasificacion_despacho.decidir` (mismo umbral de 3 intentos que el outbox)."""

from __future__ import annotations

from datetime import UTC, datetime

from smartnet_worker.cli_command_queue import ejecutar

_AHORA = datetime(2026, 8, 24, 12, 0, 0, tzinfo=UTC)


class _FakeCursor:
    def __init__(self, *, eventos: list[str], filas_reclamar=None):
        self._eventos = eventos
        self._filas_reclamar = filas_reclamar or []

    def execute(self, sentencia: str, *parametros):
        sql = sentencia.lower()
        if "readpast" in sql:
            self._eventos.append("reclamar")
            self._ultimo_fetchall = list(self._filas_reclamar)
            return
        if sql.strip().startswith("update fact.commandqueue set estado"):
            self._eventos.append(f"marcar:{parametros[0]}:{parametros[1]}")
            return
        if "intentos = intentos + 1" in sql:
            self._eventos.append(f"reintento:{parametros[1]}")
            return
        if sql.strip().startswith("update fact.procesamiento"):
            self._eventos.append(f"reprocesar:{parametros[0]}")
            return
        if sql.strip().startswith("update fact.estadointegracion"):
            self._eventos.append("reconectar_google")
            return
        raise AssertionError(f"SQL no reconocido por el fake: {sentencia}")

    def fetchall(self):
        return self._ultimo_fetchall


class _FakeConexion:
    def __init__(self, cursor: _FakeCursor, eventos: list[str]):
        self._cursor = cursor
        self._eventos = eventos

    def cursor(self) -> _FakeCursor:
        return self._cursor

    def commit(self) -> None:
        self._eventos.append("commit")

    def rollback(self) -> None:
        self._eventos.append("rollback")

    def close(self) -> None:
        self._eventos.append("close")


def _conectar_fabrica(cursor, eventos):
    def _conectar(_connection_string: str):
        return _FakeConexion(cursor, eventos)

    return _conectar


def _preparar_entorno(monkeypatch):
    monkeypatch.setenv("SMARTNET_WORKER_ODBC_CONNECTION", "DRIVER={fake};")


def test_reprocesar_documento_exitoso_marca_completado(monkeypatch):
    _preparar_entorno(monkeypatch)
    eventos: list[str] = []
    filas = [(1, "REPROCESAR_DOCUMENTO", 100, "{}", 0, "11111111-1111-1111-1111-111111111111")]
    cursor = _FakeCursor(eventos=eventos, filas_reclamar=filas)

    resultado = ejecutar(conectar=_conectar_fabrica(cursor, eventos), ahora=lambda: _AHORA)

    assert resultado == 0
    assert "reprocesar:100" in eventos
    assert "marcar:COMPLETADO:1" in eventos


def test_sincronizar_gmail_no_cableado_agenda_reintento_sin_agotar(monkeypatch):
    _preparar_entorno(monkeypatch)
    eventos: list[str] = []
    filas = [(2, "SINCRONIZAR_GMAIL", None, "{}", 0, "11111111-1111-1111-1111-111111111111")]
    cursor = _FakeCursor(eventos=eventos, filas_reclamar=filas)

    resultado = ejecutar(conectar=_conectar_fabrica(cursor, eventos), ahora=lambda: _AHORA)

    assert resultado == 1  # NotImplementedError propaga -> ejecutar lo cuenta como fallo del ciclo.
    assert any(e.startswith("reintento:") for e in eventos)
    assert not any(e.startswith("marcar:") for e in eventos)


def test_sincronizar_gmail_agotado_marca_error(monkeypatch):
    _preparar_entorno(monkeypatch)
    eventos: list[str] = []
    filas = [(2, "SINCRONIZAR_GMAIL", None, "{}", 3, "11111111-1111-1111-1111-111111111111")]
    cursor = _FakeCursor(eventos=eventos, filas_reclamar=filas)

    ejecutar(conectar=_conectar_fabrica(cursor, eventos), ahora=lambda: _AHORA)

    assert "marcar:ERROR:2" in eventos


def test_reconectar_google_exitoso_marca_completado(monkeypatch):
    _preparar_entorno(monkeypatch)
    eventos: list[str] = []
    filas = [(3, "RECONECTAR_GOOGLE", None, "{}", 0, "11111111-1111-1111-1111-111111111111")]
    cursor = _FakeCursor(eventos=eventos, filas_reclamar=filas)

    ejecutar(conectar=_conectar_fabrica(cursor, eventos), ahora=lambda: _AHORA)

    assert "reconectar_google" in eventos
    assert "marcar:COMPLETADO:3" in eventos
