"""Modulo puro de extraccion de datos desde texto de PDF (design.md Decision 5/6, proposal.md's
Open Question 1, BACKLOG #6, WU2).

Ni red, ni disco, ni DB, ni reloj (ADR 0019): recibe el texto que `pdf_lectura.py` (IO) ya extrajo
-- de la capa embebida o de OCR -- mas el nombre de archivo del adjunto (el nombre crudo de Gmail,
solo truncado -- `gmail.sanitizar_nombre_archivo` se aplica unicamente al stem de `RutaRelativa`,
no a `NombreArchivo`), y decide via regex
la clave del comprobante (RUC emisor, tipo, serie, numero) mas los campos no-identidad (monto,
moneda, fecha de emision) que REGLAS.md exige persistir junto al resto de los datos extraidos.

Content-first, filename-as-backup (design.md, Decision 6): si el texto no produce los cuatro
componentes de la clave, se cae al respaldo estricto del nombre de archivo SUNAT
`<RUC>-<TIPO>-<SERIE>-<NUMERO>.pdf` (ADR 0017: "el nombre del archivo puede usarse como respaldo,
siempre que la coincidencia sea inequivoca") -- el patron es todo-o-nada: un respaldo parcial nunca
produce clave.

Dos RUC en el mismo texto (Open Question 1, resuelta): el RUC propio de la empresa
(`fact.Configuracion` clave `EMPRESA.RUC`, migracion 014) llega como el parametro opcional
`ruc_propio` -- este modulo no lee la base de datos. Con `ruc_propio` presente, el RUC restante --
el que no coincide, tras normalizar ambos a solo digitos -- es el emisor. Sin `ruc_propio`
configurado, o si ninguno/mas de uno de los RUC encontrados sobrevive la exclusion, no hay forma
no-inferencial de elegir uno (ADR 0017 prohibe inferir por proximidad de etiqueta): el extractor
cae directo al respaldo de nombre de archivo, como cualquier otro caso sin RUC resuelto por texto.
"""

from __future__ import annotations

from dataclasses import dataclass
from datetime import date
from decimal import Decimal, InvalidOperation
from re import IGNORECASE
from re import compile as _compile_regex

from smartnet_worker.comprobante import ClaveComprobante, construir_clave, normalizar_ruc

_LONGITUD_RUC = 11

# RUC junto a la etiqueta 'RUC' o 'R.U.C.' (con o sin puntos), con o sin ':' de separador y con
# palabras intermedias tolerantes ('RUC EMISOR:', 'RUC CLIENTE:'). El grupo capturado puede traer
# separadores (guiones, espacios) — se normaliza despues via `comprobante.normalizar_ruc` y se
# valida que queden exactamente 11 digitos.
_RUC_RE = _compile_regex(r"R\.?U\.?C\.?[^\d\n]{0,20}?(\d[\d\-\s]{9,25}\d)", IGNORECASE)

# Serie SUNAT alfanumerica: letra + 3 alfanumericos ('F001', 'F96X', 'E001') o '001' (impresa, solo
# 3 digitos) — numero de 1 a 20 digitos (VARCHAR(20), 003's DDL). Con o sin espacios alrededor del
# guion. El lookahead negativo `(?![A-Za-z]{3}\b)` descarta tokens de solo letras, para que
# colocaciones de prosa ('NOTA-123', 'FACT-123') no se tomen como serie (design.md D8).
_SERIE_NUMERO_RE = _compile_regex(
    r"\b([A-Za-z](?![A-Za-z]{3}\b)[A-Za-z0-9]{3}|\d{3})\s*-\s*(\d{1,20})\b"
)

_MONTO_RE = _compile_regex(
    r"(?:TOTAL\s+A\s+PAGAR|IMPORTE\s+TOTAL|TOTAL)\s*:?\s*(?:S/\.?|US\$|USD)?\s*([\d,]+\.\d{2})",
    IGNORECASE,
)
_FECHA_RE = _compile_regex(r"\b(\d{2})/(\d{2})/(\d{4})\b")
_MONEDA_DOLARES_RE = _compile_regex(r"US\$|USD", IGNORECASE)
_MONEDA_SOLES_RE = _compile_regex(r"S/\.?", IGNORECASE)

# Respaldo de nombre de archivo SUNAT: <RUC 11 digitos>-<TIPO 1 o 2 digitos>-<SERIE>-<NUMERO>.pdf,
# todo-o-nada (design.md, Decision 6) — cualquier segmento ausente o mal formado no matchea.
_NOMBRE_ARCHIVO_RE = _compile_regex(
    r"^(\d{11})-(\d{1,2})-([A-Za-z0-9]{1,4})-(\d{1,20})\.pdf$", IGNORECASE
)

# Palabras clave -> tipo de comprobante (SUNAT catalogo 01). Orden importa: 'NOTA DE CREDITO'/
# 'NOTA DE DEBITO' deben probarse antes de que cualquier substring generico de 'FACTURA'/'BOLETA'
# pudiera aparecer en el mismo texto.
_TIPO_POR_PALABRA_CLAVE: tuple[tuple[str, str], ...] = (
    ("NOTA DE CREDITO", "07"),
    ("NOTA DE DEBITO", "08"),
    ("FACTURA", "01"),
    ("BOLETA", "03"),
)


