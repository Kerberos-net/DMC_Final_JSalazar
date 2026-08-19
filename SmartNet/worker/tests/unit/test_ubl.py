"""RED primero (BACKLOG #6, WU1): `smartnet_worker.ubl` todavia no existe.

design.md, Decision 1/2: `lxml` endurecido (`resolve_entities=False, no_network=True,
load_dtd=False, dtd_validation=False, huge_tree=False`), tres compuertas ordenadas (bien-formado ->
identidad de raiz -> campos de identidad), y ninguna validacion XSD. La suite adversarial es RED
ANTES del codigo -- el parser endurecido es la respuesta, no un manejo de excepcion ad-hoc."""

from decimal import Decimal
from pathlib import Path

import pytest

from smartnet_worker.ubl import ComprobanteUbl, UblInvalidoError, parsear

_FIXTURES = Path(__file__).resolve().parent.parent / "fixtures"


def _leer_fixture(nombre: str) -> bytes:
    return (_FIXTURES / nombre).read_bytes()


# ---------------------------------------------------------------------------
# Tabla de tres compuertas + campos
# ---------------------------------------------------------------------------


def test_factura_valida_produce_comprobante_con_clave_completa():
    datos = _leer_fixture("ubl_factura_valida.xml")

    resultado = parsear(datos)

    assert isinstance(resultado, ComprobanteUbl)
    assert resultado.clave.ruc_emisor == "20123456789"
    assert resultado.clave.tipo == "01"
    assert resultado.clave.serie == "F001"
    assert resultado.clave.numero == "123"
    assert resultado.nombre_proveedor == "Proveedor Sintetico SAC"
    assert resultado.moneda == "PEN"
    assert resultado.fecha_emision.isoformat() == "2026-08-01"
    assert resultado.codigos_afectacion == ("10",)
    assert resultado.campos_no_extraidos == ()


def test_boleta_valida_tipo_03():
    datos = _leer_fixture("ubl_boleta_valida.xml")

    resultado = parsear(datos)

    assert resultado.clave.tipo == "03"
    assert resultado.clave.serie == "B001"
    assert resultado.clave.numero == "45"


def test_nota_credito_tipo_07_y_dos_codigos_de_afectacion():
    datos = _leer_fixture("ubl_notacredito_valida.xml")

    resultado = parsear(datos)

    assert resultado.clave.tipo == "07"
    # En orden de linea (design.md, Interfaces/Contracts): '10' antes que '20'.
    assert resultado.codigos_afectacion == ("10", "20")


def test_nota_debito_tipo_08():
    datos = _leer_fixture("ubl_notadebito_valida.xml")

    resultado = parsear(datos)

    assert resultado.clave.tipo == "08"


def test_monto_es_decimal_nunca_float():
    datos = _leer_fixture("ubl_factura_valida.xml")

    resultado = parsear(datos)

    assert isinstance(resultado.monto, Decimal)
    assert resultado.monto == Decimal("118.00")
    assert not isinstance(resultado.monto, float)


def test_cdr_application_response_se_rechaza_por_nombre_de_raiz_no_por_campo_faltante():
    # design.md, Decision 2, gate 2: la constancia SUNAT es UBL valido pero no es un comprobante.
    datos = _leer_fixture("ubl_cdr_applicationresponse.xml")

    with pytest.raises(UblInvalidoError, match="ApplicationResponse"):
        parsear(datos)


def test_campo_no_identidad_ausente_no_es_fatal_se_registra():
    datos = _leer_fixture("ubl_factura_valida.xml")
    # Quita el nombre del proveedor sin tocar los campos de identidad.
    datos_sin_nombre = datos.replace(
        b"<cbc:RegistrationName>Proveedor Sintetico SAC</cbc:RegistrationName>",
        b"",
    )

    resultado = parsear(datos_sin_nombre)

    assert resultado.nombre_proveedor is None
    assert "NombreProveedor" in resultado.campos_no_extraidos


def test_identidad_ausente_es_fatal():
    datos = _leer_fixture("ubl_factura_valida.xml")
    sin_id = datos.replace(b"<cbc:ID>F001-00000123</cbc:ID>", b"")

    with pytest.raises(UblInvalidoError):
        parsear(sin_id)


# ---------------------------------------------------------------------------
# Adversarial (design.md, Threat Matrix) -- RED antes del codigo.
# ---------------------------------------------------------------------------


def test_billion_laughs_no_expande():
    datos = b"""<?xml version="1.0"?>
<!DOCTYPE lolz [
 <!ENTITY lol "lol">
 <!ELEMENT lolz (#PCDATA)>
 <!ENTITY lol1 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">
 <!ENTITY lol2 "&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;">
 <!ENTITY lol3 "&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;">
]>
<lolz>&lol3;</lolz>
"""
    with pytest.raises(UblInvalidoError):
        parsear(datos)


def test_entidad_externa_no_resuelve():
    datos = b"""<?xml version="1.0"?>
<!DOCTYPE Invoice [
 <!ENTITY xxe SYSTEM "file:///etc/passwd">
]>
<Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2">&xxe;</Invoice>
"""
    with pytest.raises(UblInvalidoError):
        parsear(datos)


def test_doctype_system_no_hace_peticion():
    datos = b"""<?xml version="1.0"?>
<!DOCTYPE Invoice SYSTEM "http://ejemplo-inexistente.invalid/evil.dtd">
<Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"/>
"""
    # Si intentara resolver la red, este test colgaria o fallaria por DNS -- no_network=True lo
    # impide categoricamente, y el resultado sigue siendo UblInvalidoError (falta identidad).
    with pytest.raises(UblInvalidoError):
        parsear(datos)


def test_xml_vacio_es_permanente():
    with pytest.raises(UblInvalidoError):
        parsear(b"")


def test_html_renombrado_xml_es_permanente():
    datos = b"<html><body><h1>No es un comprobante</h1></body></html>"
    with pytest.raises(UblInvalidoError):
        parsear(datos)


def test_xml_mal_formado_lanza_xmlsyntaxerror_envuelto_en_ublinvalidoerror():
    with pytest.raises(UblInvalidoError):
        parsear(b"<Invoice><cbc:ID>no-cierra</Invoice>")
