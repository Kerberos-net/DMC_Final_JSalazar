"""Repositorio de `fact.Email` / `fact.DocumentoRecibido` para el runtime Python — recibe un
`cursor` exactamente igual que `tipo_cambio_repo.py` (design.md, Decision 4).

`insertar_email` es la puerta de idempotencia: `UQ_Email_GmailMessageId` (003) es la que rechaza el
duplicado, este adaptador solo traduce el `IntegrityError` a `None`, nunca hace un `SELECT` previo
(misma disciplina anti-TOCTOU que `insertar_sbs`). `insertar_documento` es simetrico frente a
`UQ_DocumentoRecibido_Email_Hash` (013): dos adjuntos con el mismo contenido en el mismo mensaje son
el mismo documento, un no-op, no un error.
"""

from __future__ import annotations

from datetime import datetime

import pyodbc

from smartnet_worker.gmail import AdjuntoGmail, MensajeGmail

_LONGITUD_MAXIMA_NOMBRE_ARCHIVO = 255

_INSERT_EMAIL = """
INSERT INTO fact.Email (GmailMessageId, Remitente, Asunto, FechaRecepcion, FechaDeteccion, Estado)
OUTPUT INSERTED.EmailId
VALUES (?, ?, ?, ?, ?, 'CANDIDATO')
"""

_INSERT_DOCUMENTO = """
INSERT INTO fact.DocumentoRecibido
    (EmailId, GmailMessageId, NombreArchivo, Extension, MimeType, TamanoBytes, HashContenido,
     RutaRelativa, Estado)
VALUES (?, ?, ?, ?, ?, ?, ?, ?, 'DESCARGADO')
"""


def insertar_email(cursor, m: MensajeGmail, fecha_deteccion: datetime) -> int | None:
    """Inserta la fila `Email` en `Estado='CANDIDATO'`. Devuelve el `EmailId` generado, leido con
    `OUTPUT INSERTED.EmailId` en el MISMO `execute` que el INSERT — NUNCA un `SELECT
    SCOPE_IDENTITY()` en un `execute` separado: pyodbc envuelve un INSERT parametrizado en
    `sp_executesql`, y ese wrapper cierra su propio scope al retornar, asi que una llamada
    posterior a `SCOPE_IDENTITY()` vuelve NULL (bug real encontrado en la prueba de integracion
    contra SQL Server real, no una suposicion — `OUTPUT` no tiene ese problema porque lee el valor
    dentro del mismo statement/scope que hizo el INSERT). Devuelve `None` (no lanza) si el mensaje
    ya estaba ingestado — `UQ_Email_GmailMessageId` rechaza el duplicado y el llamador salta la
    descarga de adjuntos (design.md, Decision 4)."""
    try:
        cursor.execute(
            _INSERT_EMAIL,
            m.gmail_message_id,
            m.remitente,
            m.asunto,
            m.fecha_recepcion,
            fecha_deteccion,
        )
    except pyodbc.IntegrityError:
        return None
    return int(cursor.fetchone()[0])


def insertar_documento(
    cursor,
    email_id: int,
    m: MensajeGmail,
    a: AdjuntoGmail,
    hash_hex: str,
    ruta_relativa: str,
) -> None:
    """Inserta la fila `DocumentoRecibido` en `Estado='DESCARGADO'`. Un duplicado
    `(EmailId, HashContenido)` (`UQ_DocumentoRecibido_Email_Hash`, 013) es un no-op, no un error:
    dos adjuntos con el mismo contenido en el mismo mensaje son el mismo documento (design.md,
    Decision 4)."""
    try:
        cursor.execute(
            _INSERT_DOCUMENTO,
            email_id,
            m.gmail_message_id,
            a.nombre[:_LONGITUD_MAXIMA_NOMBRE_ARCHIVO],
            a.extension,
            a.mime_type,
            a.tamano_bytes,
            hash_hex,
            ruta_relativa,
        )
    except pyodbc.IntegrityError:
        return
