"""ADR 0019 level-2 contract tests (BACKLOG #14, Fase 5, tasks.md 5.3-5.6, design.md Testing
Strategy "Contrato N2") -- against the REAL schema applied by `SmartNet.Db.Runner` and the REAL
GRANT/DENY matrix of `008_usuarios_y_permisos.sql`, using the `worker_db` fixture's two real
instance LOGINs (`usr_api`/`usr_worker`, `conftest.py`, task 5.2).

Scope, matching design.md's table row by row:
  5.3 Bidireccional: `usr_api` INSERTs event + child rows -> `usr_worker` claims/marks (via
      `outbox_repo.OutboxRepo`, real production code, not a hand-rolled SQL string) -> `usr_api`
      reads back the terminal state.
  5.4 Permission matrix, Python side only (.NET side is `PermissionMatrixTests.cs:254-309` --
      NOT duplicated here, per design.md's explicit note): `usr_worker` INSERT on
      `OutboxEventIntegracion` denied; `usr_api` UPDATE on the same table denied; `usr_worker`
      write to `fact.Factura` denied (ADR 0003, particion de datos -- reaffirmed on the outbox
      path specifically, distinct from the generic BACKLOG #7 assertion of the same DENY).
  5.5 Lease is 5 minutes (D4/ARRENDAMIENTO): a claimed row is invisible to a second claim at
      `ahora + 4 min` and reclaimable again at `ahora + 6 min` -- asserted against `ARRENDAMIENTO`
      itself, never a bare `300`/`5` literal.
  5.6 `READPAST` actually skips: two concurrent `pyodbc` connections claim the same batch
      simultaneously; the claimed sets are disjoint and neither thread blocks on the other.
"""

from __future__ import annotations

import threading
from datetime import UTC, datetime, timedelta

import pyodbc
import pytest

from smartnet_worker.outbox_repo import OutboxRepo
from smartnet_worker.reclamo import ARRENDAMIENTO

pytestmark = pytest.mark.integracion


def _insertar_factura_como_usr_api(cursor) -> int:
    fila = cursor.execute(
        "INSERT INTO fact.Factura (ProveedorCodigo, TipoComprobante, TotalOrig, Moneda, FechaEmision) "
        "OUTPUT INSERTED.FacturaId "
        "VALUES ('P00000', '01', 100.00, 'PEN', '2026-01-01');"
    ).fetchone()
    return fila[0]


def _insertar_outbox_event_como_usr_api(
    cursor, factura_id: int, *, tipo: str = "FACTURA_VALIDADA", payload: str = "{}"
) -> int:
    # OUTPUT INSERTED en el MISMO execute() que el INSERT -- SELECT SCOPE_IDENTITY() en un
    # execute() separado devuelve NULL con pyodbc (gotcha real documentado en worker/README.md,
    # BACKLOG #5: un INSERT parametrizado viaja envuelto en sp_executesql, que cierra su propio
    # scope al retornar).
    fila = cursor.execute(
        "INSERT INTO fact.OutboxEvent (Tipo, FacturaId, Payload, Secuencia) "
        "OUTPUT INSERTED.OutboxEventId "
        "VALUES (?, ?, ?, NEXT VALUE FOR fact.SeqOutbox);",
        tipo,
        factura_id,
        payload,
    ).fetchone()
    return fila[0]


def _insertar_outbox_event_integracion_como_usr_api(
    cursor, outbox_event_id: int, integracion: str = "DRIVE"
) -> None:
    cursor.execute(
        "INSERT INTO fact.OutboxEventIntegracion (OutboxEventId, Integracion) VALUES (?, ?);",
        outbox_event_id,
        integracion,
    )


# ------------------------------------------------------------------------------------------------
# 5.3 -- bidireccional: usr_api inserta, usr_worker reclama y marca, usr_api lee el resultado.
# ------------------------------------------------------------------------------------------------


