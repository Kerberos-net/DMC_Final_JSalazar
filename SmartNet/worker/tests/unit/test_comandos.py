"""RED primero (BACKLOG #17, Fase 4, tasks.md 4.3): `smartnet_worker.comandos` todavia no existe.
Nucleo puro (ADR 0019): `construir_registro`/`handler_para` son solo wiring -- NINGUN SQL vive
aqui; los handlers concretos (que si tocan `fact.Procesamiento`/`fact.EstadoIntegracion`) se
inyectan desde `cli_command_queue.py`, el unico punto de IO de este consumidor."""

from __future__ import annotations

from smartnet_worker.comandos import (
    TIPO_RECONECTAR_GOOGLE,
    TIPO_REPROCESAR_DOCUMENTO,
    TIPO_SINCRONIZAR_GMAIL,
    TIPO_SINCRONIZAR_SBS,
    construir_registro,
    handler_para,
)


def test_construir_registro_mapea_los_cuatro_tipos():
    registro = construir_registro(
        reprocesar=lambda c: None,
        sincronizar_gmail=lambda c: None,
        sincronizar_sbs=lambda c: None,
        reconectar_google=lambda c: None,
    )

    assert set(registro) == {
        TIPO_REPROCESAR_DOCUMENTO,
        TIPO_SINCRONIZAR_GMAIL,
        TIPO_SINCRONIZAR_SBS,
        TIPO_RECONECTAR_GOOGLE,
    }


def test_handler_para_devuelve_el_handler_registrado():
    marcador = object()

    def _reprocesar(comando):
        return marcador

    registro = construir_registro(
        reprocesar=_reprocesar,
        sincronizar_gmail=lambda c: None,
        sincronizar_sbs=lambda c: None,
        reconectar_google=lambda c: None,
    )

    handler = handler_para(TIPO_REPROCESAR_DOCUMENTO, registro)
    assert handler is not None
    assert handler(None) is marcador


def test_handler_para_tipo_no_registrado_devuelve_none():
    assert handler_para("TIPO_INEXISTENTE", {}) is None
