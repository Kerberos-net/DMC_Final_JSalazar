"""RED primero (BACKLOG #6, WU1): `smartnet_worker.comprobante` todavia no existe."""

from smartnet_worker.comprobante import (
    ClaveComprobante,
    Documento,
    asociar,
    asociar_por_nombre_archivo,
    normalizar_numero,
    normalizar_ruc,
    normalizar_serie,
    normalizar_tipo,
    parsear_serie_numero,
)


def test_normalizar_numero_ignora_ceros_a_la_izquierda():
    # '00000123' == '123' -- 003's DDL: "VARCHAR because issuers do not always pad the correlativo".
    assert normalizar_numero("00000123") == normalizar_numero("123") == "123"


def test_normalizar_serie_nunca_rellena_con_ceros():
    # 'F001' (electronica) != '001' (impresa) -- namespaces distintos, no la misma serie rellenada.
    assert normalizar_serie("F001") != normalizar_serie("001")
    assert normalizar_serie("F001") == "F001"
    assert normalizar_serie("  f001  ") == "F001"


def test_normalizar_tipo_agrega_cero_a_la_izquierda_significativo():
    # SUNAT catalogo 01 tiene un cero a la izquierda significativo (003's own DDL comment).
    assert normalizar_tipo("1") == "01"
    assert normalizar_tipo("01") == "01"


def test_normalizar_ruc_conserva_solo_digitos():
    assert normalizar_ruc("20123-456-789") == "20123456789"
    assert normalizar_ruc("R.U.C. 20123456789") == "20123456789"


def test_parsear_serie_numero_separa_el_campo_compuesto():
    assert parsear_serie_numero("F001-00000123") == ("F001", "123")


def test_parsear_serie_numero_sin_guion_no_produce_clave():
    # Un Numero sin '-' no produce clave -- nunca un match parcial (Decision 5/design.md).
    assert parsear_serie_numero("F00100000123") is None


def _clave(ruc="20123456789", tipo="01", serie="F001", numero="123") -> ClaveComprobante:
    return ClaveComprobante(ruc_emisor=ruc, tipo=tipo, serie=serie, numero=numero)


def _par(xml_id: int, pdf_id: int):
    from smartnet_worker.comprobante import Par

    return Par(xml_documento_id=xml_id, pdf_documento_id=pdf_id)


def test_asociar_par_exacto_de_cuatro_componentes():
    xml = Documento(documento_recibido_id=1, tipo_documento="XML", clave=_clave())
    pdf = Documento(documento_recibido_id=2, tipo_documento="PDF", clave=_clave())

    pares = asociar(nuevos=[xml], huerfanos=[pdf])

    assert len(pares) == 1
    assert pares[0].xml_documento_id == 1
    assert pares[0].pdf_documento_id == 2


def test_asociar_es_simetrica_sin_importar_el_lado_nuevo_o_huerfano():
    xml = Documento(documento_recibido_id=1, tipo_documento="XML", clave=_clave())
    pdf = Documento(documento_recibido_id=2, tipo_documento="PDF", clave=_clave())

    pares_a = asociar(nuevos=[xml], huerfanos=[pdf])
    pares_b = asociar(nuevos=[pdf], huerfanos=[xml])

    assert pares_a == pares_b


def test_asociar_mas_de_un_candidato_no_asocia_ninguno():
    # ADR 0017: "Nunca se asigna a un comprobante por proximidad o descarte." -- ambiguedad refuta.
    xml = Documento(documento_recibido_id=1, tipo_documento="XML", clave=_clave())
    pdf_a = Documento(documento_recibido_id=2, tipo_documento="PDF", clave=_clave())
    pdf_b = Documento(documento_recibido_id=3, tipo_documento="PDF", clave=_clave())

    pares = asociar(nuevos=[xml], huerfanos=[pdf_a, pdf_b])

    assert pares == ()


def test_asociar_sin_clave_no_participa():
    xml = Documento(documento_recibido_id=1, tipo_documento="XML", clave=None)
    pdf = Documento(documento_recibido_id=2, tipo_documento="PDF", clave=_clave())

    pares = asociar(nuevos=[xml], huerfanos=[pdf])

    assert pares == ()


def test_asociar_claves_distintas_no_asocia():
    xml = Documento(documento_recibido_id=1, tipo_documento="XML", clave=_clave(serie="F001"))
    pdf = Documento(documento_recibido_id=2, tipo_documento="PDF", clave=_clave(serie="F002"))

    pares = asociar(nuevos=[xml], huerfanos=[pdf])

    assert pares == ()


