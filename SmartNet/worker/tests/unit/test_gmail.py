"""Suite adversarial de gmail.py — puro: ni red, ni disco, ni DB, ni reloj (design.md,
Testing Strategy). Fixtures redactadas en tests/fixtures/gmail_mensaje_*.json (ver su propio
comentario en cada archivo: formas reales de `messages.get`, direcciones/ids reemplazados por
valores redactados, PII).
"""

from __future__ import annotations

import json
from datetime import UTC, date, datetime
from pathlib import Path

import pytest

from smartnet_worker.gmail import (
    AdjuntoGmail,
    MensajeGmail,
    ParseoGmailError,
    calcular_hash,
    construir_consulta,
    es_candidato,
    extensiones_permitidas,
    parsear_mensaje,
    ruta_relativa,
    sanitizar_nombre_archivo,
)

_FIXTURES = Path(__file__).resolve().parent.parent / "fixtures"


def _leer_fixture(nombre: str) -> dict:
    return json.loads((_FIXTURES / nombre).read_text(encoding="utf-8"))


# ---------------------------------------------------------------------------------------------
# construir_consulta — task 1.5
# ---------------------------------------------------------------------------------------------


def test_construir_consulta_formatea_after_desde_fecha_iso():
    consulta = construir_consulta("Facturas", "fact-procesado", date(2026, 1, 1))

    assert consulta == "label:Facturas -label:fact-procesado after:2026/01/01"


def test_construir_consulta_cita_etiquetas_con_espacios():
    consulta = construir_consulta("Facturas 2026", "fact procesado", date(2026, 3, 5))

    assert consulta == 'label:"Facturas 2026" -label:"fact procesado" after:2026/03/05'


# ---------------------------------------------------------------------------------------------
# parsear_mensaje — task 1.6
# ---------------------------------------------------------------------------------------------


def test_parsear_mensaje_fixture_simple_extrae_remitente_asunto_y_un_adjunto():
    mensaje = _leer_fixture("gmail_mensaje_simple.json")

    resultado = parsear_mensaje(mensaje)

    assert resultado.gmail_message_id == "18f2a3b4c5d6e7f8"
    assert resultado.remitente == "Proveedor Redactado <facturacion@proveedor-redactado.example>"
    assert resultado.asunto == "Factura de agosto"
    assert resultado.fecha_recepcion == datetime(2026, 1, 15, 12, 34, 56, tzinfo=UTC)
    assert len(resultado.adjuntos) == 1
    adjunto = resultado.adjuntos[0]
    assert adjunto.nombre == "factura.pdf"
    assert adjunto.extension == "pdf"
    assert adjunto.mime_type == "application/pdf"
    assert adjunto.attachment_id == "ANGjdJ_redacted_attachment_id_1"
    assert adjunto.tamano_bytes == 154032


def test_parsear_mensaje_fixture_multipart_recorre_el_arbol_anidado_completo():
    mensaje = _leer_fixture("gmail_mensaje_multipart.json")

    resultado = parsear_mensaje(mensaje)

    assert resultado.gmail_message_id == "18f9b1c2d3e4f5a6"
    assert resultado.fecha_recepcion == datetime(2026, 2, 3, 7, 5, 9, tzinfo=UTC)
    # Tres adjuntos reales: la imagen inline (multipart/related), el XML (mismo nivel) y el PDF
    # (nivel superior) — las dos partes de texto (multipart/alternative) no tienen attachmentId y
    # nunca deben aparecer como adjunto.
    nombres = sorted(a.nombre for a in resultado.adjuntos)
    assert nombres == ["", "comprobante.pdf", "comprobante.xml"]

    inline = next(a for a in resultado.adjuntos if a.nombre == "")
    assert inline.extension == ""
    assert inline.mime_type == "image/png"

    xml = next(a for a in resultado.adjuntos if a.nombre == "comprobante.xml")
    assert xml.extension == "xml"
    assert xml.mime_type == "application/xml"


def test_parsear_mensaje_sin_header_from_lanza_parseo_gmail_error():
    mensaje = _leer_fixture("gmail_mensaje_simple.json")
    mensaje["payload"]["headers"] = [
        h for h in mensaje["payload"]["headers"] if h["name"] != "From"
    ]

    with pytest.raises(ParseoGmailError):
        parsear_mensaje(mensaje)


