"""Prueba de integracion (marker `integracion`) del consumidor de `fact.CommandQueue` (BACKLOG
#17, Fase 4, tasks.md 4.6) -- mismo arnes (`conftest.py::worker_db`) que
`test_outbox_contrato_bidireccional.py`: dos conexiones concurrentes reclamando el mismo lote
nunca procesan la misma fila dos veces (spec.md, "Concurrent claims do not double-process")."""

from __future__ import annotations

import threading
import uuid
from datetime import UTC, datetime

import pyodbc
import pytest

from smartnet_worker.command_queue_repo import CommandQueueRepo

pytestmark = pytest.mark.integracion


def _insertar_comando_como_usr_api(cursor, tipo: str) -> int:
    cursor.execute(
        "INSERT INTO fact.CommandQueue (Tipo, Referencia, Payload, CorrelationId) "
        "VALUES (?, NULL, '{}', ?)",
        tipo,
        str(uuid.uuid4()),
    )
    cursor.execute(
        "SELECT CommandQueueId FROM fact.CommandQueue WHERE Tipo = ? ORDER BY CommandQueueId DESC",
        tipo,
    )
    return cursor.fetchone()[0]


def test_readpast_dos_conexiones_concurrentes_reclaman_conjuntos_disjuntos(worker_db):
    with pyodbc.connect(worker_db["api_connection_string"]) as api_conn:
        cursor = api_conn.cursor()
        ids_insertados = [
            _insertar_comando_como_usr_api(cursor, "RECONECTAR_GOOGLE") for _ in range(4)
        ]
        api_conn.commit()

    ahora = datetime.now(UTC)
    resultados: dict[str, tuple] = {}
    errores: dict[str, BaseException] = {}

    def _reclamar(etiqueta: str) -> None:
        try:
            with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
                cursor = conexion.cursor()
                resultados[etiqueta] = CommandQueueRepo(cursor).reclamar(
                    ("RECONECTAR_GOOGLE",), 2, ahora
                )
                conexion.commit()
        except BaseException as error:  # noqa: BLE001 -- se reporta en el hilo principal.
            errores[etiqueta] = error

    hilo_a = threading.Thread(target=_reclamar, args=("a",))
    hilo_b = threading.Thread(target=_reclamar, args=("b",))
    hilo_a.start()
    hilo_b.start()
    hilo_a.join(timeout=30)
    hilo_b.join(timeout=30)

    assert not hilo_a.is_alive() and not hilo_b.is_alive()
    assert not errores, f"Un hilo de reclamo lanzo un error inesperado: {errores}"

    reclamados_a = {c.command_queue_id for c in resultados["a"]}
    reclamados_b = {c.command_queue_id for c in resultados["b"]}
    assert reclamados_a.isdisjoint(reclamados_b)
    assert reclamados_a | reclamados_b == set(ids_insertados)


def test_marcar_reintento_reclama_de_nuevo_tras_vencer_el_lease(worker_db):
    with pyodbc.connect(worker_db["api_connection_string"]) as api_conn:
        cursor = api_conn.cursor()
        command_queue_id = _insertar_comando_como_usr_api(cursor, "REPROCESAR_DOCUMENTO")
        api_conn.commit()

    ahora = datetime.now(UTC)
    with pyodbc.connect(worker_db["worker_connection_string"]) as worker_conn:
        cursor = worker_conn.cursor()
        repo = CommandQueueRepo(cursor)
        primero = repo.reclamar(("REPROCESAR_DOCUMENTO",), 10, ahora)
        assert len(primero) == 1
        repo.marcar_reintento(command_queue_id, proximo_intento_en=ahora)
        worker_conn.commit()

    with pyodbc.connect(worker_db["worker_connection_string"]) as worker_conn:
        cursor = worker_conn.cursor()
        segundo = CommandQueueRepo(cursor).reclamar(("REPROCESAR_DOCUMENTO",), 10, ahora)
        worker_conn.commit()

    assert len(segundo) == 1
    assert segundo[0].command_queue_id == command_queue_id
    assert segundo[0].intentos == 1
