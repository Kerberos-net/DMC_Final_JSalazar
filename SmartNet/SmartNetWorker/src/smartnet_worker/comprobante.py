"""Modulo puro de asociacion XML<->PDF (ADR 0017, BACKLOG #6).

Ni red, ni disco, ni DB, ni reloj (ADR 0019, misma regla que `gmail.py`/`sbs.py`): normaliza los
cuatro componentes de la clave de un comprobante (RUC emisor, tipo, serie, numero) y decide, por
coincidencia EXACTA, cuales pares XML<->PDF quedan asociados. Asunto, remitente, fecha y posicion
del correo NUNCA llegan hasta aqui -- este modulo ni siquiera los recibe (ADR 0017).

**Ambiguedad refuta la asociacion**: si mas de un candidato del mismo lado (XML o PDF) comparte la
misma clave normalizada, ninguno se asocia -- "Nunca se asigna a un comprobante por proximidad o
descarte" (ADR 0017). El conjunto candidato no distingue "nuevo" (de este run) de "huerfano" (de un
run anterior, todavia sin pareja): ambos se combinan antes de agrupar por clave, lo que hace la
asociacion simetrica sin importar en cual de los dos parametros llega cada documento.
"""

from __future__ import annotations

from collections import defaultdict
from collections.abc import Sequence
from dataclasses import dataclass
from re import compile as _compile_regex

_NO_DIGITO_RE = _compile_regex(r"\D+")
_ESPACIO_RE = _compile_regex(r"\s+")
_NO_ALFANUM_RE = _compile_regex(r"[^A-Za-z0-9]+")

_LONGITUD_TIPO = 2


@dataclass(frozen=True)
class ClaveComprobante:
    """Los cuatro componentes ya normalizados. Comparable por igualdad estructural: dos claves
    construidas a partir de textos crudos distintos son iguales si, tras normalizar, representan el
    mismo comprobante."""

    ruc_emisor: str
    tipo: str
    serie: str
    numero: str


@dataclass(frozen=True)
class Documento:
    """Un lado candidato de la asociacion: el `DocumentoRecibidoId` de #5, su tipo ya fijado
    ('XML'/'PDF'), y la clave que `ubl.py`/`pdf_texto.py` lograron extraer -- `None` si no se pudo
    construir una clave completa, en cuyo caso el documento nunca participa en un par."""

    documento_recibido_id: int
    tipo_documento: str
    clave: ClaveComprobante | None
    nombre_archivo: str | None = None


@dataclass(frozen=True)
class Par:
    """Un par XML<->PDF asociado. La FK se escribe en AMBOS lados (design.md, Decision 6)."""

    xml_documento_id: int
    pdf_documento_id: int


def normalizar_ruc(texto: str) -> str:
    """Solo digitos -- OCR y XML emiten '20123456789', '20123-456-789', 'R.U.C. 20123456789' por
    igual (design.md, Decision 5)."""
    return _NO_DIGITO_RE.sub("", texto)


def normalizar_tipo(texto: str) -> str:
    """2 caracteres, cero a la izquierda significativo (SUNAT catalogo 01; '1' -> '01')."""
    return texto.strip().zfill(_LONGITUD_TIPO)


def normalizar_serie(texto: str) -> str:
    """Mayusculas, espacios recortados, NUNCA rellenada -- 'F001' (electronica) y '001' (impresa)
    son namespaces distintos, no la misma serie con relleno distinto."""
    return _ESPACIO_RE.sub("", texto.strip().upper())


def normalizar_numero(texto: str) -> str:
    """Ceros a la izquierda eliminados ('00000123' -> '123') -- 003's DDL: "issuers do not always
    pad the correlativo". Sin digitos que sobrevivan el `lstrip`, el numero era todo ceros: se
    conserva un '0' unico en vez de una cadena vacia."""
    despojado = texto.strip().lstrip("0")
    return despojado or "0"


def parsear_serie_numero(numero: str) -> tuple[str, str] | None:
    """'F001-00000123' -> ('F001', '123'). Un `Numero` sin '-' no produce clave -- nunca un match
    parcial (design.md, Decision 5: la serie se parsea del campo compuesto, no vive en columna
    propia)."""
    if "-" not in numero:
        return None
    serie_cruda, numero_crudo = numero.split("-", 1)
    serie = normalizar_serie(serie_cruda)
    num = normalizar_numero(numero_crudo)
    if not serie or not num:
        return None
    return (serie, num)


def construir_clave(ruc_emisor: str, tipo: str, numero_compuesto: str) -> ClaveComprobante | None:
    """Conveniencia para `ubl.py`/`pdf_texto.py`: junta las tres normalizaciones y el parseo de
    serie-numero en una sola llamada. `None` si el campo compuesto no produce serie-numero."""
    par = parsear_serie_numero(numero_compuesto)
    if par is None:
        return None
    serie, num = par
    return ClaveComprobante(
        ruc_emisor=normalizar_ruc(ruc_emisor),
        tipo=normalizar_tipo(tipo),
        serie=serie,
        numero=num,
    )


