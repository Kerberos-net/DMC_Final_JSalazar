"""Modulo puro de decision sobre correo de Gmail (ADR 0017 aplicado a Python, BACKLOG #5).

Ni red, ni disco, ni DB, ni reloj del sistema (ADR 0019, misma regla que sbs.py): recibe el JSON ya
descargado (por `gmail_client.py`/`cli_gmail.py`, los unicos puntos de IO) y decide -- construye la
consulta acotada, parsea el mensaje, evalua candidatura por extension, calcula el hash y la ruta de
escritura. Nunca lee `Asunto`/`Remitente` para decidir candidatura (ADR 0017: "el asunto y el
remitente no intervienen") -- solo los transporta hacia las columnas que los persisten.

`fecha_recepcion` viene de `internalDate` (epoch en milisegundos, UTC segun la propia API de
Gmail), nunca de `datetime.now()` -- lo que mantiene estas funciones puras y las pruebas
deterministas, igual que `sbs.py`.
"""

from __future__ import annotations

import hashlib
import unicodedata
from dataclasses import dataclass
from datetime import UTC, date, datetime
from re import compile as _compile_regex

_CARACTER_NO_PERMITIDO_RE = _compile_regex(r"[^A-Za-z0-9._-]")
_GUION_BAJO_RUNS_RE = _compile_regex(r"_+")

_NOMBRES_RESERVADOS_WINDOWS = (
    {"CON", "PRN", "AUX", "NUL"}
    | {f"COM{n}" for n in range(1, 10)}
    | {f"LPT{n}" for n in range(1, 10)}
)

_LONGITUD_MAXIMA_NOMBRE = 100
_NOMBRE_POR_DEFECTO = "adjunto"
_EXTENSION_POR_DEFECTO = "bin"


class ParseoGmailError(Exception):
    """El mensaje de Gmail (respuesta de `messages.get`) no tiene la estructura esperada:
    `payload`/`id`/`internalDate` ausente, o falta el header `From` (Decision 6: un mensaje sin
    remitente no puede producir una fila y se cuenta como mensaje fallido)."""


@dataclass(frozen=True)
class AdjuntoGmail:
    nombre: str
    extension: str
    mime_type: str
    attachment_id: str
    tamano_bytes: int


@dataclass(frozen=True)
class MensajeGmail:
    gmail_message_id: str
    remitente: str
    asunto: str | None
    fecha_recepcion: datetime
    adjuntos: tuple[AdjuntoGmail, ...]


def construir_consulta(origen: str, procesado: str, desde: date) -> str:
    """`label:<origen> -label:<procesado> after:<yyyy/mm/dd>` (ADR 0017). Una etiqueta con espacios
    se cita (`label:"Facturas 2026"`) porque Gmail interpreta un espacio sin comillas como un nuevo
    termino de busqueda."""
    fecha_texto = desde.strftime("%Y/%m/%d")
    origen_citado = _citar_si_hace_falta(origen)
    procesado_citado = _citar_si_hace_falta(procesado)
    return f"label:{origen_citado} -label:{procesado_citado} after:{fecha_texto}"


def _citar_si_hace_falta(etiqueta: str) -> str:
    return f'"{etiqueta}"' if " " in etiqueta else etiqueta


def parsear_mensaje(mensaje: dict) -> MensajeGmail:
    """Convierte la respuesta completa de `messages.get` (format=full) en un `MensajeGmail`.
    Recorre `payload`/`parts` de forma recursiva reuniendo cada parte con `attachmentId` -- incluida
    una imagen inline con `filename` vacio, que queda en `adjuntos` con `nombre=""` y
    `extension=""`; es `es_candidato` quien la descarta de forma natural, nunca este parseo."""
    gmail_message_id = mensaje.get("id")
    if not gmail_message_id:
        raise ParseoGmailError("El mensaje no tiene 'id'.")

    payload = mensaje.get("payload")
    if not isinstance(payload, dict):
        raise ParseoGmailError(f"El mensaje '{gmail_message_id}' no tiene 'payload'.")

    headers = payload.get("headers") or []
    remitente = _buscar_header(headers, "From")
    if not remitente:
        raise ParseoGmailError(
            f"El mensaje '{gmail_message_id}' no tiene remitente (header 'From')."
        )

    asunto = _buscar_header(headers, "Subject")
    if asunto is not None and len(asunto) > 500:
        asunto = asunto[:500]

    internal_date = mensaje.get("internalDate")
    if internal_date is None:
        raise ParseoGmailError(f"El mensaje '{gmail_message_id}' no tiene 'internalDate'.")
    try:
        fecha_recepcion = datetime.fromtimestamp(int(internal_date) / 1000, tz=UTC)
    except (TypeError, ValueError) as error:
        raise ParseoGmailError(
            f"El mensaje '{gmail_message_id}' tiene 'internalDate' invalido: {internal_date!r}."
        ) from error

    adjuntos = tuple(_recorrer_adjuntos(payload))

    return MensajeGmail(
        gmail_message_id=gmail_message_id,
        remitente=remitente,
        asunto=asunto,
        fecha_recepcion=fecha_recepcion,
        adjuntos=adjuntos,
    )


