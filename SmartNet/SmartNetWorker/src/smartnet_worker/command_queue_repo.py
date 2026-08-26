"""Unico modulo del consumidor de `fact.CommandQueue` con `READPAST` (ADR 0002; BACKLOG #17, Fase
4, design.md D5) -- mismo patron que `outbox_repo.py`: `SET NOCOUNT ON` primero (mismo hallazgo de
pyodbc/multiples-result-sets documentado ahi), `UPDATE TOP (?) ... OUTPUT ... WITH (READPAST,
UPDLOCK, ROWLOCK)`, lease via `DATEADD(SECOND, ?, ?)` reusando el `ARRENDAMIENTO` importado
(`reclamo.py`) -- nunca un literal de segundos suelto.

Semantica: at-least-once con efectos idempotentes, no at-most-once (design.md D5) -- un comando
`PENDIENTE` o `EN_PROCESO` con `ProximoIntentoEn` vencido es elegible de nuevo; `marcar_reintento`
lo devuelve a `PENDIENTE` con backoff, `marcar_completado`/`marcar_error` son los dos estados
terminales."""

from __future__ import annotations

from collections.abc import Sequence
from dataclasses import dataclass
from datetime import datetime

from smartnet_worker.reclamo import ARRENDAMIENTO

_RECLAMAR_TEMPLATE = """
SET NOCOUNT ON;

DECLARE @reclamados TABLE (CommandQueueId BIGINT);

UPDATE TOP (?) cq SET Estado = 'EN_PROCESO', ProximoIntentoEn = DATEADD(SECOND, ?, ?)
OUTPUT inserted.CommandQueueId INTO @reclamados
FROM fact.CommandQueue AS cq WITH (READPAST, UPDLOCK, ROWLOCK)
WHERE cq.Tipo IN ({marcadores})
  AND (cq.Estado = 'PENDIENTE' OR (cq.Estado = 'EN_PROCESO' AND cq.ProximoIntentoEn <= ?));

SELECT r.CommandQueueId, cq.Tipo, cq.Referencia, cq.Payload, cq.Intentos, cq.CorrelationId
FROM @reclamados r
JOIN fact.CommandQueue cq ON cq.CommandQueueId = r.CommandQueueId;
"""

_MARCAR = "UPDATE fact.CommandQueue SET Estado = ? WHERE CommandQueueId = ?"

_MARCAR_REINTENTO = """
UPDATE fact.CommandQueue
SET Estado = 'PENDIENTE', Intentos = Intentos + 1, ProximoIntentoEn = ?
WHERE CommandQueueId = ?
"""


@dataclass(frozen=True)
class ComandoReclamado:
    command_queue_id: int
    tipo: str
    referencia: int | None
    payload: str
    intentos: int
    correlation_id: str


class CommandQueueRepo:
    def __init__(self, cursor):
        self._cursor = cursor

    def reclamar(
        self, tipos: Sequence[str], limite: int, ahora: datetime
    ) -> tuple[ComandoReclamado, ...]:
        if not tipos:
            return ()
        marcadores = ", ".join("?" for _ in tipos)
        sql = _RECLAMAR_TEMPLATE.format(marcadores=marcadores)
        segundos_lease = int(ARRENDAMIENTO.total_seconds())
        parametros = (limite, segundos_lease, ahora, *tipos, ahora)
        self._cursor.execute(sql, *parametros)
        return tuple(
            ComandoReclamado(
                command_queue_id=fila[0],
                tipo=fila[1],
                referencia=fila[2],
                payload=fila[3],
                intentos=fila[4],
                correlation_id=fila[5],
            )
            for fila in self._cursor.fetchall()
        )

    def marcar_completado(self, command_queue_id: int) -> None:
        self._cursor.execute(_MARCAR, "COMPLETADO", command_queue_id)

    def marcar_error(self, command_queue_id: int) -> None:
        self._cursor.execute(_MARCAR, "ERROR", command_queue_id)

    def marcar_reintento(self, command_queue_id: int, *, proximo_intento_en: datetime) -> None:
        self._cursor.execute(_MARCAR_REINTENTO, proximo_intento_en, command_queue_id)
