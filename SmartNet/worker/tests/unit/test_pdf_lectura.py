"""RED primero (BACKLOG #6, WU2): `smartnet_worker.pdf_lectura` todavia no existe.

design.md, Decision 3/4: capa de texto primero, OCR solo por pagina donde no hay una
(`_MINIMO_CARACTERES_PAGINA = 100`), rasterizada a 300 DPI; `_MAXIMO_PAGINAS_OCR = 5` topa el costo;
`MotorOcr` es el seam sustituible que ADR 0017 exige, `LectorPdf` es el seam que hace testeable la
orquestacion sin Tesseract instalado (la suite entera corre con `MotorOcrFalso`, cero llamadas
reales al binario)."""

from pathlib import Path

import pytest

from smartnet_worker.pdf_lectura import LectorPdfLocal, PdfIlegibleError

_FIXTURES = Path(__file__).resolve().parent.parent / "fixtures"


class MotorOcrFalso:
    """`MotorOcr` falso: registra cada llamada (imagen, idioma) y devuelve un texto fijo — la
    suite unitaria nunca invoca Tesseract real (design.md, Decision 4)."""

    def __init__(self, texto_fijo: str = "TEXTO OCR FALSO") -> None:
        self.texto_fijo = texto_fijo
        self.llamadas: list[tuple[bytes, str]] = []

    def reconocer(self, imagen_png: bytes, idioma: str) -> str:
        self.llamadas.append((imagen_png, idioma))
        return self.texto_fijo


def test_pagina_con_capa_de_texto_nunca_invoca_el_motor_ocr():
    motor = MotorOcrFalso()
    lector = LectorPdfLocal(motor_ocr=motor, idioma="spa")

    paginas = lector.leer_paginas(_FIXTURES / "comprobante_con_texto.pdf")

    assert len(paginas) == 1
    assert "RUC: 20123456789" in paginas[0]
    assert motor.llamadas == []


def test_pagina_sin_capa_de_texto_se_ocr_a_exactamente_esa_pagina():
    motor = MotorOcrFalso(texto_fijo="RUC: 20123456789 F001-00000123")
    lector = LectorPdfLocal(motor_ocr=motor, idioma="spa")

    paginas = lector.leer_paginas(_FIXTURES / "comprobante_escaneado.pdf")

    assert len(paginas) == 1
    assert paginas[0] == "RUC: 20123456789 F001-00000123"
    assert len(motor.llamadas) == 1
    _imagen_png, idioma = motor.llamadas[0]
    assert idioma == "spa"


def test_pdf_cifrado_lanza_pdf_ilegible_error(tmp_path):
    from pypdf import PdfWriter

    escritor = PdfWriter()
    escritor.add_blank_page(width=100, height=100)
    escritor.encrypt(user_password="secreto")
    ruta = tmp_path / "cifrado.pdf"
    with ruta.open("wb") as archivo:
        escritor.write(archivo)

    lector = LectorPdfLocal(motor_ocr=MotorOcrFalso(), idioma="spa")

    with pytest.raises(PdfIlegibleError):
        lector.leer_paginas(ruta)


def test_pdf_corrupto_lanza_pdf_ilegible_error(tmp_path):
    ruta = tmp_path / "corrupto.pdf"
    ruta.write_bytes(b"esto no es un PDF valido")

    lector = LectorPdfLocal(motor_ocr=MotorOcrFalso(), idioma="spa")

    with pytest.raises(PdfIlegibleError):
        lector.leer_paginas(ruta)


def test_pdf_sin_paginas_lanza_pdf_ilegible_error(tmp_path):
    from pypdf import PdfWriter

    escritor = PdfWriter()
    ruta = tmp_path / "sin_paginas.pdf"
    with ruta.open("wb") as archivo:
        escritor.write(archivo)

    lector = LectorPdfLocal(motor_ocr=MotorOcrFalso(), idioma="spa")

    with pytest.raises(PdfIlegibleError):
        lector.leer_paginas(ruta)


def test_tope_de_paginas_ocr_se_respeta(tmp_path):
    from pypdf import PdfWriter

    escritor = PdfWriter()
    for _ in range(7):
        escritor.add_blank_page(width=200, height=200)
    ruta = tmp_path / "siete_paginas_en_blanco.pdf"
    with ruta.open("wb") as archivo:
        escritor.write(archivo)

    motor = MotorOcrFalso()
    lector = LectorPdfLocal(motor_ocr=motor, idioma="spa")

    paginas = lector.leer_paginas(ruta)

    assert len(paginas) == 7
    # _MAXIMO_PAGINAS_OCR = 5 (design.md, Decision 3): las paginas 6 y 7 no se OCRean, quedan
    # como texto vacio en vez de intentar un sexto/septimo llamado al motor.
    assert len(motor.llamadas) == 5
    assert paginas[5] == ""
    assert paginas[6] == ""
