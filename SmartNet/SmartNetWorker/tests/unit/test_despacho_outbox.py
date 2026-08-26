"""Suite de `despacho_outbox.py` (BACKLOG #14, Fase 4, tarea 4.3) — `ReclamoDeLote` falso, sin
DB, sin `pyodbc`. Cubre: el registro vacio por defecto (#14 no tiene handlers), la guarda de
obsolescencia corriendo ANTES del handler (design.md D5), y que `marcar` reciba exactamente el
estado terminal esperado."""

from __future__ import annotations

from datetime import UTC, datetime

from smartnet_worker.despacho_outbox import (
    ESTADO_COMPLETADO,
    ESTADO_ERROR,
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


# --- clasificacion del handler (BACKLOG #17, Fase 2, tasks.md 2.4) -------------------------------


class _FakeRegistroDeFallo:
    def __init__(self):
        self.llamadas: list[tuple] = []

    def registrar(self, evento_id, integracion, resultado, mensaje, instante):
        self.llamadas.append((evento_id, integracion, resultado, mensaje, instante))


def test_handler_que_lanza_sin_registro_de_fallo_propaga_la_excepcion():
    # default None -> comportamiento de #14 preservado byte-por-byte: sin `registro_fallo`, la
    # excepcion del handler se propaga (cli_outbox._procesar_evento la captura y hace rollback).
    reclamo = _FakeReclamo(progreso=None)

    def _handler(evento):
        raise ValueError("boom")

    try:
        despachar_evento(reclamo, _evento(), ahora=_AHORA, registro={"DRIVE": _handler})
        raise AssertionError("se esperaba que la excepcion se propagara")
    except ValueError as error:
        assert str(error) == "boom"

    assert not any(llamada[0] == "marcar" for llamada in reclamo.llamadas)


def test_handler_que_lanza_con_registro_de_fallo_clasifica_y_no_propaga():
    reclamo = _FakeReclamo(progreso=None)
    registro_fallo = _FakeRegistroDeFallo()

    def _handler(evento):
        raise ValueError("boom")

    resultado = despachar_evento(
        reclamo,
        _evento(),
        ahora=_AHORA,
        registro={"DRIVE": _handler},
        registro_fallo=registro_fallo,
    )

    assert resultado == ESTADO_ERROR
    assert len(registro_fallo.llamadas) == 1
    evento_id, integracion, resultado_despacho, mensaje, instante = registro_fallo.llamadas[0]
    assert evento_id == 1
    assert integracion == "DRIVE"
    assert resultado_despacho.estado == "ERROR"
    assert mensaje == "boom"
    assert instante == _AHORA
    # sin registro_fallo.registrar el estado terminal no pasa por `reclamo.marcar` -- ese estado
    # ('ERROR' con Clasificacion/ProximoIntentoEn) lo escribe `outbox_repo.marcar_fallo`, no
    # `reclamo.marcar` (que solo conoce Estado/ActualizadoEn, tarea 4.6 de #14).
    assert not any(llamada[0] == "marcar" for llamada in reclamo.llamadas)


def test_handler_exitoso_no_invoca_registro_de_fallo():
    reclamo = _FakeReclamo(progreso=None)
    registro_fallo = _FakeRegistroDeFallo()

    resultado = despachar_evento(
        reclamo,
        _evento(),
        ahora=_AHORA,
        registro={"DRIVE": lambda evento: None},
        registro_fallo=registro_fallo,
    )

    assert resultado == ESTADO_COMPLETADO
    assert registro_fallo.llamadas == []
