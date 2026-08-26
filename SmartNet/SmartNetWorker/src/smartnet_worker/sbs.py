"""Parseo puro de la pagina de tipo de cambio de la SBS (ADR 0019 aplicado a Python).

`parse_tipo_cambio` no hace red, no toca la base de datos y no lee el reloj del sistema: recibe
el HTML ya descargado (por `cli_tipo_cambio.py`, el unico punto de IO) y devuelve un
`TipoCambioSbs`, o lanza `ParseoSbsError` si la estructura no calza. Esto es lo que permite
probarlo con una pagina guardada, sin red (design.md, Testing Strategy).

Nota sobre el fixture de pruebas (ver tests/fixtures/README.md): la pagina real de la SBS esta
detras de un WAF (Incapsula) que bloquea peticiones automatizadas sin motor JS (`curl`/WebFetch),
pero un navegador real (Claude in Chrome) la renderiza sin problema. El fixture usado en las
pruebas es una captura REAL del subarbol de la tabla (Telerik RadGrid `rgMasterTable`) y del span
de fecha, tomada el 18/08/2026 contra
`https://www.sbs.gob.pe/app/pp/SISTIP_PORTAL/Paginas/Publicacion/TipoCambioPromedio.aspx`.

Decision de diseno — `fecha` sin columna propia: la tabla real solo tiene tres columnas (MONEDA,
COMPRA, VENTA); no existe una columna "Fecha" por fila como asumia el fixture sintetico anterior.
La unica fecha que publica la pagina es la del span `#ctl00_cphContent_lblFecha`
("Tipo de Cambio al dd/mm/aaaa"), asi que `fecha` (la fecha a la que aplica el tipo de cambio) se
deriva de ese mismo texto, igual que `fecha_consulta`.

Decision de diseno — `fecha_consulta` sin hora: ese span tampoco publica una hora (a diferencia de
lo que asumia el fixture sintetico, "dd/mm/aaaa hh:mm:ss"). `TipoCambioSbs.fecha_consulta` sigue
siendo `datetime` porque `fact.TipoCambio.FechaConsulta` es `DATETIME2(3) NOT NULL` (no admite
NULL ni un tipo `date`) — se usa medianoche (00:00:00) del dia publicado como convencion explicita,
nunca la hora del reloj del proceso (la funcion sigue siendo pura).
"""

from __future__ import annotations

import re
from dataclasses import dataclass
from datetime import date, datetime
from decimal import Decimal, InvalidOperation

from bs4 import BeautifulSoup

_TABLA_ID = "ctl00_cphContent_rgTipoCambio_ctl00"
_CONSULTA_ID = "ctl00_cphContent_lblFecha"
_MONEDA_USD_TEXTO = "Dólar de N.A."

_FECHA_CONSULTA_RE = re.compile(r"(\d{2})/(\d{2})/(\d{4})")


class ParseoSbsError(Exception):
    """La pagina de la SBS no tiene la estructura esperada (tabla ausente, fila de USD ausente,
    valor no numerico, fecha con formato invalido)."""


@dataclass(frozen=True)
class TipoCambioSbs:
    fecha: date
    compra: Decimal
    venta: Decimal
    fecha_consulta: datetime


def parse_tipo_cambio(html: str) -> TipoCambioSbs:
    """Extrae la fila de tipo de cambio del dolar (USD) de la pagina de la SBS.

    Puro: ni red, ni DB, ni `datetime.now()`. `fecha`/`fecha_consulta` vienen del propio HTML (la
    pagina muestra la fecha a la que aplica la consulta), nunca del reloj del proceso — de lo
    contrario esta funcion dejaria de ser pura y las pruebas dejarian de ser deterministas.
    """
    soup = BeautifulSoup(html, "html.parser")

    tabla = soup.find(id=_TABLA_ID)
    if tabla is None:
        raise ParseoSbsError(f"No se encontro la tabla '#{_TABLA_ID}' en el HTML de la SBS.")

    fila_usd = _buscar_fila_usd(tabla)
    if fila_usd is None:
        raise ParseoSbsError(
            f"No se encontro una fila de '{_MONEDA_USD_TEXTO}' dentro de '#{_TABLA_ID}'."
        )

    texto_compra, texto_venta = fila_usd
    compra = _parsear_decimal(texto_compra, etiqueta="Compra")
    venta = _parsear_decimal(texto_venta, etiqueta="Venta")

    elemento_consulta = soup.find(id=_CONSULTA_ID)
    if elemento_consulta is None:
        raise ParseoSbsError(
            f"No se encontro el elemento '#{_CONSULTA_ID}' con la fecha de consulta."
        )

    fecha = _parsear_fecha(elemento_consulta.get_text(strip=True))
    fecha_consulta = datetime(fecha.year, fecha.month, fecha.day)

    return TipoCambioSbs(fecha=fecha, compra=compra, venta=venta, fecha_consulta=fecha_consulta)


def _buscar_fila_usd(tabla) -> tuple[str, str] | None:
    """Recorre las filas de `<tbody>` buscando la que corresponde al dolar (USD): la primera cuyo
    primer `<td>` (columna MONEDA) es exactamente "Dólar de N.A.". Preferido sobre depender del
    indice de fila (`__0`) porque no se rompe si la SBS reordena las filas."""
    for fila in tabla.find_all("tr"):
        celdas = fila.find_all("td")
        if len(celdas) != 3:
            continue
        moneda, compra, venta = celdas
        if moneda.get_text(strip=True) == _MONEDA_USD_TEXTO:
            return compra.get_text(strip=True), venta.get_text(strip=True)
    return None


def _parsear_fecha(texto: str) -> date:
    coincidencia = _FECHA_CONSULTA_RE.search(texto)
    if coincidencia is None:
        raise ParseoSbsError(
            f"Fecha de consulta con formato inesperado: '{texto}' (se esperaba dd/mm/aaaa)."
        )
    dia, mes, anio = (int(grupo) for grupo in coincidencia.groups())
    try:
        return date(anio, mes, dia)
    except ValueError as error:
        raise ParseoSbsError(f"Fecha de consulta invalida: '{texto}'.") from error


def _parsear_decimal(texto: str, *, etiqueta: str) -> Decimal:
    try:
        # Decimal(str(...)) nunca float — CONVENTIONS.md: el tipo de cambio no admite el error de
        # representacion binaria de un float, ni siquiera para un valor intermedio.
        return Decimal(str(texto).strip())
    except InvalidOperation as error:
        raise ParseoSbsError(f"{etiqueta} no es un numero decimal valido: '{texto}'.") from error
