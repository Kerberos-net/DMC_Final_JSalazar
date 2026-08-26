"""Modulo puro de parseo XML/UBL (ADR 0017, BACKLOG #6, design.md Decision 1/2).

Ni red, ni disco, ni DB, ni reloj (ADR 0019): recibe los bytes ya leidos por el punto de IO
(`cli_procesamiento.py`, WU4) y decide. El parser es explicitamente endurecido porque el XML es
atacante-influenciado -- cualquiera puede escribirle al buzon monitoreado (mismo framing de amenaza
que #5 aplico al nombre del adjunto):

- `resolve_entities=False` — ninguna entidad interna se expande (billion laughs).
- `no_network=True` — ninguna entidad/DTD externa dispara una peticion de red (documentos nunca
  salen de la organizacion, decision de negocio resuelta).
- `load_dtd=False`, `dtd_validation=False` — el DTD ni se carga ni se valida.
- `huge_tree=False` — sin arboles gigantes que agoten memoria.

"Es esto un comprobante SUNAT?" son tres compuertas ORDENADAS, sin validacion XSD (Decision 2):

1. Bien-formado (`etree.fromstring` con el parser endurecido).
2. Identidad de documento: la raiz debe ser `{ns}Invoice`, `{ns}CreditNote` o `{ns}DebitNote`
   (UBL 2.1). La constancia SUNAT (`ApplicationResponse`, el CDR) es UBL valido pero NO es un
   comprobante -- se rechaza por NOMBRE, con ese nombre en el mensaje, nunca por "falta un campo".
3. Campos de identidad presentes: `cbc:ID` (serie-numero), RUC del emisor, tipo de comprobante.

Los campos NO-identidad (`NombreProveedor`, `Monto`, `Moneda`, `FechaEmision`) nunca son fatales:
uno ausente se agrega por nombre a `campos_no_extraidos` (ADR 0010's asimetria de costo: un
PERMANENTE detiene un documento, se reserva para lo que nunca puede producir un comprobante)."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import date
from decimal import Decimal, InvalidOperation

from lxml import etree

from smartnet_worker.comprobante import ClaveComprobante, construir_clave

_NS = {
    "cac": "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2",
    "cbc": "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2",
}

# UBL 2.1: la raiz identifica el documento. El tipo de comprobante SOLO se lee del elemento
# InvoiceTypeCode en Invoice -- las notas lo llevan en el nombre de su propia raiz (design.md,
# Decision 2), asi que el mapeo es tabla, no una unica XPath.
_RAIZ_INVOICE = "{urn:oasis:names:specification:ubl:schema:xsd:Invoice-2}Invoice"
_RAIZ_CREDIT_NOTE = "{urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2}CreditNote"
_RAIZ_DEBIT_NOTE = "{urn:oasis:names:specification:ubl:schema:xsd:DebitNote-2}DebitNote"

_TIPO_FIJO_POR_RAIZ = {
    _RAIZ_CREDIT_NOTE: "07",
    _RAIZ_DEBIT_NOTE: "08",
}

_LINEA_POR_RAIZ = {
    _RAIZ_INVOICE: "cac:InvoiceLine",
    _RAIZ_CREDIT_NOTE: "cac:CreditNoteLine",
    _RAIZ_DEBIT_NOTE: "cac:DebitNoteLine",
}

_XPATH_ID = "cbc:ID/text()"
_XPATH_RUC_EMISOR = (
    "cac:AccountingSupplierParty/cac:Party/cac:PartyIdentification/cbc:ID/text()"
)
_XPATH_NOMBRE_PROVEEDOR = (
    "cac:AccountingSupplierParty/cac:Party/cac:PartyLegalEntity/cbc:RegistrationName/text()"
)
_XPATH_TIPO_COMPROBANTE_INVOICE = "cbc:InvoiceTypeCode/text()"
_XPATH_MONEDA = "cbc:DocumentCurrencyCode/text()"
_XPATH_FECHA_EMISION = "cbc:IssueDate/text()"
_XPATH_MONTO = (
    "(cac:LegalMonetaryTotal/cbc:PayableAmount"
    "|cac:RequestedMonetaryTotal/cbc:PayableAmount)/text()"
)
_XPATH_CODIGO_AFECTACION_SUFIJO = (
    "cac:TaxTotal/cac:TaxSubtotal/cac:TaxCategory/cbc:TaxExemptionReasonCode/text()"
)

# Endurecido (design.md, Decision 1): estas keywords exactas son lo que una suite estructural
# (WU3, test_no_dbo_structural.py) puede afirmar por texto literal.
_PARSER = etree.XMLParser(
    resolve_entities=False,
    no_network=True,
    load_dtd=False,
    dtd_validation=False,
    huge_tree=False,
)


class UblInvalidoError(Exception):
    """El documento no es un comprobante SUNAT valido: mal formado, raiz no reconocida (incluida
    la constancia SUNAT `ApplicationResponse`), o le faltan campos de identidad. Se clasifica
    `PERMANENTE` (ADR 0010) -- nunca se reintenta."""


@dataclass(frozen=True)
class ComprobanteUbl:
    clave: ClaveComprobante
    nombre_proveedor: str | None
    monto: Decimal | None
    moneda: str | None
    fecha_emision: date | None
    codigos_afectacion: tuple[str, ...]
    campos_no_extraidos: tuple[str, ...]


def parsear(datos: bytes) -> ComprobanteUbl:
    raiz = _parsear_raiz(datos)
    _verificar_identidad_de_documento(raiz)

    numero_compuesto = _texto_unico(raiz, _XPATH_ID)
    ruc_emisor = _texto_unico(raiz, _XPATH_RUC_EMISOR)
    tipo = _resolver_tipo(raiz)

    if not numero_compuesto or not ruc_emisor or not tipo:
        raise UblInvalidoError(
            "El documento no es un comprobante: faltan campos de identidad "
            "(cbc:ID, RUC del emisor o tipo de comprobante)."
        )

    clave = construir_clave(ruc_emisor, tipo, numero_compuesto)
    if clave is None:
        raise UblInvalidoError(
            f"El campo compuesto 'Numero' ({numero_compuesto!r}) no produce serie-numero."
        )

    campos_no_extraidos: list[str] = []

    nombre_proveedor = _texto_unico(raiz, _XPATH_NOMBRE_PROVEEDOR)
    if not nombre_proveedor:
        nombre_proveedor = None
        campos_no_extraidos.append("NombreProveedor")

    moneda = _texto_unico(raiz, _XPATH_MONEDA)
    if not moneda:
        moneda = None
        campos_no_extraidos.append("Moneda")

    fecha_emision_texto = _texto_unico(raiz, _XPATH_FECHA_EMISION)
    fecha_emision = _parsear_fecha(fecha_emision_texto)
    if fecha_emision is None:
        campos_no_extraidos.append("FechaEmision")

    monto = _parsear_monto(_texto_unico(raiz, _XPATH_MONTO))
    if monto is None:
        campos_no_extraidos.append("Monto")

    codigos_afectacion = _extraer_codigos_afectacion(raiz)

    return ComprobanteUbl(
        clave=clave,
        nombre_proveedor=nombre_proveedor,
        monto=monto,
        moneda=moneda,
        fecha_emision=fecha_emision,
        codigos_afectacion=codigos_afectacion,
        campos_no_extraidos=tuple(campos_no_extraidos),
    )


def _parsear_raiz(datos: bytes):
    if not datos:
        raise UblInvalidoError("El documento XML esta vacio (0 bytes).")
    try:
        return etree.fromstring(datos, parser=_PARSER)
    except etree.XMLSyntaxError as error:
        raise UblInvalidoError(f"XML mal formado: {error}") from error


def _verificar_identidad_de_documento(raiz) -> None:
    if raiz.tag not in _LINEA_POR_RAIZ:
        raise UblInvalidoError(
            f"Raiz '{raiz.tag}' no es un comprobante SUNAT reconocido "
            "(Invoice/CreditNote/DebitNote UBL 2.1)."
        )


def _resolver_tipo(raiz) -> str | None:
    if raiz.tag in _TIPO_FIJO_POR_RAIZ:
        return _TIPO_FIJO_POR_RAIZ[raiz.tag]
    return _texto_unico(raiz, _XPATH_TIPO_COMPROBANTE_INVOICE)


def _texto_unico(raiz, xpath: str) -> str | None:
    resultados = raiz.xpath(xpath, namespaces=_NS)
    if not resultados:
        return None
    valor = str(resultados[0]).strip()
    return valor or None


def _parsear_fecha(texto: str | None) -> date | None:
    if not texto:
        return None
    try:
        return date.fromisoformat(texto)
    except ValueError:
        return None


def _parsear_monto(texto: str | None) -> Decimal | None:
    if not texto:
        return None
    try:
        return Decimal(texto)
    except InvalidOperation:
        return None


def _extraer_codigos_afectacion(raiz) -> tuple[str, ...]:
    elemento_linea = _LINEA_POR_RAIZ[raiz.tag]
    codigos: list[str] = []
    for linea in raiz.xpath(elemento_linea, namespaces=_NS):
        codigos.extend(linea.xpath(_XPATH_CODIGO_AFECTACION_SUFIJO, namespaces=_NS))
    return tuple(str(c).strip() for c in codigos if str(c).strip())
