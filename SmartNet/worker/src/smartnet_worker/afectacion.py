"""Modulo puro: calculo de `AfectacionMixta` (REGLAS.md §8, BACKLOG #6).

Ni red, ni disco, ni DB, ni reloj (ADR 0019). Una factura mixta no tiene representacion posible en
la tabla de datos extraidos (`Afectacion` es un solo campo de cabecera, `FacturaDetalle` no existe):
el extractor recorre las lineas del XML UBL y cuenta CODIGOS DISTINTOS de afectacion, no lineas --
`['10', '10']` es una sola afectacion repetida dos veces, no dos afectaciones.
"""

from __future__ import annotations

from collections.abc import Sequence


def calcular_afectacion_mixta(codigos: Sequence[str]) -> bool | None:
    """`>1` codigo distinto -> `True` (rechazo 409, REGLAS.md §8). Exactamente `1` -> `False`
    (verificada). Ninguno -> `None` (sin XML, no verificada -- este caso lo produce el llamador
    cuando no hay comprobante XML, pasando una secuencia vacia)."""
    distintos = set(codigos)
    if len(distintos) > 1:
        return True
    if len(distintos) == 1:
        return False
    return None
