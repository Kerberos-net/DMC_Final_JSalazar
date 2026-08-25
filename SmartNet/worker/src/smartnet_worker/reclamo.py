"""Contrato puro del consumidor de `fact.OutboxEvent`/`fact.OutboxEventIntegracion` (BACKLOG #14,
Fase 4, design.md Decision D6): un `typing.Protocol` estructural, no una ABC ni una clase con
prefijo `I` (Python no tiene esa convencion; el worker usa modulos planos y dataclasses
congeladas, mismo patron que el resto del paquete).

Este modulo NUNCA importa el driver de base de datos: es la mitad "destination-agnostic" del
consumidor. La unica implementacion con la clausula de exclusividad de motor de ADR 0002 (la
unica excepcion declarada al motor SQL Server) vive en el modulo de infraestructura del
consumidor — nada fuera de ese archivo puede depender de esa sintaxis SQL-Server-especifica
(spec.md, "Dispatcher depends only on the interface").

`ARRENDAMIENTO` es la duracion del lease de un lote reclamado (design.md Decision D4, Open
Question 3 resuelta por el dueno): **5 minutos**, dentro del presupuesto de visibilidad de 15
minutos de ADR 0005. Vive aqui, como constante nombrada e importada por `outbox_repo.py` y
referenciada por la prueba de re-reclamo (nunca un literal `300` suelto ni un parametro de
`reclamar()` — la firma del Protocol publicado se mantiene estable)."""

from __future__ import annotations

from collections.abc import Sequence
from dataclasses import dataclass
from datetime import datetime, timedelta
from typing import Final, Protocol, runtime_checkable

ARRENDAMIENTO: Final[timedelta] = timedelta(minutes=5)


@dataclass(frozen=True)
class EventoReclamado:
    """Una fila `fact.OutboxEventIntegracion` reclamada (join con su `fact.OutboxEvent` padre),
    lista para pasar por la guarda de obsolescencia (`guarda_obsolescencia.py`) y, si vigente, el
    despacho (`despacho_outbox.py`)."""

    outbox_event_id: int
    integracion: str
    factura_id: int
    tipo: str
    payload: str
    secuencia: int
    # BACKLOG #17 (design.md D2/D3): cuenta de intentos fallidos previos, necesaria para que
    # `clasificacion_despacho.decidir` calcule backoff/agotamiento. Default 0 preserva la
    # construccion posicional/keyword de las pruebas de #14 que no la pasan.
    intentos: int = 0


@runtime_checkable
class ReclamoDeLote(Protocol):
    """Frontera SQL-Server-especifica del consumidor (el escaneo de exclusividad de motor de
    ADR 0002, ver `outbox_repo.py`) detras de una interfaz estructural. `despacho_outbox.py` y
    `cli_outbox.py` dependen unicamente de este Protocol, nunca del modulo de infraestructura
    directamente en su logica de decision."""

    def reclamar(
        self, destinos: Sequence[str], limite: int, ahora: datetime
    ) -> tuple[EventoReclamado, ...]: ...

    def progreso(self, factura_id: int, destino: str) -> int | None: ...

    def marcar(self, evento_id: int, destino: str, estado: str, ahora: datetime) -> None: ...
