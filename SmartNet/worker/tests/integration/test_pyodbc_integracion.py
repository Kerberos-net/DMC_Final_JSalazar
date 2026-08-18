"""Pruebas de integracion reales (marker `integracion`) — pyodbc real como `usr_worker` contra
una base efimera con el esquema versionado aplicado (design.md, Testing Strategy / tasks.md 3.9).

NOTA DE EJECUCION (ver README.md, "Limitaciones conocidas"): ejecutadas y en verde en este entorno
contra una instancia SQL Server 2025 local real, con el esquema completo aplicado via
`SmartNet.Db.Runner` y un LOGIN `usr_worker` efimero real (3/3 passed).
"""

from __future__ import annotations

from datetime import UTC, date, datetime
from decimal import Decimal

import pyodbc
import pytest

from smartnet_worker.estado_integracion import registrar_exito
from smartnet_worker.sbs import TipoCambioSbs
from smartnet_worker.tipo_cambio_repo import insertar_sbs

pytestmark = pytest.mark.integracion


def _tipo_cambio_de_hoy() -> TipoCambioSbs:
    return TipoCambioSbs(
        fecha=date.today(),
        compra=Decimal("3.798000"),
        venta=Decimal("3.802000"),
        fecha_consulta=datetime.now(UTC),
    )


def test_insertar_sbs_real_inserta_la_fila_de_hoy(worker_db):
    tc = _tipo_cambio_de_hoy()

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        cursor = conexion.cursor()
        resultado = insertar_sbs(cursor, tc)
        conexion.commit()

    assert resultado is True

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        fila = conexion.cursor().execute(
            "SELECT COUNT(*) FROM fact.TipoCambio WHERE Fecha = ? AND Origen = 'SBS'", tc.fecha
        ).fetchone()

    assert fila[0] == 1


def test_insertar_sbs_duplicado_para_la_misma_fecha_devuelve_false(worker_db):
    tc = _tipo_cambio_de_hoy()

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        cursor = conexion.cursor()
        insertar_sbs(cursor, tc)
        conexion.commit()

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        cursor = conexion.cursor()
        resultado = insertar_sbs(cursor, tc)
        conexion.commit()

    assert resultado is False


def test_registrar_exito_actualiza_exactamente_una_fila_de_estado_integracion(worker_db):
    instante = datetime.now(UTC)

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        cursor = conexion.cursor()
        registrar_exito(cursor, instante)
        conexion.commit()

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        fila = conexion.cursor().execute(
            "SELECT COUNT(*) FROM fact.EstadoIntegracion "
            "WHERE Nombre = 'SBS' AND UltimoExito IS NOT NULL"
        ).fetchone()

    assert fila[0] == 1
