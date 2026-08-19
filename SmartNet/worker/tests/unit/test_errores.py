"""RED primero (BACKLOG #6, WU1): `smartnet_worker.errores` todavia no existe.

ADR 0010: PERMANENTE nunca reintenta; TRANSITORIO reintenta hasta 3 veces con espera 2^n segundos;
"la clasificacion debe errar hacia transitorio ante la duda" -- toda excepcion no reconocida cae en
TRANSITORIO, nunca en PERMANENTE.

WU1 solo trae `ubl.py` (XML) -- `pdf_lectura.PdfIlegibleError`/`pypdf.errors.PdfReadError` (PDF)
llegan en WU2 y se agregan a `clasificar` ahi, sin tocar esta suite (design.md, Decision 8's tabla
lista ambas familias; `errores.py` esta escrito para aceptar la segunda sin romper la primera --
ver `_TIPOS_PERMANENTES`)."""

from datetime import UTC, datetime, timedelta

import pyodbc
import pytest
from lxml.etree import XMLSyntaxError

from smartnet_worker.errores import Clasificacion, clasificar, proximo_reintento
from smartnet_worker.ubl import UblInvalidoError


def _xml_syntax_error() -> XMLSyntaxError:
    try:
        from lxml import etree

        etree.fromstring(b"<no-cierra>")
    except XMLSyntaxError as error:
        return error
    raise AssertionError("se esperaba XMLSyntaxError")


@pytest.mark.parametrize(
    "excepcion",
    [
        _xml_syntax_error(),
        UblInvalidoError("raiz no reconocida"),
    ],
)
def test_errores_de_documento_son_permanentes(excepcion):
    assert clasificar(excepcion) == Clasificacion.PERMANENTE


def test_pyodbc_operational_error_es_transitorio():
    error = pyodbc.OperationalError("08001", "no se pudo conectar")
    assert clasificar(error) == Clasificacion.TRANSITORIO


def test_excepcion_no_reconocida_es_transitorio_por_defecto():
    # ADR 0010: "la clasificacion debe errar hacia transitorio ante la duda".
    assert clasificar(ValueError("algo inesperado")) == Clasificacion.TRANSITORIO


def test_permanente_nunca_agenda_reintento():
    instante = datetime(2026, 8, 19, 12, 0, 0, tzinfo=UTC)
    assert proximo_reintento(Clasificacion.PERMANENTE, instante, intento=1) is None


def test_transitorio_agenda_backoff_exponencial():
    instante = datetime(2026, 8, 19, 12, 0, 0, tzinfo=UTC)
    clasificacion = Clasificacion.TRANSITORIO

    assert proximo_reintento(clasificacion, instante, intento=1) == instante + timedelta(seconds=2)
    assert proximo_reintento(clasificacion, instante, intento=2) == instante + timedelta(seconds=4)
    assert proximo_reintento(clasificacion, instante, intento=3) == instante + timedelta(seconds=8)


def test_transitorio_tope_en_tres_intentos():
    # n <= 3 (design.md, Decision 8) -- un cuarto intento no crece el backoff mas alla de 2^3.
    instante = datetime(2026, 8, 19, 12, 0, 0, tzinfo=UTC)
    resultado = proximo_reintento(Clasificacion.TRANSITORIO, instante, intento=4)
    assert resultado == instante + timedelta(seconds=8)
