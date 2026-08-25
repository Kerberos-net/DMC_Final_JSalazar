"""Efectos del notificador (BACKLOG #17, design.md D4) -- Telegram primero, correo de respaldo si
Telegram lanza, ambos intentos logueados via `estado_integracion.registrar_exito`/`registrar_fallo`
(ambos runtimes tienen SELECT/INSERT/UPDATE sobre `fact.EstadoIntegracion`, 008:136-137) para que
un envio de Telegram fallido sea visible aunque el respaldo por correo tenga exito.

`CanalDeAviso` es un Protocol estructural (mismo patron que `ReclamoDeLote`/`RegistroDeFallo`):
`enviar(mensaje)` lanza en caso de fallo, nunca devuelve un booleano -- `notificar` decide el
siguiente canal por la excepcion, no por un valor de retorno que un canal real pudiera olvidar
propagar."""

from __future__ import annotations

from collections.abc import Sequence
from datetime import datetime
from email.message import EmailMessage
from smtplib import SMTP
from typing import Protocol, runtime_checkable

import requests

from smartnet_worker import config
from smartnet_worker.estado_integracion import registrar_exito, registrar_fallo


@runtime_checkable
class CanalDeAviso(Protocol):
    nombre: str  # 'TELEGRAM' | 'CORREO' -- clave de fact.EstadoIntegracion.Nombre.

    def enviar(self, mensaje: str) -> None: ...


class TelegramCanal:
    """Bot API de Telegram (`sendMessage`), un unico chat global (D4 -- "sin ruteo por
    integracion/severidad"). `bot_token` viene de `config.obtener_credenciales_telegram_json`;
    `chat_id` de `fact.Configuracion` (`TELEGRAM.DESTINO_CHAT_ID`, `configuracion_repo.obtener`)."""

    nombre = "TELEGRAM"

    def __init__(self, bot_token: str, chat_id: str):
        self._bot_token = bot_token
        self._chat_id = chat_id

    def enviar(self, mensaje: str) -> None:
        url = f"https://api.telegram.org/bot{self._bot_token}/sendMessage"
        respuesta = requests.post(
            url,
            json={"chat_id": self._chat_id, "text": mensaje},
            timeout=config.HTTP_TIMEOUT_SECONDS,
        )
        respuesta.raise_for_status()


class CorreoCanal:
    """SMTP de respaldo (D4). `host`/`port`/`usuario`/`password`/`remitente` vienen de
    `config.obtener_credenciales_smtp_json`; `destinatarios` de `fact.Configuracion`
    (`CORREO.DESTINATARIOS`, `configuracion_repo.obtener_destinatarios_correo`)."""

    nombre = "CORREO"

    def __init__(
        self,
        host: str,
        port: int,
        usuario: str,
        password: str,
        remitente: str,
        destinatarios: Sequence[str],
    ):
        self._host = host
        self._port = port
        self._usuario = usuario
        self._password = password
        self._remitente = remitente
        self._destinatarios = tuple(destinatarios)

    def enviar(self, mensaje: str) -> None:
        correo = EmailMessage()
        correo["Subject"] = "SmartNet -- alerta de despacho"
        correo["From"] = self._remitente
        correo["To"] = ", ".join(self._destinatarios)
        correo.set_content(mensaje)

        with SMTP(self._host, self._port, timeout=config.HTTP_TIMEOUT_SECONDS) as smtp:
            smtp.starttls()
            smtp.login(self._usuario, self._password)
            smtp.send_message(correo)


def notificar(
    canales: Sequence[CanalDeAviso], mensaje: str, instante: datetime, cursor
) -> None:
    """Intenta cada canal EN ORDEN hasta el primer exito; cada intento (exitoso o no) se loguea de
    inmediato. No hay reintento aqui -- eso es responsabilidad de `clasificacion_despacho` sobre el
    propio evento del outbox, no de este modulo."""
    for canal in canales:
        try:
            canal.enviar(mensaje)
        except Exception as error:  # noqa: BLE001 -- cualquier fallo de canal cae al respaldo.
            registrar_fallo(cursor, canal.nombre, instante, str(error))
            continue
        registrar_exito(cursor, canal.nombre, instante)
        return
