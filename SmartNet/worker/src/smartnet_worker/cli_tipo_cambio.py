"""Punto de entrada del scraper SBS — el UNICO modulo del paquete que hace IO (red, DB, reloj).

Orquesta: `requests.get` (con timeout explicito) -> `parse_tipo_cambio` (puro) -> `insertar_sbs` +
`registrar_exito` en una transaccion; cualquier fallo (red, parseo o DB) se registra con
`registrar_fallo` en una transaccion propia, despues de un rollback de la primera (design.md,
Decision 6: "una falla se loguea en su propia transaccion tras rollback").

Sin scheduler, sin polling, sin reintentos — un solo run, deferido a #5 (design.md, Non-Goals).
"""

from __future__ import annotations

import sys
from datetime import UTC, datetime

import pyodbc
import requests

from smartnet_worker import config
from smartnet_worker.estado_integracion import registrar_exito, registrar_fallo
from smartnet_worker.sbs import ParseoSbsError, parse_tipo_cambio
from smartnet_worker.tipo_cambio_repo import insertar_sbs


def ejecutar() -> int:
    """Corre un ciclo completo del scraper. Devuelve 0 en exito, 1 en fallo — pensado para
    `sys.exit`, estilo proceso de un solo run (mismo patron que `SmartNet.Db.Runner.Program.Run`).
    """
    instante = datetime.now(UTC)
    connection_string = config.obtener_connection_string()

    try:
        respuesta = requests.get(config.SBS_TIPO_CAMBIO_URL, timeout=config.HTTP_TIMEOUT_SECONDS)
        respuesta.raise_for_status()
        tipo_cambio = parse_tipo_cambio(respuesta.text)
    except (requests.RequestException, ParseoSbsError) as error:
        return _registrar_fallo_en_transaccion_propia(connection_string, instante, str(error))

    conexion = pyodbc.connect(connection_string)
    try:
        cursor = conexion.cursor()
        insertar_sbs(cursor, tipo_cambio)
        registrar_exito(cursor, instante)
        conexion.commit()
        return 0
    except Exception as error:  # noqa: BLE001 — punto de entrada del proceso: todo fallo se loguea.
        conexion.rollback()
        return _registrar_fallo_en_transaccion_propia(connection_string, instante, str(error))
    finally:
        conexion.close()


def _registrar_fallo_en_transaccion_propia(
    connection_string: str, instante: datetime, error: str
) -> int:
    conexion = pyodbc.connect(connection_string)
    try:
        cursor = conexion.cursor()
        registrar_fallo(cursor, instante, error)
        conexion.commit()
    finally:
        conexion.close()
    return 1


def main() -> None:
    sys.exit(ejecutar())


if __name__ == "__main__":
    main()
