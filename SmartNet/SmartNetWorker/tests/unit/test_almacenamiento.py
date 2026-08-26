from pathlib import Path

import pytest

from smartnet_worker.almacenamiento import ContencionError, escribir


def test_escribir_crea_directorios_intermedios_y_escribe_los_bytes(tmp_path: Path):
    raiz = tmp_path / "volumen"
    raiz.mkdir()

    destino = escribir(raiz, "2026/08/msg123/factura_ab12cd34.pdf", b"contenido-pdf")

    assert destino == raiz / "2026" / "08" / "msg123" / "factura_ab12cd34.pdf"
    assert destino.read_bytes() == b"contenido-pdf"


def test_escribir_reescribir_la_misma_ruta_es_un_no_op_idempotente(tmp_path: Path):
    raiz = tmp_path / "volumen"
    raiz.mkdir()

    primera = escribir(raiz, "2026/08/msg123/factura_ab12cd34.pdf", b"contenido-pdf")
    segunda = escribir(raiz, "2026/08/msg123/factura_ab12cd34.pdf", b"contenido-pdf")

    assert primera == segunda
    assert primera.read_bytes() == b"contenido-pdf"


def test_escribir_rechaza_una_ruta_relativa_que_escapa_la_raiz(tmp_path: Path):
    raiz = tmp_path / "volumen"
    raiz.mkdir()

    with pytest.raises(ContencionError):
        escribir(raiz, "../fuera/archivo.pdf", b"x")

    # nada se escribio fuera de la raiz
    assert not (tmp_path / "fuera").exists()


def test_escribir_rechaza_una_ruta_absoluta_que_pisa_la_guarda_de_contencion(tmp_path: Path):
    raiz = tmp_path / "volumen"
    raiz.mkdir()
    otro_lugar = tmp_path / "otro" / "archivo.pdf"

    with pytest.raises(ContencionError):
        escribir(raiz, str(otro_lugar), b"x")

    assert not otro_lugar.exists()
