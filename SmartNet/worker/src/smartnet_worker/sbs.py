"""Parseo puro de la pagina de tipo de cambio de la SBS (ADR 0019 aplicado a Python).

`parse_tipo_cambio` no hace red, no toca la base de datos y no lee el reloj del sistema: recibe
el HTML ya descargado (por `cli_tipo_cambio.py`, el unico punto de IO) y devuelve un
`TipoCambioSbs`, o lanza `ParseoSbsError` si la estructura no calza. Esto es lo que permite
probarlo con una pagina guardada, sin red (design.md, Testing Strategy).

Nota sobre el fixture de pruebas (ver tests/fixtures/README.md): la pagina real de la SBS esta
detras de un WAF (Incapsula) que bloquea peticiones automatizadas sin navegador, asi que el HTML
real no pudo obtenerse en este entorno. El fixture usado en las pruebas es SINTETICO — una
estructura plausible con un `<table id="tblTipoCambio">` de tres columnas (Fecha, Compra, Venta) y
un elemento con el id de fecha de consulta — no una copia literal de la pagina de producción. Si
la pagina real usa otro `id`/estructura, este parser debera ajustarse contra un fixture real
capturado a mano (documentado, nunca adivinado en silencio).
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from datetime import date, datetime
from decimal import Decimal, InvalidOperation

from bs4 import BeautifulSoup

_TABLA_ID = "tblTipoCambio"
_CONSULTA_ID = "lblFechaConsulta"

_FECHA_RE = re.compile(r"(\d{2})/(\d{2})/(\d{4})")
_FECHA_CONSULTA_RE = re.compile(r"(\d{2})/(\d{2})/(\d{4})\s+(\d{2}):(\d{2}):(\d{2})")


class ParseoSbsError(Exception):
    """La pagina de la SBS no tiene la estructura esperada (tabla ausente, fila ausente, valor
    no numerico, fecha con formato invalido)."""


@dataclass(frozen=True)
class TipoCambioSbs:
    fecha: date
    compra: Decimal
    venta: Decimal
    fecha_consulta: datetime


def parse_tipo_cambio(html: str) -> TipoCambioSbs:
    """Extrae la fila de tipo de cambio vigente de la pagina de la SBS.

    Puro: ni red, ni DB, ni `datetime.now()`. `fecha_consulta` viene del propio HTML (la pagina
    muestra cuando se genero la consulta), nunca del reloj del proceso — de lo contrario esta
    funcion dejaria de ser pura y las pruebas dejarian de ser deterministas.
    """
    soup = BeautifulSoup(html, "html.parser")

    tabla = soup.find(id=_TABLA_ID)
    if tabla is None:
        raise ParseoSbsError(f"No se encontro la tabla '#{_TABLA_ID}' en el HTML de la SBS.")

    fila_datos = _buscar_fila_de_datos(tabla)
    if fila_datos is None:
        raise ParseoSbsError(
            f"No se encontro una fila de datos (Fecha, Compra, Venta) dentro de '#{_TABLA_ID}'."
        )

    texto_fecha, texto_compra, texto_venta = fila_datos
    fecha = _parsear_fecha(texto_fecha)
    compra = _parsear_decimal(texto_compra, etiqueta="Compra")
    venta = _parsear_decimal(texto_venta, etiqueta="Venta")

    elemento_consulta = soup.find(id=_CONSULTA_ID)
    if elemento_consulta is None:
        raise ParseoSbsError(
            f"No se encontro el elemento '#{_CONSULTA_ID}' con la fecha de consulta."
        )

    fecha_consulta = _parsear_fecha_consulta(elemento_consulta.get_text(strip=True))

    return TipoCambioSbs(fecha=fecha, compra=compra, venta=venta, fecha_consulta=fecha_consulta)


def _buscar_fila_de_datos(tabla) -> tuple[str, str, str] | None:
    for fila in tabla.find_all("tr"):
        celdas = fila.find_all("td")
        if len(celdas) == 3:
            return tuple(celda.get_text(strip=True) for celda in celdas)  # type: ignore[return-value]
    return None


def _parsear_fecha(texto: str) -> date:
    coincidencia = _FECHA_RE.search(texto)
    if coincidencia is None:
        raise ParseoSbsError(f"Fecha con formato inesperado: '{texto}' (se esperaba dd/mm/aaaa).")
    dia, mes, anio = (int(grupo) for grupo in coincidencia.groups())
    try:
        return date(anio, mes, dia)
    except ValueError as error:
        raise ParseoSbsError(f"Fecha invalida: '{texto}'.") from error


def _parsear_fecha_consulta(texto: str) -> datetime:
    coincidencia = _FECHA_CONSULTA_RE.search(texto)
    if coincidencia is None:
        raise ParseoSbsError(
            f"Fecha de consulta con formato inesperado: '{texto}' "
            "(se esperaba dd/mm/aaaa hh:mm:ss)."
        )
    dia, mes, anio, hora, minuto, segundo = (int(grupo) for grupo in coincidencia.groups())
    try:
        return datetime(anio, mes, dia, hora, minuto, segundo)
    except ValueError as error:
        raise ParseoSbsError(f"Fecha de consulta invalida: '{texto}'.") from error


def _parsear_decimal(texto: str, *, etiqueta: str) -> Decimal:
    try:
        # Decimal(str(...)) nunca float — CONVENTIONS.md: el tipo de cambio no admite el error de
        # representacion binaria de un float, ni siquiera para un valor intermedio.
        return Decimal(str(texto).strip())
    except InvalidOperation as error:
        raise ParseoSbsError(f"{etiqueta} no es un numero decimal valido: '{texto}'.") from error
