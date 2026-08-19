"""IO de lectura de PDF y OCR local (design.md Decision 3/4/7, BACKLOG #6, WU2).

`pdf_lectura.py` es el unico punto de este stage que toca disco y el binario Tesseract
(`cli_procesamiento.py`, WU4, es quien lo invoca) -- `pdf_texto.py` (puro) nunca ve un `Path` ni un
proceso externo. Dos protocolos anidados (design.md, Decision 4): `MotorOcr` es el seam que
ADR 0017 exige explicitamente ("una interfaz propia con una implementacion sustituible");
`LectorPdf` es el seam adicional que hace testeable la orquestacion completa en una maquina sin
Tesseract instalado -- la suite unitaria de este modulo corre entera contra `MotorOcrFalso`, cero
llamadas reales al binario.

Capa de texto primero, OCR solo donde no hay una (design.md, Decision 3): por pagina, si
`page.extract_text()` sin espacios en blanco tiene menos de `_MINIMO_CARACTERES_PAGINA` caracteres
no-blancos, se trata como escaneada y se rasteriza a `config.OCR_DPI` (300) DPI via `pypdfium2`
antes de pasarla por `MotorOcr`. Las identidades de un comprobante viven en la primera pagina;
`_MAXIMO_PAGINAS_OCR` topa el costo de un adjunto de cientos de paginas sin convertirlo en un fallo
nuevo -- las paginas mas alla del tope simplemente no se OCRean (texto vacio), lo que deja ese
documento en el camino ya disenado de "sin pareja" (spec.md, `documentos-sin-pareja`), nunca en un
nuevo tipo de falla.

Un binario Tesseract ausente NUNCA se clasifica por documento (design.md, Decision 7): es una falla
de infraestructura, asi que `verificar_tesseract()` corre una unica vez, antes del primer documento
(`cli_procesamiento.py`, WU4), y aborta el run entero si falla."""

from __future__ import annotations

import io
import re
from pathlib import Path
from typing import Protocol

import pypdfium2 as pdfium
import pytesseract
from PIL import Image
from pypdf import PdfReader
from pypdf.errors import PdfReadError

from smartnet_worker import config

# Umbral deliberadamente asimetrico (design.md, Decision 3): una pagina corta que SI tenia texto
# simplemente se OCR de mas -- cuesta segundos, nunca correctitud. Cero falsos negativos. Se cuenta
# sobre caracteres NO-blancos (design.md: "below _MINIMO_CARACTERES_PAGINA = 100 non-whitespace
# characters") -- el texto guardado conserva sus saltos de linea internos para que pdf_texto.py
# pueda usarlos, solo el conteo del umbral descarta blancos.
_MINIMO_CARACTERES_PAGINA = 100
_ESPACIO_RE = re.compile(r"\s+")

# Un comprobante SUNAT trae sus campos de identidad en la primera pagina; un adjunto de cientos de
# paginas no debe colgar el run (design.md, Decision 3).
_MAXIMO_PAGINAS_OCR = 5


class PdfIlegibleError(Exception):
    """El PDF no se puede leer: corrupto, cifrado, o sin paginas. Se clasifica `PERMANENTE`
    (ADR 0010) -- nunca se reintenta (`errores.py` lo agrega a `_TIPOS_PERMANENTES` en este mismo
    WU)."""


class TesseractNotFoundError(Exception):
    """El binario Tesseract no esta disponible (o el idioma `spa` no esta instalado) en este host.
    Falla de infraestructura -- design.md Decision 7 exige que aborte el run entero, jamas un
    `PERMANENTE` por documento causado por una mala configuracion del host."""


class MotorOcr(Protocol):
    """El seam sustituible que ADR 0017 exige explicitamente para el motor de OCR."""

    def reconocer(self, imagen_png: bytes, idioma: str) -> str: ...


class LectorPdf(Protocol):
    """El seam que separa `cli_procesamiento.py` (WU4) del PDF real: sustituido por un fake en la
    suite unitaria del CLI, que corre sin tocar disco ni Tesseract."""

    def leer_paginas(self, ruta: Path) -> tuple[str, ...]: ...


