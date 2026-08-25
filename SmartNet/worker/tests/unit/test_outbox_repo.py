"""Suite de `outbox_repo.py` (BACKLOG #14, Fase 4, tarea 4.4) — cursor falso, patron
`test_procesamiento_repo.py`/`test_inbox_event_repo.py`: SQL y parametros exactos, `fact.`
calificado, `READPAST` presente unicamente en `reclamar`. Cubre las tres operaciones de
`ReclamoDeLote`: `reclamar` (lease vía `DATEADD(SECOND, ...)`, tabla de variable + join),
`progreso` (`MAX(Secuencia)` sobre filas `COMPLETADO`) y `marcar` (SOLO `Estado`/`ActualizadoEn`,
nunca `Intentos`/`UltimoError` — tarea 4.6)."""

from __future__ import annotations

from datetime import UTC, datetime

from smartnet_worker.outbox_repo import OutboxRepo
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

    def fetchone(self):
        return self._filas[0] if self._filas else None


# --- reclamar ----------------------------------------------------------------------------------


def test_reclamar_usa_readpast_updlock_rowlock():
    cursor = _FakeCursor()
    repo = OutboxRepo(cursor)

    repo.reclamar(("DRIVE", "SHEETS"), 50, _AHORA)

    sentencia, _ = cursor.llamadas[0]
    sql = sentencia.lower()
    assert "readpast" in sql
    assert "updlock" in sql
    assert "rowlock" in sql
    assert "fact.outboxeventintegracion" in sql
    assert "fact.outboxevent" in sql
    assert "dbo." not in sql


def test_reclamar_aplica_el_lease_via_dateadd_second_no_literal():
    cursor = _FakeCursor()
    repo = OutboxRepo(cursor)

    repo.reclamar(("DRIVE",), 50, _AHORA)

    sentencia, parametros = cursor.llamadas[0]
    assert "dateadd(second" in sentencia.lower()
    segundos_lease = int(ARRENDAMIENTO.total_seconds())
    assert segundos_lease in parametros
    assert "300" not in sentencia  # nunca un literal de segundos en el SQL


def test_reclamar_parametriza_limite_y_destinos_sin_concatenar_valores():
    cursor = _FakeCursor()
    repo = OutboxRepo(cursor)

    repo.reclamar(("DRIVE", "SHEETS"), 25, _AHORA)

    sentencia, parametros = cursor.llamadas[0]
    assert "top (?)" in sentencia.lower()
    assert 25 in parametros
    assert "DRIVE" in parametros
    assert "SHEETS" in parametros
    assert "DRIVE" not in sentencia
    assert "SHEETS" not in sentencia


def test_reclamar_activa_set_nocount_on_antes_del_declare():
    # BACKLOG #14, Fase 5 (tasks.md 5.3, ADR 0019 nivel 2): sin `SET NOCOUNT ON`, pyodbc contra un
    # driver real trata el mensaje "N rows affected" del UPDATE como un result-set vacio
    # intercalado antes del SELECT final -- `cursor.fetchall()` inmediatamente despues de
    # `execute()` lanza `pyodbc.ProgrammingError: No results.` El fake cursor de este archivo no
    # reproduce ese comportamiento (por eso el resto de esta suite nunca lo detecto); esta prueba
    # fija la presencia literal de la sentencia para que no se pueda perder en un refactor futuro.
    cursor = _FakeCursor()
    repo = OutboxRepo(cursor)

    repo.reclamar(("DRIVE",), 50, _AHORA)

    sentencia, _ = cursor.llamadas[0]
    sql_normalizado = sentencia.lower().replace("\n", " ")
    assert "set nocount on" in sql_normalizado
    assert sql_normalizado.index("set nocount on") < sql_normalizado.index("declare @reclamadas")


def test_reclamar_con_destinos_vacios_no_ejecuta_sql():
    cursor = _FakeCursor()
    repo = OutboxRepo(cursor)

    resultado = repo.reclamar((), 50, _AHORA)

    assert resultado == ()
    assert cursor.llamadas == []


def test_reclamar_mapea_las_filas_a_eventoreclamado():
    filas = [(1, "DRIVE", 100, "FACTURA_VALIDADA", '{"version":1}', 7)]
    cursor = _FakeCursor(filas=filas)
    repo = OutboxRepo(cursor)

    resultado = repo.reclamar(("DRIVE",), 50, _AHORA)

    assert len(resultado) == 1
    evento = resultado[0]
    assert evento.outbox_event_id == 1
    assert evento.integracion == "DRIVE"
    assert evento.factura_id == 100
    assert evento.tipo == "FACTURA_VALIDADA"
    assert evento.payload == '{"version":1}'
    assert evento.secuencia == 7


# --- progreso ------------------------------------------------------------------------------------


def test_progreso_calcula_max_secuencia_sobre_filas_completado():
    cursor = _FakeCursor(filas=[(5,)])
    repo = OutboxRepo(cursor)

    resultado = repo.progreso(100, "DRIVE")

    assert resultado == 5
    sentencia, parametros = cursor.llamadas[0]
    sql = sentencia.lower()
    assert "max(oe.secuencia)" in sql
    assert "completado" in sql
    assert "fact.outboxevent" in sql
    assert "fact.outboxeventintegracion" in sql
    assert parametros == (100, "DRIVE")


def test_progreso_sin_filas_devuelve_none():
    cursor = _FakeCursor(filas=[])
    repo = OutboxRepo(cursor)

    assert repo.progreso(100, "DRIVE") is None


# --- marcar --------------------------------------------------------------------------------------


def test_marcar_solo_escribe_estado_y_actualizadoen():
    cursor = _FakeCursor()
    repo = OutboxRepo(cursor)

    repo.marcar(1, "DRIVE", "OBSOLETO", _AHORA)

    sentencia, parametros = cursor.llamadas[0]
    sql = sentencia.lower()
    assert "update fact.outboxeventintegracion" in sql
    assert "set estado = ?, actualizadoen = ?" in sql
    assert "intentos" not in sql
    assert "ultimoerror" not in sql
    assert parametros == ("OBSOLETO", _AHORA, 1, "DRIVE")


def test_marcar_completado_tampoco_toca_intentos_ni_ultimoerror():
    cursor = _FakeCursor()
    repo = OutboxRepo(cursor)

    repo.marcar(2, "SHEETS", "COMPLETADO", _AHORA)

    sentencia, _ = cursor.llamadas[0]
    sql = sentencia.lower()
    assert "intentos" not in sql
    assert "ultimoerror" not in sql
