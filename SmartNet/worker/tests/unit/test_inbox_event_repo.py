"""Cursor falso, patron `test_procesamiento_repo.py`: SQL y parametros exactos, `fact.` calificado.
Cubre `listar_no_notificados` (LEFT JOIN DatosExtraidos — un `Estado='ERROR'` no tiene fila #6) y
`insertar_evento` como un unico `INSERT...SELECT...WHERE NOT EXISTS` atomico (design.md, Decision
D3) con `Tipo` literal fijo, nunca un segundo valor derivado del `Estado`."""

from __future__ import annotations

from datetime import date
from decimal import Decimal

from smartnet_worker.inbox_event_repo import insertar_evento, listar_no_notificados


class _FakeCursor:
    def __init__(self, *, filas: list[tuple] | None = None):
        self.llamadas: list[tuple[str, tuple]] = []
        self._filas = filas or []

    def execute(self, sentencia: str, *parametros):
        self.llamadas.append((sentencia, parametros))

    def fetchall(self):
        return self._filas


# --- listar_no_notificados ------------------------------------------------------------------


def test_listar_no_notificados_filtra_por_not_exists_inboxevent():
    filas = [
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
    cursor = _FakeCursor(filas=filas)

    resultado = listar_no_notificados(cursor)

    sentencia, _ = cursor.llamadas[0]
    assert "not exists" in sentencia.lower()
    assert "fact.inboxevent" in sentencia.lower()
    assert "fact.procesamiento" in sentencia.lower()
    assert "dbo." not in sentencia.lower()
    assert len(resultado) == 1
    fila = resultado[0]
    assert fila.procesamiento_id == 10
    assert fila.estado == "COMPLETADO"
    assert fila.documento_recibido_id == 8
    assert fila.tipo_documento == "XML"
    assert fila.documento_asociado_id == 9
    assert fila.tipo_comprobante == "01"
    assert fila.monto == Decimal("1180.00")
    assert fila.afectacion_mixta is False


def test_listar_no_notificados_documento_error_sin_datosextraidos():
    filas = [(11, "ERROR", 5, "PDF", None, None, None, None, None, None, None, None, None, None)]
    cursor = _FakeCursor(filas=filas)

    resultado = listar_no_notificados(cursor)

    assert resultado[0].estado == "ERROR"
    assert resultado[0].tipo_comprobante is None
    assert resultado[0].monto is None


# --- insertar_evento -------------------------------------------------------------------------


def test_insertar_evento_es_insert_select_where_not_exists_atomico():
    cursor = _FakeCursor()

    insertar_evento(cursor, 10, '{"version":1}')

    assert len(cursor.llamadas) == 1
    sentencia, parametros = cursor.llamadas[0]
    sql = sentencia.lower()
    assert "insert into fact.inboxevent" in sql
    assert "select" in sql
    assert "where not exists" in sql
    assert "dbo." not in sql
    assert parametros == ("PROCESAMIENTO_FINALIZADO", 10, '{"version":1}', 10)
