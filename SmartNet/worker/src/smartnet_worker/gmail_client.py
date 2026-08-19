"""Cliente IO-only de la API de Gmail (BACKLOG #5, design.md Decision 1).

`ClienteGmail` es el `cursor` de Gmail: un metodo por llamada de API, sin ramas ni parseo — la misma
disciplina que separa `tipo_cambio_repo.py` (IO) de `sbs.py` (decision). Todo el parseo vive en
`gmail.py`; este modulo solo transporta bytes/JSON entre la API real y el resto del paquete.

Resolucion de credenciales (design.md Decision 2): `Credentials.from_authorized_user_info` sobre el
JSON leido por `config.obtener_credenciales_gmail_json`, alcance `config.GMAIL_SCOPES`. El token de
acceso vive solo en memoria y muere con el proceso — un CLI de un solo run nunca lo escribe de
vuelta al entorno.
"""

from __future__ import annotations

import base64

from google.auth.transport.requests import Request
from google.oauth2.credentials import Credentials
from googleapiclient.discovery import build

_USUARIO = "me"


class ClienteGmail:
    """Envoltorio delgado sobre `googleapiclient.discovery.build("gmail", "v1")`. Ninguno de sus
    metodos decide nada: reciben lo que el llamador ya decidio (ids, nombres, paginas) y devuelven
    la respuesta cruda de la API."""

    def __init__(self, credenciales_json: dict, scopes: list[str]) -> None:
        credenciales = Credentials.from_authorized_user_info(credenciales_json, scopes=scopes)
        credenciales.refresh(Request())
        self._servicio = build("gmail", "v1", credentials=credenciales)

    def resolver_etiquetas(self) -> dict[str, str]:
        """`users.labels.list` — devuelve `{nombre: id}` de todas las etiquetas del buzon. El
        llamador (design.md Decision 3) es quien decide que hacer si un nombre esperado no aparece
        aqui; este metodo no valida nada."""
        respuesta = self._servicio.users().labels().list(userId=_USUARIO).execute()
        return {etiqueta["name"]: etiqueta["id"] for etiqueta in respuesta.get("labels", [])}

    def buscar_mensajes(self, consulta: str) -> list[str]:
        """`users.messages.list` paginado (sigue `nextPageToken` hasta que la API deja de
        devolverlo) — devuelve solo los ids, nunca el contenido del mensaje."""
        ids: list[str] = []
        pagina_token: str | None = None
        while True:
            respuesta = (
                self._servicio.users()
                .messages()
                .list(userId=_USUARIO, q=consulta, pageToken=pagina_token)
                .execute()
            )
            ids.extend(mensaje["id"] for mensaje in respuesta.get("messages", []))
            pagina_token = respuesta.get("nextPageToken")
            if not pagina_token:
                return ids

    def obtener_mensaje(self, mensaje_id: str) -> dict:
        """`users.messages.get(format=full)` — el JSON crudo que `gmail.parsear_mensaje` consume."""
        return (
            self._servicio.users()
            .messages()
            .get(userId=_USUARIO, id=mensaje_id, format="full")
            .execute()
        )

    def obtener_adjunto(self, mensaje_id: str, attachment_id: str) -> bytes:
        """`users.messages.attachments.get` — decodifica el `data` base64url-sin-padding que
        devuelve la API y regresa los bytes crudos del adjunto."""
        respuesta = (
            self._servicio.users()
            .messages()
            .attachments()
            .get(userId=_USUARIO, messageId=mensaje_id, id=attachment_id)
            .execute()
        )
        return base64.urlsafe_b64decode(respuesta["data"])

    def aplicar_etiqueta(self, mensaje_id: str, etiqueta_id: str) -> None:
        """`users.messages.modify` agregando una etiqueta — nunca borra ni mueve a papelera
        (ADR 0017; ver `test_no_dbo_structural.py`, que escanea que ningun modulo mencione
        `.delete(`/`.trash(`)."""
        self._servicio.users().messages().modify(
            userId=_USUARIO,
            id=mensaje_id,
            body={"addLabelIds": [etiqueta_id]},
        ).execute()
