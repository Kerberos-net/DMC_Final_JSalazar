from datetime import date, datetime
from decimal import Decimal
from pathlib import Path

import pytest

from smartnet_worker.sbs import ParseoSbsError, TipoCambioSbs, parse_tipo_cambio

_FIXTURES = Path(__file__).resolve().parent.parent / "fixtures"


def _leer_fixture(nombre: str) -> str:
    return (_FIXTURES / nombre).read_text(encoding="utf-8")


def test_parse_tipo_cambio_retorna_decimal_exactos_de_la_pagina_guardada():
    html = _leer_fixture("sbs_tipo_cambio.html")

    resultado = parse_tipo_cambio(html)

    # La fila real de la SBS no tiene columna "Fecha" propia (solo MONEDA/COMPRA/VENTA): `fecha`
    # se deriva del mismo span "Tipo de Cambio al dd/mm/aaaa" que `fecha_consulta`, sin hora
    # (medianoche) porque la pagina real no publica una hora de consulta — ver sbs.py.
    assert resultado == TipoCambioSbs(
        fecha=date(2026, 8, 18),
        compra=Decimal("3.356"),
        venta=Decimal("3.362"),
        fecha_consulta=datetime(2026, 8, 18, 0, 0, 0),
    )


def test_parse_tipo_cambio_usa_decimal_no_float_para_compra_y_venta():
    html = _leer_fixture("sbs_tipo_cambio.html")

    resultado = parse_tipo_cambio(html)

    assert isinstance(resultado.compra, Decimal)
    assert isinstance(resultado.venta, Decimal)
    assert not isinstance(resultado.compra, float)


def test_parse_tipo_cambio_ignora_filas_de_otras_monedas():
    html = _leer_fixture("sbs_tipo_cambio.html")

    resultado = parse_tipo_cambio(html)

    # La fixture incluye una segunda fila (Dólar Canadiense) que el parser debe ignorar: solo la
    # fila "Dólar de N.A." (USD) importa para este item.
    assert resultado.venta == Decimal("3.362")


def test_parse_tipo_cambio_html_mutilado_lanza_parseo_sbs_error():
    html = _leer_fixture("sbs_tipo_cambio_malformado.html")

    with pytest.raises(ParseoSbsError):
        parse_tipo_cambio(html)


def test_parse_tipo_cambio_valor_no_numerico_lanza_parseo_sbs_error():
    html = _leer_fixture("sbs_tipo_cambio.html").replace("3.356", "N/D")

    with pytest.raises(ParseoSbsError):
        parse_tipo_cambio(html)


def test_parse_tipo_cambio_fecha_con_formato_invalido_lanza_parseo_sbs_error():
    html = _leer_fixture("sbs_tipo_cambio.html").replace("18/08/2026", "2026-08-18")

    with pytest.raises(ParseoSbsError):
        parse_tipo_cambio(html)


def test_parse_tipo_cambio_html_vacio_lanza_parseo_sbs_error():
    with pytest.raises(ParseoSbsError):
        parse_tipo_cambio("<html><body></body></html>")
