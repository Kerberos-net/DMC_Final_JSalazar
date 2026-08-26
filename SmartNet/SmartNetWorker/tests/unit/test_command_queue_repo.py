"""RED primero (BACKLOG #17, Fase 4, tasks.md 4.1): `smartnet_worker.command_queue_repo` todavia
no existe. Cursor falso, mismo patron que `test_outbox_repo.py`: SQL/parametros exactos,
`READPAST` unicamente en `reclamar`, `SET NOCOUNT ON` antes del `DECLARE` (mismo hallazgo de
`outbox_repo.py` contra el driver real), lease via `DATEADD(SECOND, ...)` reusando
`reclamo.ARRENDAMIENTO` (nunca un literal 300 suelto)."""

from __future__ import annotations

from datetime import UTC, datetime

from smartnet_worker.command_queue_repo import CommandQueueRepo
from smartnet_worker.reclamo import ARRENDAMIENTO

_AHORA = datetime(2026, 8, 24, 12, 0, 0, tzinfo=UTC)


class _FakeCursor:
    def __init__(self, *, filas: list[tuple] | None = None):
        self.llamadas: list[tuple[str, tuple]] = []
        self._filas = filas or []

    def execute(self, sentencia: str, *parametros):
        self.llamadas.append((sentencia, parametros))

    def fetchall(self):
        return self._filas


def test_reclamar_usa_readpast_updlock_rowlock_y_set_nocount_on():
    cursor = _FakeCursor()
    repo = CommandQueueRepo(cursor)

    repo.reclamar(("REPROCESAR_DOCUMENTO",), 10, _AHORA)

    sentencia, _ = cursor.llamadas[0]
    sql = sentencia.lower()
    assert "readpast" in sql
    assert "updlock" in sql
    assert "rowlock" in sql
    assert "fact.commandqueue" in sql
    assert "dbo." not in sql
    assert sql.index("set nocount on") < sql.index("declare")


def test_reclamar_aplica_el_lease_via_dateadd_second_no_literal():
    cursor = _FakeCursor()
    repo = CommandQueueRepo(cursor)

    repo.reclamar(("REPROCESAR_DOCUMENTO",), 10, _AHORA)

    sentencia, parametros = cursor.llamadas[0]
    assert "dateadd(second" in sentencia.lower()
    segundos_lease = int(ARRENDAMIENTO.total_seconds())
    assert segundos_lease in parametros
    assert "300" not in sentencia


def test_reclamar_selecciona_pendiente_o_en_proceso_con_lease_vencido():
    cursor = _FakeCursor()
    repo = CommandQueueRepo(cursor)

    repo.reclamar(("REPROCESAR_DOCUMENTO",), 10, _AHORA)

    sql = cursor.llamadas[0][0].lower()
    assert "'pendiente'" in sql
    assert "'en_proceso'" in sql


def test_reclamar_con_tipos_vacios_no_ejecuta_sql():
    cursor = _FakeCursor()
    repo = CommandQueueRepo(cursor)

    assert repo.reclamar((), 10, _AHORA) == ()
    assert cursor.llamadas == []


def test_reclamar_mapea_filas_a_comandoreclamado():
    filas = [(1, "REPROCESAR_DOCUMENTO", 100, '{"a":1}', 0, "11111111-1111-1111-1111-111111111111")]
    cursor = _FakeCursor(filas=filas)
    repo = CommandQueueRepo(cursor)

    resultado = repo.reclamar(("REPROCESAR_DOCUMENTO",), 10, _AHORA)

    assert len(resultado) == 1
    comando = resultado[0]
    assert comando.command_queue_id == 1
    assert comando.tipo == "REPROCESAR_DOCUMENTO"
    assert comando.referencia == 100
    assert comando.payload == '{"a":1}'
    assert comando.intentos == 0


def test_marcar_completado_escribe_solo_estado():
    cursor = _FakeCursor()
    repo = CommandQueueRepo(cursor)

    repo.marcar_completado(1)

    sentencia, parametros = cursor.llamadas[0]
    sql = sentencia.lower()
    assert "update fact.commandqueue" in sql
    assert "set estado = ?" in sql
    assert parametros == ("COMPLETADO", 1)


def test_marcar_error_terminal_escribe_solo_estado():
    cursor = _FakeCursor()
    repo = CommandQueueRepo(cursor)

    repo.marcar_error(1)

    sentencia, parametros = cursor.llamadas[0]
    assert parametros == ("ERROR", 1)


def test_marcar_reintento_vuelve_a_pendiente_e_incrementa_intentos():
    cursor = _FakeCursor()
    repo = CommandQueueRepo(cursor)

    repo.marcar_reintento(1, proximo_intento_en=_AHORA)

    sentencia, parametros = cursor.llamadas[0]
    sql = sentencia.lower()
    assert "intentos = intentos + 1" in sql
    assert "'pendiente'" in sql
    assert parametros == (_AHORA, 1)