def test_usr_api_inserta_evento_usr_worker_reclama_y_marca_usr_api_lee_el_resultado(worker_db):
    with pyodbc.connect(worker_db["api_connection_string"]) as api_conn:
        cursor = api_conn.cursor()
        factura_id = _insertar_factura_como_usr_api(cursor)
        outbox_event_id = _insertar_outbox_event_como_usr_api(cursor, factura_id)
        _insertar_outbox_event_integracion_como_usr_api(cursor, outbox_event_id, "DRIVE")
        api_conn.commit()

    ahora = datetime.now(UTC)
    with pyodbc.connect(worker_db["worker_connection_string"]) as worker_conn:
        cursor = worker_conn.cursor()
        reclamados = OutboxRepo(cursor).reclamar(destinos=("DRIVE",), limite=10, ahora=ahora)
        worker_conn.commit()

    assert len(reclamados) == 1
    evento = reclamados[0]
    assert evento.outbox_event_id == outbox_event_id
    assert evento.factura_id == factura_id
    assert evento.integracion == "DRIVE"
    assert evento.tipo == "FACTURA_VALIDADA"
    assert evento.payload == "{}"

    with pyodbc.connect(worker_db["worker_connection_string"]) as worker_conn:
        cursor = worker_conn.cursor()
        OutboxRepo(cursor).marcar(evento.outbox_event_id, evento.integracion, "COMPLETADO", datetime.now(UTC))
        worker_conn.commit()

    with pyodbc.connect(worker_db["api_connection_string"]) as api_conn:
        fila = api_conn.cursor().execute(
            "SELECT Estado FROM fact.OutboxEventIntegracion WHERE OutboxEventId = ? AND Integracion = ?;",
            outbox_event_id,
            "DRIVE",
        ).fetchone()

    assert fila is not None
    assert fila[0] == "COMPLETADO"


# ------------------------------------------------------------------------------------------------
# 5.4 -- matriz de permisos, lado Python (el lado .NET ya esta cubierto por
# PermissionMatrixTests.cs:254-309 -- design.md pide explicitamente no duplicarlo).
# ------------------------------------------------------------------------------------------------


def test_usr_worker_no_puede_insertar_en_outboxeventintegracion(worker_db):
    with pyodbc.connect(worker_db["api_connection_string"]) as api_conn:
        cursor = api_conn.cursor()
        factura_id = _insertar_factura_como_usr_api(cursor)
        outbox_event_id = _insertar_outbox_event_como_usr_api(cursor, factura_id)
        api_conn.commit()

    with pytest.raises(pyodbc.Error):
        with pyodbc.connect(worker_db["worker_connection_string"]) as worker_conn:
            worker_conn.cursor().execute(
                "INSERT INTO fact.OutboxEventIntegracion (OutboxEventId, Integracion) VALUES (?, 'SHEETS');",
                outbox_event_id,
            )
            worker_conn.commit()


def test_usr_api_no_puede_actualizar_outboxeventintegracion(worker_db):
    with pyodbc.connect(worker_db["api_connection_string"]) as api_conn:
        cursor = api_conn.cursor()
        factura_id = _insertar_factura_como_usr_api(cursor)
        outbox_event_id = _insertar_outbox_event_como_usr_api(cursor, factura_id)
        _insertar_outbox_event_integracion_como_usr_api(cursor, outbox_event_id, "DRIVE")
        api_conn.commit()

    with pytest.raises(pyodbc.Error):
        with pyodbc.connect(worker_db["api_connection_string"]) as api_conn:
            api_conn.cursor().execute(
                "UPDATE fact.OutboxEventIntegracion SET Estado = 'COMPLETADO' WHERE OutboxEventId = ?;",
                outbox_event_id,
            )
            api_conn.commit()


def test_usr_worker_no_puede_escribir_en_factura_por_el_camino_outbox(worker_db):
    # Mismo DENY que test_usr_worker_no_puede_escribir_en_factura (BACKLOG #7,
    # test_pyodbc_integracion.py) -- reafirmado aqui porque design.md Testing Strategy lo pide
    # explicitamente como parte de la matriz de permisos del CAMINO outbox (Fase 5, task 5.4): no
    # es una prueba nueva del motor, es la confirmacion de que el mismo DENY (ADR 0003, particion
    # de datos) tambien protege este flujo especifico.
    with pytest.raises(pyodbc.Error):
        with pyodbc.connect(worker_db["worker_connection_string"]) as worker_conn:
            worker_conn.cursor().execute(
                "INSERT INTO fact.Factura (ProveedorCodigo, TipoComprobante, TotalOrig, Moneda, FechaEmision) "
                "VALUES ('P00000', '01', 100.00, 'PEN', '2026-01-01');"
            )
            worker_conn.commit()


# ------------------------------------------------------------------------------------------------
# 5.5 -- lease de 5 minutos (D4/ARRENDAMIENTO): invisible a los 4 min, reclamable a los 6 min.
# ------------------------------------------------------------------------------------------------