@dataclass(frozen=True)
class ExtraccionPdf:
    """Lo que `extraer` logra reconstruir del texto de un PDF (y, como respaldo, de su nombre de
    archivo). `clave` es `None` si ninguna de las dos fuentes produjo los cuatro componentes -- ese
    documento nunca participa en `comprobante.asociar`. Los demas campos son no-identidad: una
    ausencia se registra por nombre en `campos_no_extraidos`, nunca es fatal (mismo principio que
    `ubl.ComprobanteUbl`)."""

    clave: ClaveComprobante | None
    monto: Decimal | None
    moneda: str | None
    fecha_emision: date | None
    campos_no_extraidos: tuple[str, ...]


def extraer(texto: str, nombre_archivo: str, ruc_propio: str | None = None) -> ExtraccionPdf:
    campos_no_extraidos: list[str] = []

    clave = _clave_desde_texto(texto, ruc_propio)
    if clave is None:
        clave = _respaldo_desde_nombre_archivo(nombre_archivo)
    if clave is None:
        campos_no_extraidos.append("Clave")

    monto = _extraer_monto(texto)
    if monto is None:
        campos_no_extraidos.append("Monto")

    moneda = _extraer_moneda(texto)
    if moneda is None:
        campos_no_extraidos.append("Moneda")

    fecha_emision = _extraer_fecha(texto)
    if fecha_emision is None:
        campos_no_extraidos.append("FechaEmision")

    return ExtraccionPdf(
        clave=clave,
        monto=monto,
        moneda=moneda,
        fecha_emision=fecha_emision,
        campos_no_extraidos=tuple(campos_no_extraidos),
    )


def _clave_desde_texto(texto: str, ruc_propio: str | None) -> ClaveComprobante | None:
    ruc_emisor = _extraer_ruc_emisor(texto, ruc_propio)
    serie_numero = _extraer_serie_numero(texto)
    tipo = _extraer_tipo(texto)
    if not (ruc_emisor and serie_numero and tipo):
        return None
    return construir_clave(ruc_emisor, tipo, serie_numero)


def _extraer_ruc_emisor(texto: str, ruc_propio: str | None) -> str | None:
    rucs: list[str] = []
    for crudo in _RUC_RE.findall(texto):
        normalizado = normalizar_ruc(crudo)
        if len(normalizado) == _LONGITUD_RUC and normalizado not in rucs:
            rucs.append(normalizado)

    if not rucs:
        return None
    if len(rucs) == 1:
        return rucs[0]

    # Mas de un RUC: Open Question 1, resuelta. El RUC propio (si esta configurado) se excluye por
    # valor exacto; el unico restante es el emisor. Sin ruc_propio, o si la exclusion no deja
    # exactamente uno, no hay forma no-inferencial de elegir (ADR 0017).
    if not ruc_propio:
        return None
    propio_normalizado = normalizar_ruc(ruc_propio)
    restantes = [ruc for ruc in rucs if ruc != propio_normalizado]
    if len(restantes) == 1:
        return restantes[0]
    return None


def _extraer_serie_numero(texto: str) -> str | None:
    coincidencia = _SERIE_NUMERO_RE.search(texto)
    if not coincidencia:
        return None
    return f"{coincidencia.group(1)}-{coincidencia.group(2)}"


def _extraer_tipo(texto: str) -> str | None:
    texto_mayus = texto.upper()
    for palabra_clave, tipo in _TIPO_POR_PALABRA_CLAVE:
        if palabra_clave in texto_mayus:
            return tipo
    return None


def _respaldo_desde_nombre_archivo(nombre_archivo: str) -> ClaveComprobante | None:
    coincidencia = _NOMBRE_ARCHIVO_RE.match(nombre_archivo)
    if not coincidencia:
        return None
    ruc, tipo, serie, numero = coincidencia.groups()
    return construir_clave(ruc, tipo, f"{serie}-{numero}")


def _extraer_monto(texto: str) -> Decimal | None:
    coincidencia = _MONTO_RE.search(texto)
    if not coincidencia:
        return None
    crudo = coincidencia.group(1).replace(",", "")
    try:
        # CONVENTIONS.md: nunca float — Decimal(str(...)) construido directo desde el texto crudo.
        return Decimal(crudo)
    except InvalidOperation:
        return None


def _extraer_moneda(texto: str) -> str | None:
    if _MONEDA_DOLARES_RE.search(texto):
        return "USD"
    if _MONEDA_SOLES_RE.search(texto):
        return "PEN"
    return None


def _extraer_fecha(texto: str) -> date | None:
    coincidencia = _FECHA_RE.search(texto)
    if not coincidencia:
        return None
    dia, mes, anio = coincidencia.groups()
    try:
        return date(int(anio), int(mes), int(dia))
    except ValueError:
        return None
