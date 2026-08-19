"""Configuracion del worker — la unica fuente de variables de entorno y constantes de red.

Sin credencial ni cadena de conexion por defecto en el codigo (design.md, Decision 5): la unica
forma de obtener la cadena de conexion es la variable de entorno
`SMARTNET_WORKER_ODBC_CONNECTION`, igual regla que `RunnerOptions.SMARTNET_DB_CONNECTION` del lado
.NET (SmartNet.Db.Runner).
"""

from __future__ import annotations

import os

ODBC_CONNECTION_ENV_VAR = "SMARTNET_WORKER_ODBC_CONNECTION"

# URL publica, no un secreto: sin este valor el CLI no sabria que pagina scrapear. La cadena de
# conexion (la parte con credenciales) nunca vive aqui.
SBS_TIPO_CAMBIO_URL = "https://www.sbs.gob.pe/app/pp/SISTIP_PORTAL/Paginas/Publicacion/TipoCambioPromedio.aspx"

# Explicito para que un cuelgue de la SBS no deje el proceso vivo indefinidamente (design.md,
# Threat Matrix — "red + credenciales").
HTTP_TIMEOUT_SECONDS = 10


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
