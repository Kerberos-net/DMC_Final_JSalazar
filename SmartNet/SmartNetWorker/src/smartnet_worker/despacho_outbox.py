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
la clasificacion TRANSITORIO/DIFERIBLE/PERMANENTE de #17).

Item #17 (design.md D2): `handler(evento)` queda envuelto en `except BaseException`. El
`RegistroDeFallo` inyectado es el unico punto de decision sobre si un fallo se clasifica o se deja
propagar: con `registro_fallo=None` (default) la excepcion se relanza tal cual -- el comportamiento
del item anterior (`cli_outbox._procesar_evento` la captura, hace rollback y la cuenta) queda
preservado byte-por-byte. Con un `registro_fallo` inyectado, la excepcion NUNCA se propaga: se
clasifica (`clasificacion_despacho.decidir`), se persiste via `registro_fallo.registrar` y
`despachar_evento` devuelve `ESTADO_ERROR` -- el estado terminal completo (Estado/Intentos/
UltimoError/Clasificacion/ProximoIntentoEn) lo escribe la implementacion inyectada del Protocol
(fuera de este modulo), nunca `reclamo.marcar`, que solo conoce Estado/ActualizadoEn.
"""

from __future__ import annotations

from collections.abc import Callable, Mapping
from datetime import datetime
from typing import Protocol, runtime_checkable

from smartnet_worker.clasificacion_despacho import ResultadoDespacho, decidir
from smartnet_worker.guarda_obsolescencia import VerdictoObsolescencia, evaluar
from smartnet_worker.reclamo import EventoReclamado, ReclamoDeLote

ESTADO_COMPLETADO = "COMPLETADO"
ESTADO_OBSOLETO = "OBSOLETO"
ESTADO_ERROR = "ERROR"

REGISTRO_HANDLERS: Mapping[str, Callable[[EventoReclamado], None]] = {}


@runtime_checkable
class RegistroDeFallo(Protocol):
    """Frontera de persistencia del fallo (design.md D2) -- Protocol estructural, mismo patron que
    `ReclamoDeLote`. La implementacion real (`outbox_repo.OutboxRepo.marcar_fallo`) vive en el
    modulo de infraestructura SQL-Server-especifico; este modulo nunca la importa directamente."""

    def registrar(
        self,
        evento_id: int,
        integracion: str,
        resultado: ResultadoDespacho,
        mensaje: str,
        instante: datetime,
    ) -> None: ...


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
    registro_fallo: RegistroDeFallo | None = None,
) -> str:
    """Un evento ya reclamado: guarda de obsolescencia -> (si vigente) handler -> marcar estado
    terminal. Devuelve el estado escrito (`'COMPLETADO'`, `'OBSOLETO'` o, si `registro_fallo` esta
    inyectado y el handler lanzo, `'ERROR'`) para que el orquestador (`cli_outbox.py`) pueda
    contarlos sin releer la fila."""
    progreso = reclamo.progreso(evento.factura_id, evento.integracion)
    veredicto = evaluar(evento.secuencia, progreso)

    if veredicto is VerdictoObsolescencia.OBSOLETO:
        reclamo.marcar(evento.outbox_event_id, evento.integracion, ESTADO_OBSOLETO, ahora)
        return ESTADO_OBSOLETO

    handler = registro.get(evento.integracion)
    if handler is not None:
        try:
            handler(evento)
        except BaseException as error:  # noqa: BLE001 -- clasificar, no silenciar (ADR 0010).
            if registro_fallo is None:
                raise
            resultado = decidir(error, evento.intentos, ahora)
            registro_fallo.registrar(
                evento.outbox_event_id, evento.integracion, resultado, str(error), ahora
            )
            return ESTADO_ERROR

    reclamo.marcar(evento.outbox_event_id, evento.integracion, ESTADO_COMPLETADO, ahora)
    return ESTADO_COMPLETADO
