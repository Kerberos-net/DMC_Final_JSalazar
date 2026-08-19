"""Modulo puro de clasificacion de errores y reintentos (ADR 0010, BACKLOG #6).

Ni red, ni disco, ni DB (ADR 0019): `clasificar` es un lookup por tipo de excepcion, sin efecto de
lado; `proximo_reintento` recibe el instante como parametro, nunca lee el reloj del sistema.

Tres clases de ADR 0010 aplican a este item (`OBSOLETO` es exclusivo de #4/eventos de agregado, sin
productor aqui): `PERMANENTE` para un documento que nunca puede producir un comprobante (XML
invalido, adjunto corrupto/cifrado/no soportado), `TRANSITORIO` para todo lo demas -- incluida
CUALQUIER excepcion no reconocida, porque "la clasificacion debe errar hacia transitorio ante la
duda" (ADR 0010). `DIFERIBLE` no tiene productor en este item -- nada aqui llama una API con cuota;
queda deliberadamente sin usar, no olvidado.

`_TIPOS_PERMANENTES` cubre ambas familias de documento (design.md, Decision 8's tabla): el lado XML
(`XMLSyntaxError`, `UblInvalidoError`, WU1) y el lado PDF (`PdfIlegibleError` de `pdf_lectura.py`,
`pypdf.errors.PdfReadError`, WU2)."""

from __future__ import annotations

from datetime import datetime, timedelta
from enum import StrEnum

from lxml.etree import XMLSyntaxError
from pypdf.errors import PdfReadError

from smartnet_worker.pdf_lectura import PdfIlegibleError
from smartnet_worker.ubl import UblInvalidoError

_TOPE_INTENTOS_BACKOFF = 3


class Clasificacion(StrEnum):
    TRANSITORIO = "TRANSITORIO"
    DIFERIBLE = "DIFERIBLE"
    PERMANENTE = "PERMANENTE"
    OBSOLETO = "OBSOLETO"


# Excepciones de documento: nunca se resuelven reintentando (ADR 0010: "Adjunto corrupto, protegido
# con contrasena o en formato no soportado; XML invalido").
_TIPOS_PERMANENTES: tuple[type[BaseException], ...] = (
    XMLSyntaxError,
    UblInvalidoError,
    PdfIlegibleError,
    PdfReadError,
)


def clasificar(error: BaseException) -> Clasificacion:
    """Lookup puro: un tipo de `_TIPOS_PERMANENTES` -> `PERMANENTE`; cualquier otra cosa,
    reconocida o no, -> `TRANSITORIO` (ADR 0010's regla de "errar hacia transitorio ante la
    duda")."""
    if isinstance(error, _TIPOS_PERMANENTES):
        return Clasificacion.PERMANENTE
    return Clasificacion.TRANSITORIO


def proximo_reintento(
    clasificacion: Clasificacion, instante: datetime, intento: int
) -> datetime | None:
    """`PERMANENTE` -> `None` (nunca se reintenta, `ProximoReintentoEn IS NULL`). `TRANSITORIO` ->
    `instante + 2^n` segundos, con `n` topado en `_TOPE_INTENTOS_BACKOFF` (design.md, Decision 8) --
    un cuarto intento no crece el backoff mas alla de `2**3`."""
    if clasificacion == Clasificacion.PERMANENTE:
        return None
    exponente = min(intento, _TOPE_INTENTOS_BACKOFF)
    return instante + timedelta(seconds=2**exponente)
