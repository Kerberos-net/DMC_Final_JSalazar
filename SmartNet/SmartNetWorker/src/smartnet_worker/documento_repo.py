"""Repositorio de `fact.Email` / `fact.DocumentoRecibido` para el runtime Python — recibe un
`cursor` exactamente igual que `tipo_cambio_repo.py` (design.md, Decision 4).

`insertar_email` es la puerta de idempotencia: `UQ_Email_GmailMessageId` (003) es la que rechaza el
duplicado, este adaptador solo traduce el `IntegrityError` a `None`, nunca hace un `SELECT` previo
(misma disciplina anti-TOCTOU que `insertar_sbs`). `insertar_documento` es simetrico frente a
`UQ_DocumentoRecibido_Email_Hash` (013): dos adjuntos con el mismo contenido en el mismo mensaje son
el mismo documento, un no-op, no un error.

BACKLOG #6 (WU3) agrega el lado de lectura/cierre que la etapa de extraccion necesita:
`listar_pendientes` sirve el predicado de reintento de design.md's Decision 8 (`DESCARGADO` O un
`ERROR` cuyo reintento ya vencio, `NumeroIntento < 3`); `fijar_tipo_documento`/
`fijar_estado_documento` escriben el hook que #5 dejo explicitamente NULL; `refrescar_estado_email`
implementa la regla de cierre de Decision 9 (`CANDIDATO` -> `PROCESADO`/`ERROR`) como una UNICA
sentencia `UPDATE...CASE`, nunca un `SELECT`-then-`UPDATE` (misma disciplina anti-TOCTOU que el
resto de este modulo).
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime

import pyodbc

from smartnet_worker.gmail import AdjuntoGmail, MensajeGmail

_LONGITUD_MAXIMA_NOMBRE_ARCHIVO = 255
_TOPE_INTENTOS_RETRY = 3

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

# design.md, Decision 8: un unico predicado sirve tanto el primer intento (DESCARGADO) como un
# reintento cuyo backoff ya vencio (ERROR + ProximoReintentoEn <= ? + NumeroIntento < 3). Un
# PERMANENTE escribio ProximoReintentoEn = NULL, asi que EXISTS nunca lo re-selecciona.
_LISTAR_PENDIENTES = f"""
SELECT d.DocumentoRecibidoId, d.EmailId, d.GmailMessageId, d.NombreArchivo, d.Extension,
       d.MimeType, d.TamanoBytes, d.HashContenido, d.RutaRelativa
FROM fact.DocumentoRecibido d
WHERE d.Estado = 'DESCARGADO'
   OR (d.Estado = 'ERROR' AND EXISTS (
        SELECT 1
        FROM fact.ProcesamientoIntentos i
        JOIN fact.Procesamiento p ON p.ProcesamientoId = i.ProcesamientoId
        WHERE p.DocumentoRecibidoId = d.DocumentoRecibidoId
          AND i.ProximoReintentoEn <= ?
          AND i.NumeroIntento < {_TOPE_INTENTOS_RETRY}
   ))
"""

_UPDATE_TIPO_DOCUMENTO = """
UPDATE fact.DocumentoRecibido SET TipoDocumento = ? WHERE DocumentoRecibidoId = ?
"""

_UPDATE_ESTADO_DOCUMENTO = """
UPDATE fact.DocumentoRecibido SET Estado = ? WHERE DocumentoRecibidoId = ?
"""

# design.md, Decision 9: CANDIDATO -> PROCESADO cuando todo documento del correo esta PROCESADO;
# -> ERROR si alguno termino en error y ninguno sigue pendiente. Una unica sentencia UPDATE...CASE
# evita el SELECT-then-UPDATE que el resto de este modulo ya rechaza (anti-TOCTOU).
_REFRESCAR_ESTADO_EMAIL = """
UPDATE fact.Email
SET Estado = CASE
    WHEN NOT EXISTS (
        SELECT 1 FROM fact.DocumentoRecibido d
        WHERE d.EmailId = ? AND d.Estado NOT IN ('PROCESADO', 'ERROR')
    ) THEN
        CASE
            WHEN EXISTS (
                SELECT 1 FROM fact.DocumentoRecibido d
                WHERE d.EmailId = ? AND d.Estado = 'ERROR'
            ) THEN 'ERROR'
            ELSE 'PROCESADO'
        END
    ELSE Estado
END
WHERE EmailId = ?
"""


@dataclass(frozen=True)
class DocumentoPendiente:
    """Una fila `fact.DocumentoRecibido` lista para que `cli_procesamiento.py` (WU4) la lea del
    volumen compartido (`RutaRelativa`) y decida XML o PDF (`Extension`)."""

    documento_recibido_id: int
    email_id: int
    gmail_message_id: str
    nombre_archivo: str
    extension: str
    mime_type: str
    tamano_bytes: int
    hash_contenido: str
    ruta_relativa: str


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


def listar_pendientes(cursor, ahora: datetime) -> tuple[DocumentoPendiente, ...]:
    """`DESCARGADO` (primer intento) o `ERROR` con reintento vencido y `NumeroIntento < 3`
    (design.md, Decision 8) — un unico predicado, cero maquina de estados."""
    cursor.execute(_LISTAR_PENDIENTES, ahora)
    return tuple(
        DocumentoPendiente(
            documento_recibido_id=fila[0],
            email_id=fila[1],
            gmail_message_id=fila[2],
            nombre_archivo=fila[3],
            extension=fila[4],
            mime_type=fila[5],
            tamano_bytes=fila[6],
            hash_contenido=fila[7],
            ruta_relativa=fila[8],
        )
        for fila in cursor.fetchall()
    )


def fijar_tipo_documento(cursor, documento_recibido_id: int, tipo_documento: str) -> None:
    """`'XML'`/`'PDF'` — el hook que #5 dejo explicitamente NULL (design.md, Decision 9)."""
    cursor.execute(_UPDATE_TIPO_DOCUMENTO, tipo_documento, documento_recibido_id)


def fijar_estado_documento(cursor, documento_recibido_id: int, estado: str) -> None:
    """`'PROCESADO'`/`'ERROR'` — terminal, escrito una unica vez por transaccion de documento."""
    cursor.execute(_UPDATE_ESTADO_DOCUMENTO, estado, documento_recibido_id)


def refrescar_estado_email(cursor, email_id: int) -> None:
    """`CANDIDATO` -> `PROCESADO`/`ERROR` cuando ningun documento del correo sigue pendiente
    (design.md, Decision 9). Una sola sentencia `UPDATE...CASE`, nunca `SELECT`-then-`UPDATE`."""
    cursor.execute(_REFRESCAR_ESTADO_EMAIL, email_id, email_id, email_id)
