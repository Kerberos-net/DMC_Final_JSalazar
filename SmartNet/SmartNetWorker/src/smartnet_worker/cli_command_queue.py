"""Punto de entrada del consumidor de `fact.CommandQueue` (BACKLOG #17, Fase 4, design.md D5) --
mismo patron single-run que `cli_outbox.py`: su propio schedule externo, dos transacciones cortas
por ciclo (una de reclamo para todo el lote, una por comando para el handler + estado terminal).

ADR 0003: este consumidor toca UNICAMENTE la tabla de contrato del CommandQueue, la tabla de
Procesamiento (privada de Python) y la de EstadoIntegracion (compartida) -- nunca la tabla de
facturas ni ningun catalogo externo (verificado mecanicamente por `test_no_dbo_structural.py`).

`SINCRONIZAR_GMAIL`/`SINCRONIZAR_SBS` quedan sin cablear a proposito en este item: los CLI
dedicados `smartnet-gmail`/`smartnet-tipo-cambio` (items #4/#5) ya cubren esos flujos con su propio
schedule; conectarlos al CommandQueue es trabajo futuro fuera del alcance ratificado de #17 -- el
handler inyectado aqui lanza `NotImplementedError` explicito en vez de fingir un exito."""

from __future__ import annotations

import sys
from collections.abc import Callable
from datetime import UTC, datetime

import pyodbc

from smartnet_worker import comandos, config
from smartnet_worker.clasificacion_despacho import decidir
from smartnet_worker.command_queue_repo import ComandoReclamado, CommandQueueRepo
from smartnet_worker.errores import Clasificacion

_LIMITE_LOTE = 50

_TIPOS_RECLAMADOS = (
    comandos.TIPO_REPROCESAR_DOCUMENTO,
    comandos.TIPO_SINCRONIZAR_GMAIL,
    comandos.TIPO_SINCRONIZAR_SBS,
    comandos.TIPO_RECONECTAR_GOOGLE,
)


def ejecutar(
    *,
    conectar: Callable[[str], object] = pyodbc.connect,
    ahora: Callable[[], datetime] | None = None,
) -> int:
    connection_string = config.obtener_connection_string()
    momento = (ahora or (lambda: datetime.now(UTC)))()

    reclamados = _reclamar_lote(conectar, connection_string, momento)

    errores_run: list[str] = []
    for comando in reclamados:
        try:
            _procesar_comando(comando, conectar, connection_string, momento)
        except Exception as error:  # noqa: BLE001 -- aislamiento por comando (design.md D5).
            errores_run.append(f"{comando.command_queue_id}:{comando.tipo}: {error}")

    return 1 if errores_run else 0


def _reclamar_lote(
    conectar: Callable[[str], object], connection_string: str, ahora: datetime
) -> tuple[ComandoReclamado, ...]:
    conexion = conectar(connection_string)
    try:
        cursor = conexion.cursor()
        repo = CommandQueueRepo(cursor)
        reclamados = repo.reclamar(_TIPOS_RECLAMADOS, _LIMITE_LOTE, ahora)
        conexion.commit()
        return reclamados
    except Exception:
        conexion.rollback()
        raise
    finally:
        conexion.close()


def _procesar_comando(
    comando: ComandoReclamado,
    conectar: Callable[[str], object],
    connection_string: str,
    ahora: datetime,
) -> None:
    conexion = conectar(connection_string)
    try:
        cursor = conexion.cursor()
        repo = CommandQueueRepo(cursor)
        registro = _construir_registro(cursor)
        handler = comandos.handler_para(comando.tipo, registro)
        try:
            if handler is not None:
                handler(comando)
            repo.marcar_completado(comando.command_queue_id)
        except BaseException as error:
            resultado = decidir(error, comando.intentos, ahora)
            if resultado.clasificacion == Clasificacion.PERMANENTE or resultado.agotado:
                repo.marcar_error(comando.command_queue_id)
            elif resultado.proximo_intento_en is not None:
                repo.marcar_reintento(
                    comando.command_queue_id, proximo_intento_en=resultado.proximo_intento_en
                )
            conexion.commit()
            raise
        conexion.commit()
    except Exception:
        conexion.rollback()
        raise
    finally:
        conexion.close()


def _construir_registro(cursor):
    return comandos.construir_registro(
        reprocesar=lambda comando: _reprocesar_documento(cursor, comando),
        sincronizar_gmail=_sincronizar_no_cableado,
        sincronizar_sbs=_sincronizar_no_cableado,
        reconectar_google=lambda comando: _reconectar_google(cursor),
    )


def _reprocesar_documento(cursor, comando: ComandoReclamado) -> None:
    if comando.referencia is None:
        raise ValueError("REPROCESAR_DOCUMENTO requiere Referencia (ProcesamientoId).")
    cursor.execute(
        "UPDATE fact.Procesamiento SET Estado = 'PENDIENTE' WHERE ProcesamientoId = ?",
        comando.referencia,
    )


def _reconectar_google(cursor) -> None:
    cursor.execute("UPDATE fact.EstadoIntegracion SET FallosSeguidos = 0 WHERE Nombre = 'GMAIL'")


def _sincronizar_no_cableado(comando: ComandoReclamado) -> None:
    raise NotImplementedError(
        f"{comando.tipo} todavia no esta conectado al consumidor de CommandQueue (BACKLOG #17) -- "
        "usar los CLI dedicados smartnet-gmail/smartnet-tipo-cambio (items #4/#5) hasta cablearlo."
    )


def main() -> None:
    sys.exit(ejecutar())


if __name__ == "__main__":
    main()
