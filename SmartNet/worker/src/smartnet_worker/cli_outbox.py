"""Punto de entrada del consumidor de `fact.OutboxEvent`/`fact.OutboxEventIntegracion` (BACKLOG
#14, Fase 4) — mismo patron single-run que `cli_inbox.py`/`cli_procesamiento.py`, pero con **su
propio** schedule externo, independiente del publicador de `InboxEvent` (#11) y de cualquier otro
componente (design.md Decision D7, spec.md "One-minute independent consumer cadence"):
`smartnet-outbox`, scheduled cada minuto, `smartnet-inbox` scheduled por separado.

Dos transacciones cortas por ciclo (design.md Decision D4 — "dispatch runs outside [the claim
transaction]; a second short transaction writes the terminal state"): (1) una unica transaccion de
reclamo que fija `ProximoIntentoEn` sobre TODO el lote reclamado y libera los locks de inmediato;
(2) por cada evento reclamado, una transaccion propia y aislada (mismo framing de aislamiento por
fila que `cli_inbox._publicar_evento`) que corre la guarda de obsolescencia + el handler (si
aplica) + el marcado del estado terminal. Un fallo en un evento no aborta el resto del lote.

`REGISTRO_HANDLERS` esta vacio en #14 (`despacho_outbox.py`): ningun destino se reclama todavia,
las filas `fact.OutboxEventIntegracion` se acumulan `PENDIENTE` para #15/#16."""

from __future__ import annotations

import sys
from collections.abc import Callable
from datetime import UTC, datetime

import pyodbc

from smartnet_worker import config
from smartnet_worker.despacho_outbox import (
    REGISTRO_HANDLERS,
    despachar_evento,
    destinos_registrados,
)
from smartnet_worker.outbox_repo import OutboxRepo
from smartnet_worker.reclamo import EventoReclamado

_LIMITE_LOTE = 50


def ejecutar(
    *,
    conectar: Callable[[str], object] = pyodbc.connect,
    ahora: Callable[[], datetime] | None = None,
) -> int:
    """Corre un ciclo completo de consumo. Devuelve 0 si ningun evento reclamado fallo, 1 si al
    menos uno fallo — pensado para `sys.exit`, mismo patron que `cli_inbox.ejecutar`."""
    connection_string = config.obtener_connection_string()
    momento = (ahora or (lambda: datetime.now(UTC)))()

    reclamados = _reclamar_lote(conectar, connection_string, momento)

    errores_run: list[str] = []
    for evento in reclamados:
        try:
            _procesar_evento(evento, conectar, connection_string, momento)
        except Exception as error:  # noqa: BLE001 — aislamiento por fila (design.md D4).
            errores_run.append(f"{evento.outbox_event_id}:{evento.integracion}: {error}")

    return 1 if errores_run else 0


def _reclamar_lote(
    conectar: Callable[[str], object], connection_string: str, ahora: datetime
) -> tuple[EventoReclamado, ...]:
    conexion = conectar(connection_string)
    try:
        cursor = conexion.cursor()
        repo = OutboxRepo(cursor)
        reclamados = repo.reclamar(destinos_registrados(), _LIMITE_LOTE, ahora)
        conexion.commit()
        return reclamados
    except Exception:
        conexion.rollback()
        raise
    finally:
        conexion.close()


def _procesar_evento(
    evento: EventoReclamado,
    conectar: Callable[[str], object],
    connection_string: str,
    ahora: datetime,
) -> None:
    conexion = conectar(connection_string)
    try:
        cursor = conexion.cursor()
        repo = OutboxRepo(cursor)
        despachar_evento(repo, evento, ahora=ahora, registro=REGISTRO_HANDLERS)
        conexion.commit()
    except Exception:
        conexion.rollback()
        raise
    finally:
        conexion.close()


def main() -> None:
    sys.exit(ejecutar())


if __name__ == "__main__":
    main()
