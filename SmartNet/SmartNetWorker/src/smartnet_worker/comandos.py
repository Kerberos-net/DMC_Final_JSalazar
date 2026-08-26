"""Nucleo puro de ruteo `Tipo -> handler` del consumidor de `fact.CommandQueue` (BACKLOG #17, Fase
4, design.md D5). Ningun SQL vive aqui -- `construir_registro` solo arma el diccionario a partir de
callables ya construidos por `cli_command_queue.py` (el unico punto de IO de este consumidor,
mismo patron `REGISTRO_HANDLERS` vacio/inerte de `despacho_outbox.py` para el item anterior)."""

from __future__ import annotations

from collections.abc import Callable, Mapping

TIPO_REPROCESAR_DOCUMENTO = "REPROCESAR_DOCUMENTO"
TIPO_SINCRONIZAR_GMAIL = "SINCRONIZAR_GMAIL"
TIPO_SINCRONIZAR_SBS = "SINCRONIZAR_SBS"
TIPO_RECONECTAR_GOOGLE = "RECONECTAR_GOOGLE"

Handler = Callable[[object], None]


def construir_registro(
    *,
    reprocesar: Handler,
    sincronizar_gmail: Handler,
    sincronizar_sbs: Handler,
    reconectar_google: Handler,
) -> Mapping[str, Handler]:
    return {
        TIPO_REPROCESAR_DOCUMENTO: reprocesar,
        TIPO_SINCRONIZAR_GMAIL: sincronizar_gmail,
        TIPO_SINCRONIZAR_SBS: sincronizar_sbs,
        TIPO_RECONECTAR_GOOGLE: reconectar_google,
    }


def handler_para(tipo: str, registro: Mapping[str, Handler]) -> Handler | None:
    return registro.get(tipo)