def _buscar_header(headers: list[dict], nombre: str) -> str | None:
    nombre_normalizado = nombre.lower()
    for header in headers:
        if str(header.get("name", "")).lower() == nombre_normalizado:
            valor = header.get("value")
            return valor if valor else None
    return None


def _recorrer_adjuntos(parte: dict) -> list[AdjuntoGmail]:
    resultado: list[AdjuntoGmail] = []
    cuerpo = parte.get("body") or {}
    if "attachmentId" in cuerpo:
        nombre = parte.get("filename") or ""
        resultado.append(
            AdjuntoGmail(
                nombre=nombre,
                extension=_extension_final(nombre),
                mime_type=parte.get("mimeType") or "application/octet-stream",
                attachment_id=cuerpo["attachmentId"],
                tamano_bytes=int(cuerpo.get("size") or 0),
            )
        )
    for subparte in parte.get("parts") or []:
        resultado.extend(_recorrer_adjuntos(subparte))
    return resultado


def _extension_final(nombre: str) -> str:
    """La extension es exactamente el ultimo sufijo tras el ultimo punto, en minusculas -- nunca
    una comparacion por substring (Decision 6: 'factura.pdf.exe' es candidato solo si 'exe' esta
    permitido, jamas por contener 'pdf')."""
    if "." not in nombre:
        return ""
    return nombre.rsplit(".", 1)[1].lower()


def extensiones_permitidas(texto: str) -> frozenset[str]:
    """'pdf,xml' -> frozenset({'pdf','xml'}). Recorta espacios, pasa a minuscula, quita un punto
    inicial si alguien lo escribio por error, e ignora entradas vacias (comas dobles/al borde)."""
    partes = (parte.strip().lower().lstrip(".") for parte in texto.split(","))
    return frozenset(parte for parte in partes if parte)


def es_candidato(nombre: str, permitidas: frozenset[str]) -> bool:
    """Candidatura = exactamente la extension final, en minusculas, contra la lista permitida.
    Nunca evalua asunto ni remitente -- ni siquiera los recibe (ADR 0017)."""
    if not nombre:
        return False
    extension = _extension_final(nombre)
    if not extension:
        return False
    return extension in permitidas


def calcular_hash(datos: bytes) -> str:
    """SHA-256 en hexadecimal minuscula, 64 caracteres -- identidad de contenido del adjunto
    (ADR 0010)."""
    return hashlib.sha256(datos).hexdigest()


def sanitizar_nombre_archivo(nombre: str) -> str:
    """Pura (Decision 5): NFC-normaliza, conserva solo `[A-Za-z0-9._-]` (todo lo demas -- separador
    de ruta, `:`, espacios, caracteres de control, acentos -- se vuelve `_`), colapsa corridas de
    `_`, recorta `.`/`_` en los bordes, y si el resultado queda vacio usa `adjunto`. Un nombre base
    (antes del primer punto) que coincide con un dispositivo reservado de Windows
    (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`) recibe un prefijo `_`. Trunca a 100
    caracteres al final, despues de cualquier prefijo."""
    normalizado = unicodedata.normalize("NFC", nombre)
    reemplazado = _CARACTER_NO_PERMITIDO_RE.sub("_", normalizado)
    colapsado = _GUION_BAJO_RUNS_RE.sub("_", reemplazado)
    despojado = colapsado.strip("._")

    if not despojado:
        return _NOMBRE_POR_DEFECTO

    base = despojado.split(".", 1)[0]
    if base.upper() in _NOMBRES_RESERVADOS_WINDOWS:
        despojado = f"_{despojado}"

    return despojado[:_LONGITUD_MAXIMA_NOMBRE]


def ruta_relativa(m: MensajeGmail, a: AdjuntoGmail, hash_hex: str) -> str:
    """`<yyyy>/<MM>/<GmailMessageId>/<stem-sanitizado>_<hash[:8]>.<ext>` (Decision 5). `yyyy`/`MM`
    vienen de `m.fecha_recepcion` (el `internalDate` del mensaje), nunca del reloj. El sufijo
    `_<hash[:8]>` obligatorio es lo que hace que ningun componente pueda terminar siendo `.` o `..`
    incluso si el nombre saneado colapsa a `adjunto`, y lo que separa dos adjuntos con el mismo
    nombre pero contenido distinto en rutas distintas."""
    anio = m.fecha_recepcion.strftime("%Y")
    mes = m.fecha_recepcion.strftime("%m")
    raiz_original = a.nombre.rsplit(".", 1)[0] if "." in a.nombre else a.nombre
    raiz_saneada = sanitizar_nombre_archivo(raiz_original)
    extension = a.extension or _EXTENSION_POR_DEFECTO
    return f"{anio}/{mes}/{m.gmail_message_id}/{raiz_saneada}_{hash_hex[:8]}.{extension}"