def test_lease_hace_invisible_la_fila_reclamada_y_reclamable_tras_vencer(worker_db):
    assert ARRENDAMIENTO == timedelta(minutes=5)  # D4 -- pin explicito, nunca un literal suelto.

    with pyodbc.connect(worker_db["api_connection_string"]) as api_conn:
        cursor = api_conn.cursor()
        factura_id = _insertar_factura_como_usr_api(cursor)
        outbox_event_id = _insertar_outbox_event_como_usr_api(cursor, factura_id)
        _insertar_outbox_event_integracion_como_usr_api(cursor, outbox_event_id, "DRIVE")
        api_conn.commit()

    t0 = datetime(2026, 8, 24, 12, 0, 0, tzinfo=UTC)

    with pyodbc.connect(worker_db["worker_connection_string"]) as worker_conn:
        cursor = worker_conn.cursor()
        primero = OutboxRepo(cursor).reclamar(destinos=("DRIVE",), limite=10, ahora=t0)
        worker_conn.commit()
    assert len(primero) == 1
    assert primero[0].outbox_event_id == outbox_event_id

    # Dentro del lease (t0 + 4 min < t0 + ARRENDAMIENTO): la fila sigue con ProximoIntentoEn en el
    # futuro -- invisible a un segundo reclamo.
    with pyodbc.connect(worker_db["worker_connection_string"]) as worker_conn:
        cursor = worker_conn.cursor()
        segundo = OutboxRepo(cursor).reclamar(
            destinos=("DRIVE",), limite=10, ahora=t0 + timedelta(minutes=4)
        )
        worker_conn.commit()
    assert segundo == ()

    # Fuera del lease (t0 + 6 min > t0 + ARRENDAMIENTO): ProximoIntentoEn ya paso -- reclamable de nuevo.
    with pyodbc.connect(worker_db["worker_connection_string"]) as worker_conn:
        cursor = worker_conn.cursor()
        tercero = OutboxRepo(cursor).reclamar(
            destinos=("DRIVE",), limite=10, ahora=t0 + timedelta(minutes=6)
        )
        worker_conn.commit()
    assert len(tercero) == 1
    assert tercero[0].outbox_event_id == outbox_event_id


# ------------------------------------------------------------------------------------------------
# 5.6 -- READPAST: dos conexiones concurrentes reclaman el mismo lote sin bloquearse, sets disjuntos.
# ------------------------------------------------------------------------------------------------


def test_readpast_dos_conexiones_concurrentes_reclaman_conjuntos_disjuntos_sin_bloqueo(worker_db):
    with pyodbc.connect(worker_db["api_connection_string"]) as api_conn:
        cursor = api_conn.cursor()
        factura_id = _insertar_factura_como_usr_api(cursor)
        ids_insertados = []
        for _ in range(4):
            outbox_event_id = _insertar_outbox_event_como_usr_api(cursor, factura_id)
            _insertar_outbox_event_integracion_como_usr_api(cursor, outbox_event_id, "DRIVE")
            ids_insertados.append(outbox_event_id)
        api_conn.commit()

    ahora = datetime.now(UTC)
    resultados: dict[str, tuple] = {}
    errores: dict[str, BaseException] = {}

    def _reclamar(etiqueta: str) -> None:
        try:
            with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
                cursor = conexion.cursor()
                resultados[etiqueta] = OutboxRepo(cursor).reclamar(
                    destinos=("DRIVE",), limite=2, ahora=ahora
                )
                conexion.commit()
        except BaseException as error:  # noqa: BLE001 -- se reporta en el hilo principal, no se silencia.
            errores[etiqueta] = error

    hilo_a = threading.Thread(target=_reclamar, args=("a",))
    hilo_b = threading.Thread(target=_reclamar, args=("b",))
    hilo_a.start()
    hilo_b.start()
    hilo_a.join(timeout=30)
    hilo_b.join(timeout=30)

    assert not hilo_a.is_alive() and not hilo_b.is_alive(), (
        "READPAST/UPDLOCK/ROWLOCK debio evitar que un hilo bloqueara al otro"
    )
    assert not errores, f"Un hilo de reclamo lanzo un error inesperado: {errores}"

    reclamados_a = {e.outbox_event_id for e in resultados["a"]}
    reclamados_b = {e.outbox_event_id for e in resultados["b"]}
    assert reclamados_a.isdisjoint(reclamados_b), "READPAST no evito que ambos hilos reclamaran la misma fila"
    assert reclamados_a | reclamados_b == set(ids_insertados)
