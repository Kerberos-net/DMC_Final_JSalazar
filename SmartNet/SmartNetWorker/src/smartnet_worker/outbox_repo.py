"""Unico modulo del consumidor con `READPAST` (ADR 0002 — la unica excepcion declarada al motor
SQL Server; BACKLOG #14, Fase 4, design.md Decision D6). Recibe un `cursor`, mismo patron que
`procesamiento_repo.py`/`documento_repo.py`/`inbox_event_repo.py`; implementa `ReclamoDeLote`
(`reclamo.py`) de forma estructural, sin heredar.

`reclamar` (design.md, Interfaces/Contracts — forma SQL literal): `OUTPUT` no puede referenciar
columnas de una tabla unida, asi que las claves reclamadas caen en una tabla de variable y se
re-seleccionan con el join a `fact.OutboxEvent`. El lease (`ARRENDAMIENTO`, `reclamo.py`, D4) se
aplica con `DATEADD(SECOND, ?, ?)` sobre `ahora` — nunca un literal de segundos suelto en el SQL.
`WITH (READPAST, UPDLOCK, ROWLOCK)` es lo que hace que dos ciclos de reclamo concurrentes no
procesen la misma fila dos veces (spec.md, "Concurrent claims do not double-process a row").

`progreso` calcula `MAX(oe.Secuencia)` sobre `fact.OutboxEvent` unido con las filas
`fact.OutboxEventIntegracion` en `Estado='COMPLETADO'` para el mismo `FacturaId`/`Integracion`
(design.md, Interfaces/Contracts: "Progress = MAX(oe.Secuencia) ... Stale iff progreso is not
None and secuencia <= progreso").

`marcar` escribe UNICAMENTE `Estado`/`ActualizadoEn` — nunca toca `Intentos` ni `UltimoError`
(spec.md, tarea 4.6: el veredicto `OBSOLETO` de la guarda no es un error y no debe alimentar
ningun contador de reintento).

`SET NOCOUNT ON` al inicio de `_RECLAMAR_TEMPLATE` (BACKLOG #14, Fase 5, task 5.3 -- descubierto
contra esquema real, no anticipado en design.md): sin ella, pyodbc trata el mensaje "N rows
affected" del `UPDATE` como un result-set vacio intercalado ANTES del `SELECT` final, y
`cursor.fetchall()` inmediatamente despues de `execute()` lanza
`pyodbc.ProgrammingError: No results.  Previous SQL was not a query.` -- el fake-cursor unitario de
`test_outbox_repo.py` nunca lo detecto porque un fake no reproduce el comportamiento de
multiples-result-sets del driver real; el arnes de contrato N2 (`worker_db`, tasks.md 5.3/5.6) fue
el primero en ejecutar este SQL contra un driver ODBC real."""

from __future__ import annotations

from collections.abc import Sequence
from datetime import datetime

from smartnet_worker.reclamo import ARRENDAMIENTO, EventoReclamado

_RECLAMAR_TEMPLATE = """
SET NOCOUNT ON;

DECLARE @reclamadas TABLE (OutboxEventId BIGINT, Integracion VARCHAR(20));

UPDATE TOP (?) oei SET ProximoIntentoEn = DATEADD(SECOND, ?, ?), ActualizadoEn = ?
OUTPUT inserted.OutboxEventId, inserted.Integracion INTO @reclamadas
FROM fact.OutboxEventIntegracion AS oei WITH (READPAST, UPDLOCK, ROWLOCK)
WHERE oei.Estado = 'PENDIENTE' AND oei.Integracion IN ({marcadores})
  AND (oei.ProximoIntentoEn IS NULL OR oei.ProximoIntentoEn <= ?);

SELECT r.OutboxEventId, r.Integracion, oe.FacturaId, oe.Tipo, oe.Payload, oe.Secuencia,
       oei.Intentos
FROM @reclamadas r
JOIN fact.OutboxEvent oe ON oe.OutboxEventId = r.OutboxEventId
JOIN fact.OutboxEventIntegracion oei
    ON oei.OutboxEventId = r.OutboxEventId AND oei.Integracion = r.Integracion;
"""

