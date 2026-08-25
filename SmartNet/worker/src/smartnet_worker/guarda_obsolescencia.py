"""Guarda de obsolescencia (BACKLOG #14, Fase 4, design.md Decision D5) — un veredicto puro,
sin I/O, que corre ANTES de cualquier handler de destino. Compara la `Secuencia` del evento
reclamado contra el progreso ya registrado en `fact.OutboxEventIntegracion` para ese mismo
`FacturaId`/destino (spec.md, "Obsolescence guard precedes handler dispatch").

Deliberadamente NO es una excepcion (`EventoObsoleto` fue la alternativa rechazada en D5): el
item #17 clasifica **excepciones lanzadas por un handler** en TRANSITORIO/DIFERIBLE/PERMANENTE, y
`OBSOLETO` debe quedar disjunto de esa clasificacion por TIPO, no por convencion (ADR 0010:
`OBSOLETO` nunca es un error ni dispara una alerta). `evaluar` nunca lanza."""

from __future__ import annotations

from enum import Enum, auto


class VerdictoObsolescencia(Enum):
    VIGENTE = auto()
    OBSOLETO = auto()


def evaluar(secuencia: int, progreso: int | None) -> VerdictoObsolescencia:
    """`Obsoleto` cuando la secuencia reclamada NO supera el progreso ya registrado (ADR 0004:
    "no supera la registrada"). Sin progreso previo (`None`), el evento siempre es `Vigente`."""
    if progreso is not None and secuencia <= progreso:
        return VerdictoObsolescencia.OBSOLETO
    return VerdictoObsolescencia.VIGENTE
