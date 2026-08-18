from datetime import UTC, datetime

import pytest

from smartnet_worker.estado_integracion import (
    EstadoIntegracionError,
    registrar_exito,
    registrar_fallo,
)


class _FakeCursor:
    def __init__(self, *, rowcount: int = 1):
        self.rowcount = rowcount
        self.sentencia: str | None = None
        self.parametros: tuple = ()

    def execute(self, sentencia: str, *parametros):
        self.sentencia = sentencia
        self.parametros = parametros


def test_registrar_exito_emite_update_filtrado_por_nombre_sbs():
    cursor = _FakeCursor()
    instante = datetime(2026, 8, 17, 9, 15, 0, tzinfo=UTC)

    registrar_exito(cursor, instante)

    assert "update" in cursor.sentencia.lower()
    assert "nombre = 'sbs'" in cursor.sentencia.lower()
    assert instante in cursor.parametros


def test_registrar_exito_lanza_estado_integracion_error_si_rowcount_no_es_uno():
    cursor = _FakeCursor(rowcount=0)
    instante = datetime(2026, 8, 17, 9, 15, 0, tzinfo=UTC)

    with pytest.raises(EstadoIntegracionError):
        registrar_exito(cursor, instante)


def test_registrar_fallo_emite_update_filtrado_por_nombre_sbs():
    cursor = _FakeCursor()
    instante = datetime(2026, 8, 17, 9, 15, 0, tzinfo=UTC)

    registrar_fallo(cursor, instante, "SBS no respondio")

    assert "update" in cursor.sentencia.lower()
    assert "nombre = 'sbs'" in cursor.sentencia.lower()


def test_registrar_fallo_trunca_ultimo_error_a_2000_caracteres():
    cursor = _FakeCursor()
    instante = datetime(2026, 8, 17, 9, 15, 0, tzinfo=UTC)
    error_largo = "x" * 5000

    registrar_fallo(cursor, instante, error_largo)

    parametros_str = [p for p in cursor.parametros if isinstance(p, str)]
    assert len(parametros_str) == 1
    assert len(parametros_str[0]) == 2000


def test_registrar_fallo_lanza_estado_integracion_error_si_rowcount_no_es_uno():
    cursor = _FakeCursor(rowcount=2)
    instante = datetime(2026, 8, 17, 9, 15, 0, tzinfo=UTC)

    with pytest.raises(EstadoIntegracionError):
        registrar_fallo(cursor, instante, "boom")


def test_instante_se_pasa_siempre_como_parametro_nunca_datetime_now():
    cursor = _FakeCursor()
    instante_fijo = datetime(2020, 1, 1, tzinfo=UTC)

    registrar_exito(cursor, instante_fijo)

    assert instante_fijo in cursor.parametros
