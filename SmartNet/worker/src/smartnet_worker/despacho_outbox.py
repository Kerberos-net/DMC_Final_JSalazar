"""Despacho destination-agnostic del consumidor (BACKLOG #14, Fase 4, design.md Data Flow /
File Changes: "Destination-agnostic dispatch + empty registry"). Depende UNICAMENTE del Protocol
`ReclamoDeLote` (`reclamo.py`) — este modulo nunca importa el modulo de infraestructura SQL del
consumidor ni su driver de base de datos (spec.md, "Dispatcher depends only on the interface";
mecanicamente verificado por `test_no_dbo_structural.py`, ADR 0002).

`REGISTRO_HANDLERS` esta vacio en #14 a proposito (design.md, Data Flow: "Registro de destinos
vacio en #14 -> nada se reclama, las filas se acumulan PENDIENTE para #15/#16"); items #15/#16
lo llenan con un handler por `Integracion` ('DRIVE', 'SHEETS').

`despachar_evento` aplica la guarda de obsolescencia (D5) ANTES de cualquier handler: un veredicto
`OBSOLETO` marca el estado terminal y retorna sin invocar el registro (ADR 0010 — nunca pasa por
la clasificacion TRANSITORIO/DIFERIBLE/PERMANENTE de #17)."""

from __future__ import annotations

from collections.abc import Callable, Mapping
from datetime import datetime

from smartnet_worker.guarda_obsolescencia import VerdictoObsolescencia, evaluar
from smartnet_worker.reclamo import EventoReclamado, ReclamoDeLote

ESTADO_COMPLETADO = "COMPLETADO"
ESTADO_OBSOLETO = "OBSOLETO"

REGISTRO_HANDLERS: Mapping[str, Callable[[EventoReclamado], None]] = {}


def destinos_registrados(
    registro: Mapping[str, Callable[[EventoReclamado], None]] = REGISTRO_HANDLERS,
) -> tuple[str, ...]:
    """Las claves del registro son, a la vez, los `Integracion` que `ReclamoDeLote.reclamar`
    debe pedir — un registro vacio en #14 produce una tupla vacia y, por construccion, ningun
    reclamo (design.md Data Flow)."""
    return tuple(registro)


def despachar_evento(
    reclamo: ReclamoDeLote,
    evento: EventoReclamado,
    *,
    ahora: datetime,
    registro: Mapping[str, Callable[[EventoReclamado], None]] = REGISTRO_HANDLERS,
) -> str:
    """Un evento ya reclamado: guarda de obsolescencia -> (si vigente) handler -> marcar estado
    terminal. Devuelve el estado escrito (`'COMPLETADO'` u `'OBSOLETO'`) para que el orquestador
    (`cli_outbox.py`) pueda contarlos sin releer la fila."""
    progreso = reclamo.progreso(evento.factura_id, evento.integracion)
    veredicto = evaluar(evento.secuencia, progreso)

    if veredicto is VerdictoObsolescencia.OBSOLETO:
        reclamo.marcar(evento.outbox_event_id, evento.integracion, ESTADO_OBSOLETO, ahora)
        return ESTADO_OBSOLETO

    handler = registro.get(evento.integracion)
    if handler is not None:
        handler(evento)
    reclamo.marcar(evento.outbox_event_id, evento.integracion, ESTADO_COMPLETADO, ahora)
    return ESTADO_COMPLETADO