def test_parsear_mensaje_asunto_mayor_a_500_caracteres_se_trunca():
    mensaje = _leer_fixture("gmail_mensaje_simple.json")
    asunto_largo = "A" * 600
    for header in mensaje["payload"]["headers"]:
        if header["name"] == "Subject":
            header["value"] = asunto_largo

    resultado = parsear_mensaje(mensaje)

    assert resultado.asunto == "A" * 500
    assert len(resultado.asunto) == 500


def test_parsear_mensaje_sin_internal_date_lanza_parseo_gmail_error():
    mensaje = _leer_fixture("gmail_mensaje_simple.json")
    del mensaje["internalDate"]

    with pytest.raises(ParseoGmailError):
        parsear_mensaje(mensaje)


def test_parsear_mensaje_sin_payload_lanza_parseo_gmail_error():
    with pytest.raises(ParseoGmailError):
        parsear_mensaje({"id": "x", "internalDate": "1700000000000"})


def test_parsear_mensaje_fixture_real_capturado_extrae_xml_y_pdf():
    """gmail_mensaje_real_capturado.json es una captura REAL (via OAuth, verify post-item #5),
    no sintetica como las dos de arriba -- ver el comentario del propio fixture y
    tests/fixtures/README.md. Confirma que parsear_mensaje funciona contra la forma real de un
    correo con factura en XML+PDF, no solo contra la forma inventada."""
    mensaje = _leer_fixture("gmail_mensaje_real_capturado.json")

    resultado = parsear_mensaje(mensaje)

    assert resultado.gmail_message_id == "a1b2c3d4e5f6a7b8"
    assert resultado.asunto == "Facturas de desayuno personal"
    assert len(resultado.adjuntos) == 2
    nombres = sorted(a.nombre for a in resultado.adjuntos)
    assert nombres == [
        "85877-20127765279-fa-f96x-00001230.pdf",
        "85878-20127765279-fa-f96x-00001230.xml",
    ]
    pdf = next(a for a in resultado.adjuntos if a.extension == "pdf")
    assert pdf.mime_type == "application/pdf"
    assert pdf.tamano_bytes == 56726
    xml = next(a for a in resultado.adjuntos if a.extension == "xml")
    assert xml.mime_type == "text/xml"
    assert xml.tamano_bytes == 20372


# ---------------------------------------------------------------------------------------------
# extensiones_permitidas / es_candidato — task 1.7
# ---------------------------------------------------------------------------------------------


def test_extensiones_permitidas_separa_por_coma_recorta_y_pasa_a_minuscula():
    resultado = extensiones_permitidas(" PDF, xml ,.docx, , pdf")

    assert resultado == frozenset({"pdf", "xml", "docx"})


@pytest.mark.parametrize(
    ("nombre", "permitidas", "esperado"),
    [
        ("factura.pdf", frozenset({"pdf", "xml"}), True),
        ("factura.PDF", frozenset({"pdf", "xml"}), True),
        ("nota.docx", frozenset({"pdf", "xml"}), False),
        ("factura.pdf.exe", frozenset({"pdf", "xml"}), False),
        ("sinextension", frozenset({"pdf", "xml"}), False),
        ("", frozenset({"pdf", "xml"}), False),
    ],
)
def test_es_candidato_tabla_de_casos(nombre, permitidas, esperado):
    assert es_candidato(nombre, permitidas) is esperado


def test_es_candidato_nunca_evalua_asunto_ni_remitente():
    # es_candidato solo recibe el nombre del adjunto — no hay forma de que subject/sender influyan,
    # verificado por la propia firma de la funcion (ADR 0017).
    permitidas = frozenset({"pdf"})
    assert es_candidato("factura.pdf", permitidas) is True


# ---------------------------------------------------------------------------------------------
# calcular_hash — task 1.8
# ---------------------------------------------------------------------------------------------


def test_calcular_hash_vector_conocido_sha256_de_bytes_vacios():
    resultado = calcular_hash(b"")

    assert resultado == "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"[:64]
    assert len(resultado) == 64
    assert resultado == resultado.lower()


def test_calcular_hash_distintos_contenidos_distinto_hash():
    assert calcular_hash(b"contenido-a") != calcular_hash(b"contenido-b")


# ---------------------------------------------------------------------------------------------
# sanitizar_nombre_archivo / ruta_relativa — task 1.9, adversarial
# ---------------------------------------------------------------------------------------------


