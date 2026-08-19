"""Registro de intentos en `fact.EstadoIntegracion` (design.md, Decision 6/7).

`009_datos_base.sql` siembra una fila por cada integracion listada en `CK_EstadoIntegracion_Nombre`
(`SBS`, `GMAIL`, ...). `nombre` es un parametro obligatorio (`WHERE Nombre = ?`, nunca interpolado)
— un valor por defecto (`nombre='SBS'`) fue rechazado porque esconde en el llamador cual integracion
se esta actualizando (design.md, Decision 7). Este modulo **lanza si `rowcount != 1`** en vez de
caer a un `INSERT`: tanto una fila base ausente (el esquema no se aplico) como un `nombre` fuera del
CHECK producen 0 filas afectadas, y un INSERT silencioso ocultaria cualquiera de los dos problemas.

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
WHERE Nombre = ?
"""

_UPDATE_FALLO = """
UPDATE fact.EstadoIntegracion
SET UltimoIntento = ?, UltimoError = ?, FallosSeguidos = FallosSeguidos + 1
WHERE Nombre = ?
"""


class EstadoIntegracionError(Exception):
    """El UPDATE contra fact.EstadoIntegracion no afecto exactamente una fila — la fila base
    `Nombre=<nombre>` (009_datos_base.sql) probablemente no existe: el esquema no se aplico, o
    `nombre` no es uno de los valores de `CK_EstadoIntegracion_Nombre`."""


def registrar_exito(cursor, nombre: str, instante: datetime) -> None:
    cursor.execute(_UPDATE_EXITO, instante, instante, nombre)
    _verificar_una_fila_afectada(cursor, nombre)


def registrar_fallo(cursor, nombre: str, instante: datetime, error: str) -> None:
    error_truncado = str(error)[:_MAX_ULTIMO_ERROR_LEN]
    cursor.execute(_UPDATE_FALLO, instante, error_truncado, nombre)
    _verificar_una_fila_afectada(cursor, nombre)


def _verificar_una_fila_afectada(cursor, nombre: str) -> None:
    if cursor.rowcount != 1:
        raise EstadoIntegracionError(
            f"El UPDATE de fact.EstadoIntegracion afecto {cursor.rowcount} filas, se esperaba "
            f"exactamente 1 (Nombre='{nombre}') — revisar que 009_datos_base.sql se haya aplicado "
            "y que 'nombre' este en CK_EstadoIntegracion_Nombre."
        )
