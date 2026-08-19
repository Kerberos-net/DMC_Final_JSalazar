"""Punto de entrada del publicador de `fact.InboxEvent` (BACKLOG #7, WU1) — el UNICO orquestador de
este stage, mismo patron single-run que `cli_gmail.py`/`cli_tipo_cambio.py`/`cli_procesamiento.py`.

Un solo ciclo por invocacion (design.md, Decision D7 — "`cli_inbox.py` es single-run por
invocacion, scheduled externally each minute"): lee `fact.Procesamiento` sin `fact.InboxEvent`
(`inbox_event_repo.listar_no_notificados`) -> por fila, construye el `Payload`
(`payload_inbox`, puro) -> inserta con `inbox_event_repo.insertar_evento`, UNA transaccion propia
por fila, aislada del resto del run (mismo framing que #6's Decision 7 en `cli_procesamiento.py`)
-> un fallo en una fila no aborta el batch.

Design D8: este modulo NO escribe `fact.EstadoIntegracion` — `CK_EstadoIntegracion_Nombre` no
tiene un valor `INBOX` y reutilizar `'WORKER'` enmascararia el heartbeat de #6; una fila que quede
sin notificar se auto-repara en el siguiente ciclo (un minuto despues).
"""

from __future__ import annotations

import sys
from collections.abc import Callable

import pyodbc

from smartnet_worker import config
from smartnet_worker.inbox_event_repo import (
    ProcesamientoNoNotificado,
    insertar_evento,
    listar_no_notificados,
)
from smartnet_worker.payload_inbox import ComprobanteParaEvento, construir_payload

_ESTADO_COMPLETADO = "COMPLETADO"


def ejecutar(*, conectar: Callable[[str], object] = pyodbc.connect) -> int:
    """Corre un ciclo completo de publicacion. Devuelve 0 si ninguna fila fallo, 1 si al menos una
    fallo — pensado para `sys.exit`, mismo patron que `cli_procesamiento.ejecutar`. Un fallo por
    fila se acumula y no aborta el resto del batch (aislamiento por fila, design.md)."""
    connection_string = config.obtener_connection_string()

    conexion_lectura = conectar(connection_string)
    try:
        cursor_lectura = conexion_lectura.cursor()
        pendientes = listar_no_notificados(cursor_lectura)
    finally:
        conexion_lectura.close()

    errores_run: list[str] = []
    for fila in pendientes:
        try:
            _publicar_evento(fila, conectar, connection_string)
        except Exception as error:  # noqa: BLE001 — aislamiento por fila (design.md).
            errores_run.append(f"{fila.procesamiento_id}: {error}")

    return 1 if errores_run else 0


def _publicar_evento(
    fila: ProcesamientoNoNotificado,
    conectar: Callable[[str], object],
    connection_string: str,
) -> None:
    payload = construir_payload(
        estado_procesamiento=fila.estado,
        documento_recibido_id=fila.documento_recibido_id,
        tipo_documento=fila.tipo_documento,
        documento_asociado_id=fila.documento_asociado_id,
        comprobante=_comprobante_desde_fila(fila),
    )
    conexion = conectar(connection_string)
    try:
        cursor = conexion.cursor()
        insertar_evento(cursor, fila.procesamiento_id, payload)
        conexion.commit()
    except Exception:
        conexion.rollback()
        raise
    finally:
        conexion.close()


def _comprobante_desde_fila(fila: ProcesamientoNoNotificado) -> ComprobanteParaEvento | None:
    """El outcome se deriva UNICAMENTE de `Procesamiento.Estado` (spec.md 'Failed processing
    still emits an event'): `#6` nunca escribe `fact.DatosExtraidos` para un documento en
    `Estado='ERROR'`, asi que ese caso se representa como `comprobante=None`, nunca como un objeto
    con todos los campos en `None` (evitaria fabricar un comprobante inexistente)."""
    if fila.estado != _ESTADO_COMPLETADO:
        return None
    return ComprobanteParaEvento(
        tipo_comprobante=fila.tipo_comprobante,
        numero=fila.numero,
        ruc_proveedor=fila.ruc_proveedor,
        nombre_proveedor=fila.nombre_proveedor,
        monto=fila.monto,
        moneda=fila.moneda,
        fecha_emision=fila.fecha_emision,
        campos_no_extraidos=fila.campos_no_extraidos,
        afectacion_mixta=fila.afectacion_mixta,
    )


def main() -> None:
    sys.exit(ejecutar())


if __name__ == "__main__":
    main()
