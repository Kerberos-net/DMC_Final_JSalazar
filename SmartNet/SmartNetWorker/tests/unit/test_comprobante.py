"""RED primero (BACKLOG #6, WU1): `smartnet_worker.comprobante` todavia no existe."""

from smartnet_worker.comprobante import (
    ClaveComprobante,
    Documento,
    asociar,
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