def asociar(nuevos: Sequence[Documento], huerfanos: Sequence[Documento]) -> tuple[Par, ...]:
    """Exacto sobre los 4 componentes; >1 coincidencia en cualquiera de los dos lados => ninguna
    asociacion para esa clave (ADR 0017). `nuevos` y `huerfanos` se combinan antes de agrupar, asi
    que el resultado no depende de en cual de los dos llega cada documento (simetria)."""
    por_clave: dict[ClaveComprobante, list[Documento]] = defaultdict(list)
    for documento in (*nuevos, *huerfanos):
        if documento.clave is not None:
            por_clave[documento.clave].append(documento)

    pares: list[Par] = []
    for candidatos in por_clave.values():
        xmls = [d for d in candidatos if d.tipo_documento == "XML"]
        pdfs = [d for d in candidatos if d.tipo_documento == "PDF"]
        if len(xmls) == 1 and len(pdfs) == 1:
            pares.append(
                Par(
                    xml_documento_id=xmls[0].documento_recibido_id,
                    pdf_documento_id=pdfs[0].documento_recibido_id,
                )
            )
    return tuple(pares)


def _tokens(nombre_archivo: str) -> tuple[str, ...]:
    """Divide el nombre de archivo en tokens sobre `[^A-Za-z0-9]+` -- subsume el alfabeto crudo de
    Gmail (espacio, parentesis, `#`, `+`) y el sanitizado (`-`/`_`/`.`)."""
    return tuple(t for t in _NO_ALFANUM_RE.split(nombre_archivo) if t)


def _hay_representantes_distintos(listas: Sequence[Sequence[int]]) -> bool:
    """Sistema de representantes distintos: existe una asignacion inyectiva que da a cada componente
    una posicion de token propia. Con tres componentes chicos, backtracking directo."""
    conjuntos = sorted((set(lista) for lista in listas), key=len)
    if any(not c for c in conjuntos):
        return False

    def _rec(indice: int, usados: frozenset[int]) -> bool:
        if indice == len(conjuntos):
            return True
        return any(
            _rec(indice + 1, usados | {posicion})
            for posicion in conjuntos[indice]
            if posicion not in usados
        )

    return _rec(0, frozenset())


def _nombre_confirma_clave(clave: ClaveComprobante, tokens: Sequence[str]) -> bool:
    """El RUC de emisor, la serie y el numero de `clave` aparecen los tres como tokens delimitados
    y DISTINTOS del nombre de archivo. `tipo` NUNCA se exige (es el componente que los emisores
    mutilan). La comparacion va del XML hacia el nombre: se verifica una clave que ya existe."""
    ruc_idx = [i for i, t in enumerate(tokens) if t == clave.ruc_emisor]
    serie_idx = [i for i, t in enumerate(tokens) if normalizar_serie(t) == clave.serie]
    numero_idx = [i for i, t in enumerate(tokens) if normalizar_numero(t) == clave.numero]
    return _hay_representantes_distintos((ruc_idx, serie_idx, numero_idx))


def asociar_por_nombre_archivo(candidatos: Sequence[Documento]) -> tuple[Par, ...]:
    """Segunda pasada acotada (ADR 0017 rev. 3), fisicamente separada de `asociar`. Candidatos XML:
    `clave` completa. Candidatos PDF: `clave is None` y `nombre_archivo` presente. Empareja solo con
    exclusividad 1:1 bilateral por nodo sobre TODO el conjunto sin pareja: si mas de un XML califica
    para un PDF, o mas de un PDF para un XML, ninguno se asocia."""
    xmls = [d for d in candidatos if d.tipo_documento == "XML" and d.clave is not None]
    pdfs = [
        d
        for d in candidatos
        if d.tipo_documento == "PDF" and d.clave is None and d.nombre_archivo
    ]

    aristas: list[tuple[Documento, Documento]] = []
    for xml in xmls:
        assert xml.clave is not None
        for pdf in pdfs:
            assert pdf.nombre_archivo is not None
            if _nombre_confirma_clave(xml.clave, _tokens(pdf.nombre_archivo)):
                aristas.append((xml, pdf))

    grado_xml: dict[int, int] = defaultdict(int)
    grado_pdf: dict[int, int] = defaultdict(int)
    for xml, pdf in aristas:
        grado_xml[xml.documento_recibido_id] += 1
        grado_pdf[pdf.documento_recibido_id] += 1

    return tuple(
        Par(
            xml_documento_id=xml.documento_recibido_id,
            pdf_documento_id=pdf.documento_recibido_id,
        )
        for xml, pdf in aristas
        if grado_xml[xml.documento_recibido_id] == 1
        and grado_pdf[pdf.documento_recibido_id] == 1
    )
