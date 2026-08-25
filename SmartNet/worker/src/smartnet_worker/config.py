"""Configuracion del worker — la unica fuente de variables de entorno y constantes de red.

Sin credencial ni cadena de conexion por defecto en el codigo (design.md, Decision 5): la unica
forma de obtener la cadena de conexion es la variable de entorno
`SMARTNET_WORKER_ODBC_CONNECTION`, igual regla que `RunnerOptions.SMARTNET_DB_CONNECTION` del lado
.NET (SmartNet.Db.Runner).

Las credenciales de Gmail y la raiz del volumen compartido siguen la misma regla (BACKLOG #5,
design.md Decision 2): un unico valor por variable de entorno, sin default en codigo — un
`RefreshError` o un volumen mal montado deben fallar visiblemente, nunca escribir por accidente en
un lugar no configurado.
"""

from __future__ import annotations

import json
import os

ODBC_CONNECTION_ENV_VAR = "SMARTNET_WORKER_ODBC_CONNECTION"

# Nombre de la unica variable de entorno que porta el JSON completo `authorized_user`
# (client_id, client_secret, refresh_token, token_uri) como un secreto atomico — tres variables
# separadas podrian rotarse de forma mutuamente inconsistente (design.md, Decision 2).
GMAIL_CREDENTIALS_ENV_VAR = "SMARTNET_WORKER_GMAIL_CREDENTIALS"

# Raiz del volumen compartido donde el worker escribe los adjuntos descargados (ADR 0013). El lado
# .NET la lee para servir la descarga; este proceso solo escribe.
STORAGE_ROOT_ENV_VAR = "SMARTNET_WORKER_STORAGE_ROOT"

# Alcance minimo que permite leer mensajes y aplicar la etiqueta de "procesado" (ADR 0015):
# gmail.modify no habilita el flujo de consentimiento interactivo, que es responsabilidad del lado
# .NET (POST /api/integraciones/google/reconectar), fuera de alcance de este item.
GMAIL_SCOPES = ["https://www.googleapis.com/auth/gmail.modify"]

# URL publica, no un secreto: sin este valor el CLI no sabria que pagina scrapear. La cadena de
# conexion (la parte con credenciales) nunca vive aqui.
SBS_TIPO_CAMBIO_URL = "https://www.sbs.gob.pe/app/pp/SISTIP_PORTAL/Paginas/Publicacion/TipoCambioPromedio.aspx"

# Explicito para que un cuelgue de la SBS no deje el proceso vivo indefinidamente (design.md,
# Threat Matrix — "red + credenciales").
HTTP_TIMEOUT_SECONDS = 10

# Ruta opcional al binario Tesseract (BACKLOG #6, design.md Decision 7). A diferencia de la cadena
# de conexion, las credenciales de Gmail y la raiz de almacenamiento, su AUSENCIA es legitima: en
# Linux/CI el binario ya esta en el PATH tras `apt-get install tesseract-ocr`; en Windows no
# (instala en `C:\Program Files\Tesseract-OCR\tesseract.exe`, fuera del PATH por defecto), y ahi es
# donde un operador fija esta variable. No carga ningun secreto ni tiene un "default peligroso".
TESSERACT_CMD_ENV_VAR = "SMARTNET_WORKER_TESSERACT_CMD"

# REGLAS/ADR 0017: OCR local, castellano — 'spa' es el paquete de idioma de Tesseract instalado en
# CI (`tesseract-ocr-spa`) y documentado como prerequisito en README.md.
OCR_IDIOMA = "spa"

# 300 DPI (design.md, Decision 3): balance entre precision de reconocimiento y tiempo de proceso
# para un documento SUNAT tipico; escala = OCR_DPI / 72 (72 DPI es la unidad nativa de PDF).
OCR_DPI = 300

# BACKLOG #17 (design.md D4): mismo patron que GMAIL_CREDENTIALS_ENV_VAR -- un secreto atomico por
# integracion, sin default en codigo. El JSON de Telegram carga {"bot_token": "..."}; el de SMTP
# carga {"host": "...", "port": ..., "usuario": "...", "password": "...", "remitente": "..."}. Las
# NO-secretas (chat id, destinatarios de correo) vienen de fact.Configuracion via
# configuracion_repo.py, nunca de aqui (008:131 le da SELECT a fact_worker sobre esa tabla).
TELEGRAM_CREDENTIALS_ENV_VAR = "SMARTNET_WORKER_TELEGRAM_CREDENTIALS"
SMTP_CREDENTIALS_ENV_VAR = "SMARTNET_WORKER_SMTP_CREDENTIALS"


