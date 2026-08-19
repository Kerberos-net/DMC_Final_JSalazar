"""Pruebas de integracion reales del binario Tesseract (marker `ocr`, nuevo — a diferencia de
`externa`, SI corre en CI: `apt-get install tesseract-ocr tesseract-ocr-spa`, design.md Testing
Strategy). `comprobante_escaneado.pdf` (tests/fixtures/, WU2) no tiene capa de texto embebida --
`LectorPdfLocal` debe rasterizarlo y pasarlo por `MotorTesseract` de verdad.

Las aserciones son sobre los CAMPOS EXTRAIDOS (RUC, serie, numero), nunca sobre el texto exacto
que Tesseract devuelve — la version de `tesseract-ocr-spa` de `apt` puede diferir de la instalada
localmente, y el objetivo de esta prueba es probar la integracion (rasterizacion -> OCR ->
extraccion), no fijar el output literal de un motor de terceros (design.md, nota bajo la fila
`ocr` de la Testing Strategy)."""

from __future__ import annotations

from pathlib import Path

import pytest

from smartnet_worker import config, pdf_texto
from smartnet_worker.pdf_lectura import LectorPdfLocal, MotorTesseract, TesseractNotFoundError
from smartnet_worker.pdf_lectura import verificar_tesseract as _verificar_tesseract

pytestmark = pytest.mark.ocr

_FIXTURE = Path(__file__).resolve().parents[1] / "fixtures" / "comprobante_escaneado.pdf"


@pytest.fixture(autouse=True)
def _requiere_tesseract_real():
    try:
        _verificar_tesseract()
    except TesseractNotFoundError as error:
        pytest.skip(f"Tesseract no esta disponible en este host: {error}")


def test_comprobante_escaneado_se_lee_via_ocr_real_y_extrae_los_campos_de_identidad():
    assert _FIXTURE.exists(), f"Fixture no encontrada: {_FIXTURE}"

    lector = LectorPdfLocal(MotorTesseract(), config.OCR_IDIOMA)
    paginas = lector.leer_paginas(_FIXTURE)
    texto = "\n".join(paginas)

    extraccion = pdf_texto.extraer(texto, _FIXTURE.name)

    # Solo se afirma sobre los campos que el extractor logra reconstruir, nunca sobre el texto
    # crudo que devolvio Tesseract (la version de `tesseract-ocr-spa` de CI puede variar).
    assert extraccion.clave is not None, (
        f"OCR real no produjo una clave completa (RUC/tipo/serie/numero). Texto reconocido: "
        f"{texto[:300]!r}"
    )
    assert len(extraccion.clave.ruc_emisor) == 11
    assert extraccion.clave.ruc_emisor.isdigit()
    assert extraccion.clave.serie
    assert extraccion.clave.numero