@pytest.mark.parametrize(
    ("crudo", "no_debe_contener"),
    [
        ("../../etc/passwd", ".."),
        ("..", None),
        (".", None),
        ("....", None),
        ("C:\\x", ":"),
        ("a:b", ":"),
    ],
)
def test_sanitizar_nombre_archivo_neutraliza_traversal_y_separadores(crudo, no_debe_contener):
    resultado = sanitizar_nombre_archivo(crudo)

    assert resultado != ".."
    assert resultado != "."
    assert "/" not in resultado
    assert "\\" not in resultado
    if no_debe_contener is not None:
        assert no_debe_contener not in resultado


@pytest.mark.parametrize("reservado", ["CON.pdf", "NUL", "con", "PRN.xml", "COM1", "LPT1.pdf"])
def test_sanitizar_nombre_archivo_antepone_guion_bajo_a_nombres_reservados_de_windows(reservado):
    resultado = sanitizar_nombre_archivo(reservado)

    assert resultado.startswith("_")


def test_sanitizar_nombre_archivo_trunca_nombre_de_300_caracteres():
    crudo = "a" * 300 + ".pdf"

    resultado = sanitizar_nombre_archivo(crudo)

    assert len(resultado) <= 100


def test_sanitizar_nombre_archivo_solo_emoji_no_lanza_y_produce_algo_usable():
    resultado = sanitizar_nombre_archivo("🙂🙃😀.pdf")

    assert resultado
    assert resultado != ".pdf"


def test_sanitizar_nombre_archivo_cadena_vacia_se_convierte_en_adjunto():
    assert sanitizar_nombre_archivo("") == "adjunto"


def test_sanitizar_nombre_archivo_solo_puntos_se_convierte_en_adjunto():
    assert sanitizar_nombre_archivo("....") == "adjunto"


def _mensaje_de_prueba(gmail_message_id: str = "18abc") -> MensajeGmail:
    return MensajeGmail(
        gmail_message_id=gmail_message_id,
        remitente="alguien@example.com",
        asunto="asunto",
        fecha_recepcion=datetime(2026, 1, 15, 12, 0, 0, tzinfo=UTC),
        adjuntos=(),
    )


def test_ruta_relativa_usa_anio_mes_del_mensaje_y_hash_de_ocho_caracteres():
    mensaje = _mensaje_de_prueba()
    adjunto = AdjuntoGmail(
        nombre="factura.pdf",
        extension="pdf",
        mime_type="application/pdf",
        attachment_id="a1",
        tamano_bytes=100,
    )
    hash_hex = calcular_hash(b"contenido")

    ruta = ruta_relativa(mensaje, adjunto, hash_hex)

    assert ruta.startswith("2026/01/18abc/")
    assert ruta.endswith(f"_{hash_hex[:8]}.pdf")
    assert len(ruta) <= 400
    assert all(len(componente) <= 255 for componente in ruta.split("/"))


def test_ruta_relativa_dos_adjuntos_mismo_nombre_distinto_contenido_dan_rutas_distintas():
    mensaje = _mensaje_de_prueba()
    adjunto = AdjuntoGmail(
        nombre="factura.pdf",
        extension="pdf",
        mime_type="application/pdf",
        attachment_id="a1",
        tamano_bytes=100,
    )
    hash_a = calcular_hash(b"contenido-a")
    hash_b = calcular_hash(b"contenido-b")

    ruta_a = ruta_relativa(mensaje, adjunto, hash_a)
    ruta_b = ruta_relativa(mensaje, adjunto, hash_b)

    assert ruta_a != ruta_b


def test_ruta_relativa_nombre_con_traversal_no_produce_componentes_de_directorio_ascendentes():
    mensaje = _mensaje_de_prueba()
    adjunto = AdjuntoGmail(
        nombre="../../etc/passwd.pdf",
        extension="pdf",
        mime_type="application/pdf",
        attachment_id="a1",
        tamano_bytes=100,
    )
    hash_hex = calcular_hash(b"contenido")

    ruta = ruta_relativa(mensaje, adjunto, hash_hex)

    componentes = ruta.split("/")
    assert ".." not in componentes
    assert "." not in componentes
    assert len(componentes) == 4  # yyyy / MM / gmail_message_id / archivo