class ConfiguracionError(Exception):
    """La configuracion requerida del worker (variables de entorno) no esta presente."""


def obtener_connection_string() -> str:
    """Lee la cadena de conexion ODBC desde el entorno. Lanza si no esta definida — nunca hay un
    valor por defecto que pudiera terminar apuntando, por accidente, a una base real."""
    valor = os.environ.get(ODBC_CONNECTION_ENV_VAR)
    if not valor:
        raise ConfiguracionError(
            f"La variable de entorno {ODBC_CONNECTION_ENV_VAR} no esta definida."
        )
    return valor


def obtener_credenciales_gmail_json() -> dict:
    """Lee y parsea el JSON `authorized_user` (client_id, client_secret, refresh_token, token_uri)
    desde `SMARTNET_WORKER_GMAIL_CREDENTIALS`. Lanza `ConfiguracionError` si la variable no esta
    definida o si su contenido no es JSON valido — nunca antes de la primera llamada de red, y
    nunca con un valor por defecto que pudiera esconder una credencial mal rotada."""
    valor = os.environ.get(GMAIL_CREDENTIALS_ENV_VAR)
    if not valor:
        raise ConfiguracionError(
            f"La variable de entorno {GMAIL_CREDENTIALS_ENV_VAR} no esta definida."
        )
    try:
        return json.loads(valor)
    except json.JSONDecodeError as error:
        raise ConfiguracionError(
            f"La variable de entorno {GMAIL_CREDENTIALS_ENV_VAR} no contiene JSON valido."
        ) from error


def obtener_raiz_almacenamiento() -> str:
    """Lee la raiz del volumen compartido donde se escriben los adjuntos descargados. Lanza si no
    esta definida — sin default en codigo, mismo principio que la cadena de conexion (ADR 0013)."""
    valor = os.environ.get(STORAGE_ROOT_ENV_VAR)
    if not valor:
        raise ConfiguracionError(
            f"La variable de entorno {STORAGE_ROOT_ENV_VAR} no esta definida."
        )
    return valor


def obtener_credenciales_telegram_json() -> dict:
    """Lee y parsea el JSON `{"bot_token": "..."}` desde `SMARTNET_WORKER_TELEGRAM_CREDENTIALS`.
    Lanza `ConfiguracionError` si la variable no esta definida o su contenido no es JSON valido --
    mismo contrato que `obtener_credenciales_gmail_json`."""
    valor = os.environ.get(TELEGRAM_CREDENTIALS_ENV_VAR)
    if not valor:
        raise ConfiguracionError(
            f"La variable de entorno {TELEGRAM_CREDENTIALS_ENV_VAR} no esta definida."
        )
    try:
        return json.loads(valor)
    except json.JSONDecodeError as error:
        raise ConfiguracionError(
            f"La variable de entorno {TELEGRAM_CREDENTIALS_ENV_VAR} no contiene JSON valido."
        ) from error


def obtener_credenciales_smtp_json() -> dict:
    """Lee y parsea el JSON `{"host", "port", "usuario", "password", "remitente"}` desde
    `SMARTNET_WORKER_SMTP_CREDENTIALS`. Lanza `ConfiguracionError` si la variable no esta definida
    o su contenido no es JSON valido -- mismo contrato que `obtener_credenciales_gmail_json`."""
    valor = os.environ.get(SMTP_CREDENTIALS_ENV_VAR)
    if not valor:
        raise ConfiguracionError(
            f"La variable de entorno {SMTP_CREDENTIALS_ENV_VAR} no esta definida."
        )
    try:
        return json.loads(valor)
    except json.JSONDecodeError as error:
        raise ConfiguracionError(
            f"La variable de entorno {SMTP_CREDENTIALS_ENV_VAR} no contiene JSON valido."
        ) from error


def obtener_tesseract_cmd() -> str | None:
    """Lee la ruta opcional al binario Tesseract. A diferencia de `obtener_connection_string`/
    `obtener_credenciales_gmail_json`/`obtener_raiz_almacenamiento`, su ausencia es legal y NO
    lanza -- significa 'esperar `tesseract` en el PATH' (design.md, Decision 7)."""
    valor = os.environ.get(TESSERACT_CMD_ENV_VAR)
    return valor or None