class MotorTesseract:
    """`MotorOcr` real: `pytesseract.image_to_string` sobre los bytes PNG de una pagina ya
    rasterizada. Sin llamada de red (decision de negocio resuelta: los documentos nunca salen de la
    organizacion)."""

    def reconocer(self, imagen_png: bytes, idioma: str) -> str:
        imagen = Image.open(io.BytesIO(imagen_png))
        return pytesseract.image_to_string(imagen, lang=idioma)


class LectorPdfLocal:
    """`LectorPdf` real: `pypdf` para la capa de texto embebida y el diagnostico de
    cifrado/corrupcion; `pypdfium2` solo para rasterizar las paginas que no tienen texto, antes de
    pasarlas por `MotorOcr` (design.md, Decision 3)."""

    def __init__(self, motor_ocr: MotorOcr, idioma: str) -> None:
        self._motor_ocr = motor_ocr
        self._idioma = idioma

    def leer_paginas(self, ruta: Path) -> tuple[str, ...]:
        lector = _abrir_lector(ruta)

        if lector.is_encrypted:
            raise PdfIlegibleError(f"El PDF '{ruta.name}' esta protegido con contrasena.")
        if len(lector.pages) == 0:
            raise PdfIlegibleError(f"El PDF '{ruta.name}' no tiene paginas.")

        documento_pdfium: pdfium.PdfDocument | None = None
        textos: list[str] = []
        paginas_ocr_usadas = 0
        try:
            for indice, pagina in enumerate(lector.pages):
                texto = _texto_de_pagina(pagina, ruta)
                caracteres_no_blancos = len(_ESPACIO_RE.sub("", texto))

                if caracteres_no_blancos >= _MINIMO_CARACTERES_PAGINA:
                    textos.append(texto)
                    continue

                if paginas_ocr_usadas >= _MAXIMO_PAGINAS_OCR:
                    textos.append("")
                    continue

                if documento_pdfium is None:
                    documento_pdfium = pdfium.PdfDocument(ruta)
                imagen_png = _rasterizar_pagina(documento_pdfium, indice)
                textos.append(self._motor_ocr.reconocer(imagen_png, self._idioma))
                paginas_ocr_usadas += 1
        finally:
            if documento_pdfium is not None:
                documento_pdfium.close()

        return tuple(textos)


def _abrir_lector(ruta: Path) -> PdfReader:
    try:
        return PdfReader(ruta)
    except (PdfReadError, OSError) as error:
        raise PdfIlegibleError(
            f"PDF corrupto o formato no soportado ('{ruta.name}'): {error}"
        ) from error


def _texto_de_pagina(pagina, ruta: Path) -> str:
    # pypdf puede fallar por pagina en un documento parcialmente corrupto.
    try:
        return (pagina.extract_text() or "").strip()
    except Exception as error:
        raise PdfIlegibleError(
            f"PDF corrupto o formato no soportado ('{ruta.name}'): {error}"
        ) from error


def _rasterizar_pagina(documento: pdfium.PdfDocument, indice: int) -> bytes:
    escala = config.OCR_DPI / 72
    pagina = documento[indice]
    try:
        bitmap = pagina.render(scale=escala)
        imagen_pil = bitmap.to_pil()
        buffer = io.BytesIO()
        imagen_pil.save(buffer, format="PNG")
        return buffer.getvalue()
    finally:
        pagina.close()


def verificar_tesseract() -> None:
    """Preflight de una sola vez, antes del primer documento (design.md, Decision 7): aplica
    `config.obtener_tesseract_cmd()` si esta configurado, luego confirma que el binario responde.
    Un binario ausente o mal configurado aborta el run entero -- nunca un `PERMANENTE` por
    documento por un problema del host."""
    cmd = config.obtener_tesseract_cmd()
    if cmd:
        pytesseract.pytesseract.tesseract_cmd = cmd
    try:
        pytesseract.get_tesseract_version()
    except Exception as error:
        raise TesseractNotFoundError(
            "El binario Tesseract no esta disponible en este host. Verifique la instalacion "
            f"(o la variable {config.TESSERACT_CMD_ENV_VAR})."
        ) from error
