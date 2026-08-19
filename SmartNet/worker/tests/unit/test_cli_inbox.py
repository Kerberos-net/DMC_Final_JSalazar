"""Suite del orquestador `cli_inbox.py` (BACKLOG #7, WU1) — cursor falso, sin DB real, sin red,
sin reloj externo. Cubre: un ciclo lee no-notificados -> construye payload -> INSERT -> commit;
aislamiento por fila (un fallo no aborta el batch); `Tipo` siempre `PROCESAMIENTO_FINALIZADO`,
nunca un segundo literal derivado del `Estado` (mismo patron que
`test_cli_procesamiento.py::_FakeCursor`, dispatch por substring del SQL)."""

from __future__ import annotations

from datetime import date
from decimal import Decimal

from smartnet_worker.cli_inbox import ejecutar


class _FakeCursor:
    def __init__(
        self, *, pendientes_filas: list[tuple] | None = None, eventos: list[str] | None = None
    ):
        self._pendientes_filas = pendientes_filas or []
        self.eventos = eventos if eventos is not None else []

    def execute(self, sentencia: str, *parametros):
        sql = sentencia.lower()
        if "from fact.procesamiento p" in sql and "not exists" in sql:
            self.eventos.append("listar_no_notificados")
            self._ultimo_fetchall = list(self._pendientes_filas)
            return
        if "insert into fact.inboxevent" in sql:
            tipo, procesamiento_id, payload, _ = parametros
            self.eventos.append(f"insertar_evento:{procesamiento_id}:{tipo}:{payload}")
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


def _conectar_fabrica(cursor: _FakeCursor, eventos: list[str]):
    def _conectar(_connection_string: str):
        return _FakeConexion(cursor, eventos)

    return _conectar


def _preparar_entorno(monkeypatch):
    monkeypatch.setenv("SMARTNET_WORKER_ODBC_CONNECTION", "DRIVER={fake};")


def test_ciclo_publica_un_evento_por_fila_completada(monkeypatch):
    _preparar_entorno(monkeypatch)
    eventos: list[str] = []
    pendientes_filas = [
        (
            10,
            "COMPLETADO",
            8,
            "XML",
            9,
            "01",
            "F001-123",
            "20100000001",
            "Proveedor SAC",
            Decimal("1180.00"),
            "PEN",
            date(2026, 8, 10),
            None,
            False,
        )
    ]
    cursor = _FakeCursor(pendientes_filas=pendientes_filas, eventos=eventos)

    resultado = ejecutar(conectar=_conectar_fabrica(cursor, eventos))

    assert resultado == 0
    insercion = next(e for e in eventos if e.startswith("insertar_evento:"))
    assert insercion.startswith("insertar_evento:10:PROCESAMIENTO_FINALIZADO:")
    assert '"estadoProcesamiento": "COMPLETADO"' in insercion
    assert "commit" in eventos


def test_documento_error_publica_evento_sin_comprobante(monkeypatch):
    _preparar_entorno(monkeypatch)
    eventos: list[str] = []
    pendientes_filas = [
        (11, "ERROR", 5, "PDF", None, None, None, None, None, None, None, None, None, None)
    ]
    cursor = _FakeCursor(pendientes_filas=pendientes_filas, eventos=eventos)

    resultado = ejecutar(conectar=_conectar_fabrica(cursor, eventos))

    assert resultado == 0
    insercion = next(e for e in eventos if e.startswith("insertar_evento:"))
    assert '"comprobante": null' in insercion
    assert '"estadoProcesamiento": "ERROR"' in insercion


def test_fallo_de_una_fila_no_aborta_el_batch(monkeypatch):
    _preparar_entorno(monkeypatch)
    eventos: list[str] = []
    pendientes_filas = [
        (10, "COMPLETADO", 8, "XML", None, None, None, None, None, None, None, None, None, None),
        (11, "COMPLETADO", 9, "XML", None, None, None, None, None, None, None, None, None, None),
    ]

    class _CursorConFalloEnPrimeraInsercion(_FakeCursor):
        def __init__(self, **kwargs):
            super().__init__(**kwargs)
            self._inserciones = 0

        def execute(self, sentencia: str, *parametros):
            if "insert into fact.inboxevent" in sentencia.lower():
                self._inserciones += 1
                if self._inserciones == 1:
                    raise RuntimeError("fallo simulado de escritura")
            return super().execute(sentencia, *parametros)

    cursor = _CursorConFalloEnPrimeraInsercion(pendientes_filas=pendientes_filas, eventos=eventos)

    resultado = ejecutar(conectar=_conectar_fabrica(cursor, eventos))

    assert resultado == 1
    inserciones = [e for e in eventos if e.startswith("insertar_evento:")]
    assert len(inserciones) == 1
    assert inserciones[0].startswith("insertar_evento:11:")
    assert "rollback" in eventos


def test_ningun_pendiente_devuelve_exito_sin_inserciones(monkeypatch):
    _preparar_entorno(monkeypatch)
    eventos: list[str] = []
    cursor = _FakeCursor(pendientes_filas=[], eventos=eventos)

    resultado = ejecutar(conectar=_conectar_fabrica(cursor, eventos))

    assert resultado == 0
    assert not any(e.startswith("insertar_evento:") for e in eventos)
