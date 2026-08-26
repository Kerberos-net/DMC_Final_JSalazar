"""Suite del orquestador `cli_outbox.py` (BACKLOG #14, Fase 4, tareas 4.5/4.6) — cursor/conexion
falsos, sin DB real, sin red, sin reloj externo (`ahora` inyectado). Cubre: dos transacciones
cortas por ciclo (reclamo, luego una por evento — design.md Decision D4), aislamiento por fila,
registro de handlers vacio (#14 no reclama nada porque `destinos_registrados()` esta vacio), y
que un evento `OBSOLETO` nunca toque `Intentos`/`UltimoError` (tarea 4.6, via el SQL real de
`OutboxRepo.marcar`)."""

from __future__ import annotations

from datetime import UTC, datetime

from smartnet_worker.cli_outbox import ejecutar

_AHORA = datetime(2026, 8, 24, 12, 0, 0, tzinfo=UTC)


class _FakeCursor:
    def __init__(self, *, eventos: list[str], filas_reclamar=None, filas_progreso=None):
        self._eventos = eventos
        self._filas_reclamar = filas_reclamar or []
        self._filas_progreso = filas_progreso if filas_progreso is not None else []
        self._ultimo_fetchall: list[tuple] = []
        self._ultimo_fetchone = None

    def execute(self, sentencia: str, *parametros):
        sql = sentencia.lower()
        if "readpast" in sql:
            self._eventos.append("reclamar")
            self._ultimo_fetchall = list(self._filas_reclamar)
            return
        if "max(oe.secuencia)" in sql:
            self._eventos.append(f"progreso:{parametros}")
            self._ultimo_fetchone = self._filas_progreso
            return
        if sql.strip().startswith("update fact.outboxeventintegracion"):
            estado, ahora, evento_id, destino = parametros
            self._eventos.append(f"marcar:{evento_id}:{destino}:{estado}")
            assert "intentos" not in sql
            assert "ultimoerror" not in sql
            return
        raise AssertionError(f"SQL no reconocido por el fake: {sentencia}")

    def fetchall(self):
        return self._ultimo_fetchall

    def fetchone(self):
        return self._ultimo_fetchone


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


def _conectar_fabrica(cursor: _FakeCursor, eventos: list[str]):
    def _conectar(_connection_string: str):
        return _FakeConexion(cursor, eventos)

    return _conectar


def _preparar_entorno(monkeypatch):
    monkeypatch.setenv("SMARTNET_WORKER_ODBC_CONNECTION", "DRIVER={fake};")


def test_sin_destinos_registrados_no_reclama_nada(monkeypatch):
    _preparar_entorno(monkeypatch)
    eventos: list[str] = []
    cursor = _FakeCursor(eventos=eventos)

    resultado = ejecutar(conectar=_conectar_fabrica(cursor, eventos), ahora=lambda: _AHORA)

    # destinos_registrados() esta vacio (#14 no tiene handlers) -> OutboxRepo.reclamar detecta
    # una tupla vacia y ni siquiera ejecuta el SQL de reclamo (guard interno de reclamar()).
    assert resultado == 0
    assert "reclamar" not in eventos
    assert not any(e.startswith("marcar:") for e in eventos)


def test_ciclo_usa_una_transaccion_para_reclamar_y_una_por_evento(monkeypatch):
    _preparar_entorno(monkeypatch)
    eventos: list[str] = []
    filas_reclamar = [(1, "DRIVE", 100, "FACTURA_VALIDADA", '{"version":1}', 6, 0)]
    cursor = _FakeCursor(eventos=eventos, filas_reclamar=filas_reclamar, filas_progreso=(None,))

    import smartnet_worker.despacho_outbox as despacho_outbox

    # REGISTRO_HANDLERS es el MISMO objeto dict importado en cli_outbox y despacho_outbox
    # (`from ... import REGISTRO_HANDLERS`, vinculado por referencia) -- se muta en su lugar,
    # nunca se reasigna, para que ambos modulos vean el mismo registro (igual disciplina que
    # #15/#16 usaran para registrar sus handlers reales).
    monkeypatch.setitem(despacho_outbox.REGISTRO_HANDLERS, "DRIVE", lambda evento: None)

    resultado = ejecutar(conectar=_conectar_fabrica(cursor, eventos), ahora=lambda: _AHORA)

    assert resultado == 0
    # Dos "commit" -> dos conexiones/transacciones distintas: una para reclamar, otra para el
    # unico evento procesado (design.md Decision D4).
    assert eventos.count("commit") == 2
    assert "reclamar" in eventos
    assert any(e.startswith("marcar:1:DRIVE:COMPLETADO") for e in eventos)


def test_evento_obsoleto_marca_obsoleto_sin_tocar_intentos_ni_ultimoerror(monkeypatch):
    _preparar_entorno(monkeypatch)
    eventos: list[str] = []
    filas_reclamar = [(1, "DRIVE", 100, "FACTURA_VALIDADA", '{"version":1}', 5, 0)]
    cursor = _FakeCursor(eventos=eventos, filas_reclamar=filas_reclamar, filas_progreso=(5,))

    import smartnet_worker.despacho_outbox as despacho_outbox

    monkeypatch.setitem(despacho_outbox.REGISTRO_HANDLERS, "DRIVE", lambda evento: None)

    resultado = ejecutar(conectar=_conectar_fabrica(cursor, eventos), ahora=lambda: _AHORA)

    assert resultado == 0
    assert any(e.startswith("marcar:1:DRIVE:OBSOLETO") for e in eventos)
    # La aserción de que el SQL de marcar nunca menciona Intentos/UltimoError ya corrió dentro
    # del fake (_FakeCursor.execute) en cada llamada.


def test_fallo_en_un_evento_no_aborta_el_resto_del_lote(monkeypatch):
    _preparar_entorno(monkeypatch)
    eventos: list[str] = []
    filas_reclamar = [
        (1, "DRIVE", 100, "FACTURA_VALIDADA", '{"version":1}', 6, 0),
        (2, "DRIVE", 101, "FACTURA_VALIDADA", '{"version":1}', 6, 0),
    ]

    class _CursorConFalloEnPrimerProgreso(_FakeCursor):
        def __init__(self, **kwargs):
            super().__init__(**kwargs)
            self._llamadas_progreso = 0

        def execute(self, sentencia: str, *parametros):
            if "max(oe.secuencia)" in sentencia.lower():
                self._llamadas_progreso += 1
                if self._llamadas_progreso == 1:
                    raise RuntimeError("fallo simulado de lectura de progreso")
            return super().execute(sentencia, *parametros)

    cursor = _CursorConFalloEnPrimerProgreso(
        eventos=eventos, filas_reclamar=filas_reclamar, filas_progreso=(None,)
    )

    import smartnet_worker.despacho_outbox as despacho_outbox

    monkeypatch.setitem(despacho_outbox.REGISTRO_HANDLERS, "DRIVE", lambda evento: None)

    resultado = ejecutar(conectar=_conectar_fabrica(cursor, eventos), ahora=lambda: _AHORA)

    assert resultado == 1
    marcados = [e for e in eventos if e.startswith("marcar:")]
    assert len(marcados) == 1
    assert marcados[0].startswith("marcar:2:")
    assert "rollback" in eventos