# --- asociar_por_nombre_archivo (ADR 0017 rev. 3, segunda pasada acotada) ----------------------

_CLAVE_XML = ClaveComprobante(
    ruc_emisor="20127765279", tipo="01", serie="F96X", numero="1230"
)


def _xml(documento_recibido_id: int, clave: ClaveComprobante | None = _CLAVE_XML) -> Documento:
    return Documento(
        documento_recibido_id=documento_recibido_id, tipo_documento="XML", clave=clave
    )


def _pdf(documento_recibido_id: int, nombre_archivo: str) -> Documento:
    return Documento(
        documento_recibido_id=documento_recibido_id,
        tipo_documento="PDF",
        clave=None,
        nombre_archivo=nombre_archivo,
    )


def test_containment_inequivoco_asocia_con_la_clave_del_xml_como_autoridad():
    xml = _xml(1)
    pdf = _pdf(2, "85877-20127765279-fa-f96x-00001230.pdf")

    pares = asociar_por_nombre_archivo([xml, pdf])

    assert pares == (_par(1, 2),)


def test_tipo_token_ausente_o_no_estandar_igual_asocia():
    xml = _xml(1)
    pdf = _pdf(2, "20127765279 F96X 00001230.pdf")

    assert asociar_por_nombre_archivo([xml, pdf]) == (_par(1, 2),)


def test_mas_de_un_xml_califica_para_un_pdf_no_asocia_ninguno():
    xml_a = _xml(1)
    xml_b = _xml(3)  # misma clave normalizada, distinto DocumentoRecibidoId
    pdf = _pdf(2, "20127765279-f96x-00001230.pdf")

    assert asociar_por_nombre_archivo([xml_a, xml_b, pdf]) == ()


def test_mas_de_un_pdf_califica_para_un_xml_no_asocia_ninguno():
    xml = _xml(1)
    pdf_a = _pdf(2, "20127765279-f96x-00001230.pdf")
    pdf_b = _pdf(4, "adjunto-20127765279-f96x-1230.pdf")

    assert asociar_por_nombre_archivo([xml, pdf_a, pdf_b]) == ()


def test_token_casi_igual_no_matchea():
    # design.md D2: `12300` normaliza a `12300` != `1230` -> refuta. `01230` NO es near-miss: la
    # regla de `normalizar_numero` (elimina ceros a la izquierda, misma que hace matchear
    # `00001230`) lo iguala a `1230` deliberadamente (CLAUDE.md regla 1: reconciliacion explicita
    # del parentetico del spec contra la regla de D2).
    xml = _xml(1)
    pdf = _pdf(2, "20127765279-f96x-12300.pdf")
    assert asociar_por_nombre_archivo([xml, pdf]) == ()


def test_numero_con_ceros_a_la_izquierda_si_matchea():
    xml = _xml(1)
    pdf = _pdf(2, "20127765279-f96x-00001230.pdf")
    assert asociar_por_nombre_archivo([xml, pdf]) == (_par(1, 2),)


def test_un_solo_token_no_cuenta_como_serie_y_numero_a_la_vez():
    xml = _xml(1, ClaveComprobante(ruc_emisor="20127765279", tipo="01", serie="001", numero="1"))
    pdf = _pdf(2, "20127765279-001.pdf")

    assert asociar_por_nombre_archivo([xml, pdf]) == ()


def test_xml_con_clave_incompleta_nunca_es_candidato():
    xml = _xml(1, clave=None)
    pdf = _pdf(2, "85877-20127765279-fa-f96x-00001230.pdf")

    assert asociar_por_nombre_archivo([xml, pdf]) == ()


def test_pdf_con_clave_propia_no_entra_en_la_segunda_pasada():
    xml = _xml(1)
    pdf = Documento(
        documento_recibido_id=2,
        tipo_documento="PDF",
        clave=_CLAVE_XML,
        nombre_archivo="20127765279-fa-f96x-00001230.pdf",
    )

    assert asociar_por_nombre_archivo([xml, pdf]) == ()


def test_nombre_crudo_con_espacios_y_parentesis_tokeniza():
    xml = _xml(1)
    pdf = _pdf(2, "factura 85877 (20127765279) fa f96x 00001230.pdf")

    assert asociar_por_nombre_archivo([xml, pdf]) == (_par(1, 2),)
