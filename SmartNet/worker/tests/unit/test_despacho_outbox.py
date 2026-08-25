"""Suite de `despacho_outbox.py` (BACKLOG #14, Fase 4, tarea 4.3) — `ReclamoDeLote` falso, sin
DB, sin `pyodbc`. Cubre: el registro vacio por defecto (#14 no tiene handlers), la guarda de
obsolescencia corriendo ANTES del handler (design.md D5), y que `marcar` reciba exactamente el
estado terminal esperado."""

from __future__ import annotations

from datetime import UTC, datetime

from smartnet_worker.despacho_outbox import (
    ESTADO_COMPLETADO,
    ESTADO_OBSOLETO,
    REGISTRO_HANDLERS,
    despachar_evento,
    destinos_registrados,
)
from smartnet_worker.reclamo import EventoReclamado

_AHORA = datetime(2026, 8, 24, 12, 0, 0, tzinfo=UTC)


class _FakeReclamo:
    def __init__(self, *, progreso: int | None):
        self._progreso = progreso
        self.llamadas: list[tuple] = []

    def reclamar(self, destinos, limite, ahora):
        raise AssertionError("despachar_evento no debe llamar a reclamar().")

    def progreso(self, factura_id, destino):
        self.llamadas.append(("progreso", factura_id, destino))
        return self._progreso

    def marcar(self, evento_id, destino, estado, ahora):
        self.llamadas.append(("marcar", evento_id, destino, estado, ahora))


def _evento(secuencia: int = 6, integracion: str = "DRIVE") -> EventoReclamado:
    return EventoReclamado(
        outbox_event_id=1,
        integracion=integracion,
        factura_id=100,
        tipo="FACTURA_VALIDADA",
        payload='{"version":1}',
        secuencia=secuencia,
    )


def test_registro_de_handlers_esta_vacio_por_defecto():
    assert REGISTRO_HANDLERS == {}
    assert destinos_registrados() == ()


def test_destinos_registrados_expone_las_claves_del_registro_inyectado():
    registro = {"DRIVE": lambda evento: None, "SHEETS": lambda evento: None}
    assert set(destinos_registrados(registro)) == {"DRIVE", "SHEETS"}


def test_evento_obsoleto_marca_obsoleto_y_nunca_invoca_el_handler():
    reclamo = _FakeReclamo(progreso=6)  # secuencia (6) no supera el progreso (6) -> OBSOLETO
    invocaciones: list[EventoReclamado] = []
    registro = {"DRIVE": invocaciones.append}

    resultado = despachar_evento(reclamo, _evento(secuencia=6), ahora=_AHORA, registro=registro)

    assert resultado == ESTADO_OBSOLETO
    assert invocaciones == [], "El handler no debe invocarse para un evento OBSOLETO."
    assert ("marcar", 1, "DRIVE", ESTADO_OBSOLETO, _AHORA) in reclamo.llamadas


def test_evento_vigente_invoca_el_handler_y_marca_completado():
    reclamo = _FakeReclamo(progreso=5)  # secuencia (6) supera el progreso (5) -> VIGENTE
    invocaciones: list[EventoReclamado] = []
    evento = _evento(secuencia=6)
    registro = {"DRIVE": invocaciones.append}

    resultado = despachar_evento(reclamo, evento, ahora=_AHORA, registro=registro)

    assert resultado == ESTADO_COMPLETADO
    assert invocaciones == [evento]
    assert ("marcar", 1, "DRIVE", ESTADO_COMPLETADO, _AHORA) in reclamo.llamadas


def test_evento_vigente_sin_handler_registrado_igual_marca_completado():
    reclamo = _FakeReclamo(progreso=None)

    resultado = despachar_evento(reclamo, _evento(), ahora=_AHORA, registro={})

    assert resultado == ESTADO_COMPLETADO


def test_guarda_corre_antes_que_el_handler():
    orden: list[str] = []

    class _ReclamoConOrden(_FakeReclamo):
        def progreso(self, factura_id, destino):
            orden.append("progreso")
            return super().progreso(factura_id, destino)

    def _handler(evento):
        orden.append("handler")

    despachar_evento(
        _ReclamoConOrden(progreso=None), _evento(), ahora=_AHORA, registro={"DRIVE": _handler}
    )

    assert orden == ["progreso", "handler"]
