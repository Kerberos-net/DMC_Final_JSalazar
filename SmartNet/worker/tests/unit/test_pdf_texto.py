"""RED primero (BACKLOG #6, WU2): `smartnet_worker.pdf_texto` todavia no existe.

design.md, Decision 5/6 y proposal.md's Open Question 1 (resuelta): regex sobre texto ya extraido
(nunca toca disco/DB/reloj — ADR 0019), content-first con respaldo estricto todo-o-nada en el
nombre de archivo SUNAT, y resolucion de dos RUC en el mismo texto usando el parametro opcional
`ruc_propio` (viene de `fact.Configuracion` `EMPRESA.RUC`, migracion 014 — este modulo puro nunca
lee la base de datos)."""

from datetime import date
from decimal import Decimal

from smartnet_worker.pdf_texto import ExtraccionPdf, extraer

_RUC_PROVEEDOR = "20123456789"
_RUC_EMPRESA_PROPIA = "20999999999"


def _texto_comprobante_completo() -> str:
    return (
        "RUC: 20123456789\n"
        "F001-00000123\n"
        "FACTURA ELECTRONICA\n"
        "TOTAL A PAGAR: S/ 118.00\n"
        "15/03/2026\n"
    )


# ---------------------------------------------------------------------------
# RUC junto a la etiqueta, con y sin puntos
# ---------------------------------------------------------------------------


def test_ruc_con_etiqueta_sin_puntos_produce_clave():
    resultado = extraer(_texto_comprobante_completo(), "cualquier_nombre.pdf")

    assert isinstance(resultado, ExtraccionPdf)
    assert resultado.clave is not None
    assert resultado.clave.ruc_emisor == _RUC_PROVEEDOR
    assert resultado.clave.tipo == "01"
    assert resultado.clave.serie == "F001"
    assert resultado.clave.numero == "123"


def test_ruc_con_etiqueta_r_punto_u_punto_c_punto_produce_clave():
    texto = _texto_comprobante_completo().replace("RUC: 20123456789", "R.U.C. : 20123456789")

    resultado = extraer(texto, "cualquier_nombre.pdf")

    assert resultado.clave is not None
    assert resultado.clave.ruc_emisor == _RUC_PROVEEDOR


# ---------------------------------------------------------------------------
# Dos RUC en el mismo texto (Open Question 1, resuelta)
# ---------------------------------------------------------------------------


def test_dos_rucs_se_resuelven_excluyendo_el_ruc_propio_configurado():
    texto = (
        f"RUC EMISOR: {_RUC_PROVEEDOR}\n"
        f"RUC CLIENTE: {_RUC_EMPRESA_PROPIA}\n"
        "F001-00000123\n"
        "FACTURA ELECTRONICA\n"
    )

    resultado = extraer(texto, "cualquier_nombre.pdf", ruc_propio=_RUC_EMPRESA_PROPIA)

    assert resultado.clave is not None
    assert resultado.clave.ruc_emisor == _RUC_PROVEEDOR


def test_dos_rucs_sin_ruc_propio_configurado_no_produce_clave_por_texto():
    # ADR 0017: sin una senal no-inferencial (el RUC propio configurado), no hay forma de elegir
    # entre dos RUC por proximidad de etiqueta -- el nombre de archivo tampoco calza aqui, asi que
    # la clave queda en None.
    texto = (
        f"RUC EMISOR: {_RUC_PROVEEDOR}\n"
        f"RUC CLIENTE: {_RUC_EMPRESA_PROPIA}\n"
        "F001-00000123\n"
        "FACTURA ELECTRONICA\n"
    )

    resultado = extraer(texto, "nombre_no_sunat.pdf", ruc_propio=None)

    assert resultado.clave is None


def test_dos_rucs_con_ruc_propio_que_no_hace_match_con_ninguno_no_produce_clave():
    texto = (
        f"RUC EMISOR: {_RUC_PROVEEDOR}\n"
        f"RUC CLIENTE: {_RUC_EMPRESA_PROPIA}\n"
        "F001-00000123\n"
        "FACTURA ELECTRONICA\n"
    )

    resultado = extraer(texto, "nombre_no_sunat.pdf", ruc_propio="20111111111")

    assert resultado.clave is None


# ---------------------------------------------------------------------------
# Serie-numero con y sin espacios
# ---------------------------------------------------------------------------


def test_serie_numero_sin_espacios():
    texto = f"RUC: {_RUC_PROVEEDOR}\nF001-00000123\nFACTURA ELECTRONICA\n"

    resultado = extraer(texto, "cualquier_nombre.pdf")

    assert resultado.clave.serie == "F001"
    assert resultado.clave.numero == "123"


def test_serie_numero_con_espacios_alrededor_del_guion():
    texto = f"RUC: {_RUC_PROVEEDOR}\nF001 - 00000123\nFACTURA ELECTRONICA\n"

    resultado = extraer(texto, "cualquier_nombre.pdf")

    assert resultado.clave.serie == "F001"
    assert resultado.clave.numero == "123"


# ---------------------------------------------------------------------------
# Respaldo de nombre de archivo SUNAT (content-first, filename-as-backup)
# ---------------------------------------------------------------------------


def test_respaldo_de_nombre_de_archivo_produce_clave_cuando_el_texto_no_alcanza():
    texto_sin_datos = "documento escaneado sin capa de texto reconocible"

    resultado = extraer(texto_sin_datos, "20123456789-01-F001-00000123.pdf")

    assert resultado.clave is not None
    assert resultado.clave.ruc_emisor == _RUC_PROVEEDOR
    assert resultado.clave.tipo == "01"
    assert resultado.clave.serie == "F001"
    assert resultado.clave.numero == "123"


def test_respaldo_de_nombre_de_archivo_parcial_no_produce_clave():
    # Todo-o-nada (design.md, Decision 6): falta el segmento NUMERO -- ningun match parcial.
    texto_sin_datos = "documento escaneado sin capa de texto reconocible"

    resultado = extraer(texto_sin_datos, "20123456789-01-F001.pdf")

    assert resultado.clave is None
    assert "Clave" in resultado.campos_no_extraidos


def test_respaldo_de_nombre_de_archivo_no_se_usa_si_el_texto_ya_produjo_clave():
    # Content-first: el texto ya basta, el nombre de archivo (que no calzaria con el patron SUNAT)
    # nunca se consulta.
    resultado = extraer(_texto_comprobante_completo(), "correo_adjunto_1.pdf")

    assert resultado.clave is not None
    assert resultado.clave.ruc_emisor == _RUC_PROVEEDOR


# ---------------------------------------------------------------------------
# Monto, moneda, fecha (campos no-identidad)
# ---------------------------------------------------------------------------


def test_monto_moneda_y_fecha_se_extraen_del_texto():
    resultado = extraer(_texto_comprobante_completo(), "cualquier_nombre.pdf")

    assert resultado.monto == Decimal("118.00")
    assert resultado.moneda == "PEN"
    assert resultado.fecha_emision == date(2026, 3, 15)
    assert resultado.campos_no_extraidos == ()


def test_campos_no_extraidos_registra_ausencias_sin_ser_fatal():
    texto = f"RUC: {_RUC_PROVEEDOR}\nF001-00000123\nFACTURA ELECTRONICA\n"

    resultado = extraer(texto, "cualquier_nombre.pdf")

    assert resultado.clave is not None
    assert resultado.monto is None
    assert resultado.moneda is None
    assert resultado.fecha_emision is None
    assert set(resultado.campos_no_extraidos) == {"Monto", "Moneda", "FechaEmision"}
