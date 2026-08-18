"""Registro de intentos en `fact.EstadoIntegracion` (design.md, Decision 6).

`009_datos_base.sql` ya siembra la fila `Nombre='SBS'`; este modulo la actualiza con `UPDATE ...
WHERE Nombre='SBS'` y **lanza si `rowcount != 1`** en vez de caer a un `INSERT`: una fila base
ausente significa que el esquema no se aplico, y un INSERT silencioso ocultaria ese problema.

`instante` siempre llega como parametro, nunca `datetime.now()` — de lo contrario estas funciones
dejarian de ser deterministas para pruebas (mismo principio que `FechaConsulta` en
`SqlTipoCambioRepository` del lado .NET).
"""

from __future__ import annotations

from datetime import datetime

_MAX_ULTIMO_ERROR_LEN = 2000

_UPDATE_EXITO = """
UPDATE fact.EstadoIntegracion
SET UltimoIntento = ?, UltimoExito = ?, FallosSeguidos = 0
WHERE Nombre = 'SBS'
"""

_UPDATE_FALLO = """
UPDATE fact.EstadoIntegracion
SET UltimoIntento = ?, UltimoError = ?, FallosSeguidos = FallosSeguidos + 1
WHERE Nombre = 'SBS'
"""


class EstadoIntegracionError(Exception):
    """El UPDATE contra fact.EstadoIntegracion no afecto exactamente una fila — la fila base
    `Nombre='SBS'` (009_datos_base.sql) probablemente no existe: el esquema no se aplico."""


def registrar_exito(cursor, instante: datetime) -> None:
    cursor.execute(_UPDATE_EXITO, instante, instante)
    _verificar_una_fila_afectada(cursor)


def registrar_fallo(cursor, instante: datetime, error: str) -> None:
    error_truncado = str(error)[:_MAX_ULTIMO_ERROR_LEN]
    cursor.execute(_UPDATE_FALLO, instante, error_truncado)
    _verificar_una_fila_afectada(cursor)


def _verificar_una_fila_afectada(cursor) -> None:
    if cursor.rowcount != 1:
        raise EstadoIntegracionError(
            f"El UPDATE de fact.EstadoIntegracion afecto {cursor.rowcount} filas, se esperaba "
            "exactamente 1 (Nombre='SBS') — revisar que 009_datos_base.sql se haya aplicado."
        )
