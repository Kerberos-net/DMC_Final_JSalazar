from datetime import date, datetime
from decimal import Decimal

import pyodbc

from smartnet_worker.sbs import TipoCambioSbs
from smartnet_worker.tipo_cambio_repo import insertar_sbs


class _FakeCursor:
    """Cursor falso que registra la sentencia y los parametros exactos que recibio, sin tocar
    ninguna base real — lo que permite probar el SQL emitido sin DB (design.md, Testing Strategy:
    "Cursor falso que registra sentencia y parametros")."""

    def __init__(self, *, lanzar_integrity_error: bool = False):
        self.sentencia: str | None = None
        self.parametros: tuple = ()
        self._lanzar_integrity_error = lanzar_integrity_error

    def execute(self, sentencia: str, *parametros):
        self.sentencia = sentencia
        self.parametros = parametros
        if self._lanzar_integrity_error:
            raise pyodbc.IntegrityError("23000", "Violacion de PK (Fecha, Origen)")


def _tipo_cambio() -> TipoCambioSbs:
    return TipoCambioSbs(
        fecha=date(2026, 8, 17),
        compra=Decimal("3.798000"),
        venta=Decimal("3.802000"),
        fecha_consulta=datetime(2026, 8, 17, 9, 15, 0),
    )


def test_insertar_sbs_no_menciona_dbo_en_el_sql_emitido():
    cursor = _FakeCursor()

    insertar_sbs(cursor, _tipo_cambio())

    assert "dbo." not in cursor.sentencia.lower()
    assert "fact.tipocambio" in cursor.sentencia.lower()


def test_insertar_sbs_fija_origen_sbs_de_forma_hardcodeada_sin_parametro():
    cursor = _FakeCursor()
    tc = _tipo_cambio()

    insertar_sbs(cursor, tc)

    assert "'sbs'" in cursor.sentencia.lower()
    # 4 parametros: Fecha, Compra, Venta, FechaConsulta — Origen NO viaja como parametro.
    assert cursor.parametros == (tc.fecha, tc.compra, tc.venta, tc.fecha_consulta)


def test_insertar_sbs_retorna_true_en_insercion_exitosa():
    cursor = _FakeCursor()

    assert insertar_sbs(cursor, _tipo_cambio()) is True


def test_insertar_sbs_retorna_false_cuando_ya_existe_la_fila_integrity_error():
    cursor = _FakeCursor(lanzar_integrity_error=True)

    assert insertar_sbs(cursor, _tipo_cambio()) is False