_PROGRESO = """
SELECT MAX(oe.Secuencia)
FROM fact.OutboxEventIntegracion oei
JOIN fact.OutboxEvent oe ON oe.OutboxEventId = oei.OutboxEventId
WHERE oe.FacturaId = ? AND oei.Integracion = ? AND oei.Estado = 'COMPLETADO'
"""

_MARCAR = """
UPDATE fact.OutboxEventIntegracion
SET Estado = ?, ActualizadoEn = ?
WHERE OutboxEventId = ? AND Integracion = ?
"""

# BACKLOG #17 (design.md D1/D1b): unico UPDATE que toca `Clasificacion` -- distinto de `_MARCAR`,
# que solo escribe Estado/ActualizadoEn para los estados terminales sin error (COMPLETADO/OBSOLETO,
# tarea 4.6 de #14). `Intentos = Intentos + 1` incrementa server-side; el conteo que
# `clasificacion_despacho.decidir` necesita para el backoff viene de la lectura previa
# (`EventoReclamado.intentos`, `reclamar`), nunca de releer esta fila.
_LEER_CLASIFICACION = """
SELECT Clasificacion FROM fact.OutboxEventIntegracion
WHERE OutboxEventId = ? AND Integracion = ?
"""

_MARCAR_FALLO = """
UPDATE fact.OutboxEventIntegracion
SET Estado = ?, Intentos = Intentos + 1, UltimoError = ?, Clasificacion = ?,
    ProximoIntentoEn = ?, ActualizadoEn = ?
WHERE OutboxEventId = ? AND Integracion = ?
"""

_MAX_MENSAJE_LEN = 2000


class OutboxRepo:
    """Implementacion `pyodbc` de `ReclamoDeLote` (Protocol estructural — no hereda de nada)."""

    def __init__(self, cursor):
        self._cursor = cursor

    def reclamar(
        self, destinos: Sequence[str], limite: int, ahora: datetime
    ) -> tuple[EventoReclamado, ...]:
        if not destinos:
            return ()
        marcadores = ", ".join("?" for _ in destinos)
        sql = _RECLAMAR_TEMPLATE.format(marcadores=marcadores)
        segundos_lease = int(ARRENDAMIENTO.total_seconds())
        parametros = (
            limite,
            segundos_lease,
            ahora,
            ahora,
            *destinos,
            ahora,
        )
        self._cursor.execute(sql, *parametros)
        return tuple(
            EventoReclamado(
                outbox_event_id=fila[0],
                integracion=fila[1],
                factura_id=fila[2],
                tipo=fila[3],
                payload=fila[4],
                secuencia=fila[5],
                intentos=fila[6],
            )
            for fila in self._cursor.fetchall()
        )

    def progreso(self, factura_id: int, destino: str) -> int | None:
        self._cursor.execute(_PROGRESO, factura_id, destino)
        fila = self._cursor.fetchone()
        return fila[0] if fila is not None else None

    def marcar(self, evento_id: int, destino: str, estado: str, ahora: datetime) -> None:
        self._cursor.execute(_MARCAR, estado, ahora, evento_id, destino)

    def leer_clasificacion(self, evento_id: int, destino: str) -> str | None:
        """Lee la `Clasificacion` YA escrita en la fila, antes de sobreescribirla -- el mecanismo
        de dedupe de DIFERIBLE de `politica_notificacion.debe_notificar` (design.md D4)."""
        self._cursor.execute(_LEER_CLASIFICACION, evento_id, destino)
        fila = self._cursor.fetchone()
        return fila[0] if fila is not None else None

    def marcar_fallo(
        self,
        *,
        evento_id: int,
        destino: str,
        clasificacion: str,
        mensaje: str,
        proximo_intento_en: datetime | None,
        ahora: datetime,
    ) -> None:
        """Unico punto de escritura de `Clasificacion` (BACKLOG #17, design.md D1). `mensaje` se
        trunca a `_MAX_MENSAJE_LEN` antes de tocar `UltimoError` -- mismo limite y mismo motivo que
        `estado_integracion.registrar_fallo` (no filtrar payload crudo en la base, design.md Threat
        Matrix)."""
        mensaje_truncado = str(mensaje)[:_MAX_MENSAJE_LEN]
        self._cursor.execute(
            _MARCAR_FALLO,
            "ERROR",
            mensaje_truncado,
            clasificacion,
            proximo_intento_en,
            ahora,
            evento_id,
            destino,
        )
