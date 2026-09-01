"""Suite del orquestador `cli_procesamiento.py` (design.md, Testing Strategy) — `LectorPdf` falso +
cursor falso, sin disco real, sin red, sin Tesseract real. Cubre: XML antes que PDF; XML presente
=> cero llamadas al lector de PDF; fallo de un documento no aborta el run; preflight de Tesseract
fallido => 0 filas escritas; `PERMANENTE` nunca agenda reintento; coincidencia ambigua de 4
componentes deja ambos documentos sin asociar (design.md, Decision 2/6/7/8).
"""

from __future__ import annotations

from datetime import UTC, datetime
from pathlib import Path

import pyodbc

from smartnet_worker.cli_procesamiento import ejecutar

_INSTANTE = datetime(2026, 8, 19, 10, 0, 0, tzinfo=UTC)

_XML_FACTURA_VALIDA = b"""<?xml version="1.0" encoding="UTF-8"?>
<Invoice xmlns="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2"
         xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2"
         xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
    <cbc:ID>F001-00000123</cbc:ID>
    <cbc:IssueDate>2026-08-15</cbc:IssueDate>
    <cbc:InvoiceTypeCode>01</cbc:InvoiceTypeCode>
    <cbc:DocumentCurrencyCode>PEN</cbc:DocumentCurrencyCode>
    <cac:AccountingSupplierParty>
        <cac:Party>
            <cac:PartyIdentification><cbc:ID>20123456789</cbc:ID></cac:PartyIdentification>
            <cac:PartyLegalEntity><cbc:RegistrationName>Proveedor SAC</cbc:RegistrationName>
            </cac:PartyLegalEntity>
        </cac:Party>
    </cac:AccountingSupplierParty>
    <cac:LegalMonetaryTotal><cbc:PayableAmount>118.00</cbc:PayableAmount></cac:LegalMonetaryTotal>
    <cac:InvoiceLine>
        <cac:TaxTotal><cac:TaxSubtotal><cac:TaxCategory>
            <cbc:TaxExemptionReasonCode>10</cbc:TaxExemptionReasonCode>
        </cac:TaxCategory></cac:TaxSubtotal></cac:TaxTotal>
    </cac:InvoiceLine>
</Invoice>
"""

_XML_MAL_FORMADO = b"<Invoice><cbc:ID>roto</Invoice>"


def _documento_pendiente(
    *,
    documento_recibido_id: int,
    email_id: int,
    extension: str,
    nombre_archivo: str,
    ruta_relativa: str,
):
    from smartnet_worker.documento_repo import DocumentoPendiente

    return DocumentoPendiente(
        documento_recibido_id=documento_recibido_id,
        email_id=email_id,
        gmail_message_id=f"msg-{documento_recibido_id}",
        nombre_archivo=nombre_archivo,
        extension=extension,
        mime_type="application/xml" if extension == "xml" else "application/pdf",
        tamano_bytes=100,
        hash_contenido="a" * 64,
        ruta_relativa=ruta_relativa,
    )


class _LectorPdfFalso:
    """Sustituye `LectorPdf`: nunca toca disco ni Tesseract. Registra cada llamada."""

    def __init__(self, *, paginas_por_ruta: dict[str, tuple[str, ...]] | None = None):
        self._paginas_por_ruta = paginas_por_ruta or {}
        self.llamadas: list[str] = []

    def leer_paginas(self, ruta: Path) -> tuple[str, ...]:
        self.llamadas.append(str(ruta))
        return self._paginas_por_ruta.get(str(ruta), ("",))


class _FakeCursor:
    """Cursor falso compartido por todas las fases del run (config, lectura de pendientes,
    por-documento, asociacion, estado final) — dispatch por substring del SQL en minusculas, mismo
    patron que `test_cli_gmail.py::_FakeCursor`."""

    def __init__(
        self,
        *,
        ruc_propio: str | None = None,
        pendientes_filas: list[tuple] | None = None,
        huerfanos_filas: list[tuple] | None = None,
        eventos: list[str] | None = None,
        procesamiento_id_por_documento: dict[int, int] | None = None,
    ):
        self._ruc_propio = ruc_propio
        self._pendientes_filas = pendientes_filas or []
        self._huerfanos_filas = huerfanos_filas or []
        self.eventos = eventos if eventos is not None else []
        self._procesamiento_id_por_documento = procesamiento_id_por_documento or {}
        self._siguiente_procesamiento_id = 1000
        self._ultimo_fetchone: tuple | None = None
        self._ultimo_fetchall: list[tuple] = []
        self.rowcount = 1

    def execute(self, sentencia: str, *parametros):
        sql = sentencia.lower()
        self.eventos.append(sql.split()[0] + ":" + sql[:40])

        if "select valor from fact.configuracion" in sql:
            self._ultimo_fetchone = (self._ruc_propio,) if self._ruc_propio else None
            return
        if "from fact.documentorecibido" in sql and "select" in sql:
            self._ultimo_fetchall = list(self._pendientes_filas)
            return
        if "insert into fact.procesamiento " in sql or "insert into fact.procesamiento\n" in sql:
            documento_id = parametros[0]
            procesamiento_id = self._procesamiento_id_por_documento.get(
                documento_id, self._siguiente_procesamiento_id
            )
            self._procesamiento_id_por_documento[documento_id] = procesamiento_id
            self._siguiente_procesamiento_id += 1
            self._ultimo_fetchone = (procesamiento_id,)
            self.eventos.append(f"upsert_procesamiento_insert:{documento_id}")
            return
        if "update fact.procesamiento" in sql and "documentoasociadoid" in sql:
            self.eventos.append(f"asociar_documentos:{parametros}")
            return
        if "update fact.procesamiento" in sql and "output inserted.procesamientoid" in sql:
            documento_id = parametros[-1]
            procesamiento_id = self._procesamiento_id_por_documento.get(
                documento_id, self._siguiente_procesamiento_id
            )
            self._procesamiento_id_por_documento[documento_id] = procesamiento_id
            self._siguiente_procesamiento_id += 1
            self._ultimo_fetchone = (procesamiento_id,)
            self.eventos.append(f"upsert_procesamiento_update:{documento_id}")
            return
        if "select procesamientoid from fact.procesamiento" in sql:
            documento_id = parametros[0]
            self._ultimo_fetchone = (self._procesamiento_id_por_documento[documento_id],)
            return
        if "select count(*) from fact.procesamientointentos" in sql:
            self._ultimo_fetchone = (0,)
            return
        if "insert into fact.datosextraidos" in sql:
            self.eventos.append("insertar_datos_extraidos")
            return
        if "update fact.documentorecibido" in sql and "tipodocumento" in sql:
            self.eventos.append(f"fijar_tipo_documento:{parametros}")
            return
        if "update fact.documentorecibido" in sql and "set estado" in sql:
            self.eventos.append(f"fijar_estado_documento:{parametros}")
            return
        if "insert into fact.procesamientointentos" in sql:
            # (procesamiento_id, numero_intento, resultado, ocurrido, detalle, proximo_reintento)
            self.eventos.append(f"insertar_intento:{parametros}")
            return
        if "insert into fact.procesamientoerror" in sql:
            self.eventos.append(f"insertar_error:{parametros[3]}")  # Clasificacion
            return
        if "update fact.email" in sql:
            self.eventos.append("refrescar_estado_email")
            return
        if "select p.documentorecibidoid" in sql:
            self._ultimo_fetchall = list(self._huerfanos_filas)
            return
        if "update fact.estadointegracion" in sql:
            self.rowcount = 1
            self.eventos.append(f"estado_integracion:{parametros[-1]}")
            return

    def fetchone(self):
        return self._ultimo_fetchone

    def fetchall(self):
        return self._ultimo_fetchall


class _FakeConexion:
    def __init__(self, cursor: _FakeCursor, eventos: list[str]):
        self._cursor = cursor
        self._eventos = eventos

    def cursor(self) -> _FakeCursor:
        return self._cursor

    def commit(self) -> None:
        self._eventos.append("commit")

    def rollback(self) -> None:
        self._eventos.append("rollback")

    def close(self) -> None:
        self._eventos.append("close")


def _preparar_entorno(monkeypatch, tmp_path: Path) -> None:
    monkeypatch.setenv("SMARTNET_WORKER_ODBC_CONNECTION", "DRIVER={fake};")
    monkeypatch.setenv("SMARTNET_WORKER_STORAGE_ROOT", str(tmp_path))


def _conectar_fabrica(cursor: _FakeCursor, eventos: list[str]):
    def _conectar(_connection_string: str):
        return _FakeConexion(cursor, eventos)

    return _conectar


def _verificar_tesseract_ok() -> None:
    return None


def _escribir_archivo(tmp_path: Path, ruta_relativa: str, datos: bytes) -> None:
    destino = tmp_path / ruta_relativa
    destino.parent.mkdir(parents=True, exist_ok=True)
    destino.write_bytes(datos)


def test_xml_se_procesa_antes_que_pdf(monkeypatch, tmp_path: Path):
    _preparar_entorno(monkeypatch, tmp_path)
    eventos: list[str] = []
    _escribir_archivo(tmp_path, "xml/factura.xml", _XML_FACTURA_VALIDA)
    _escribir_archivo(tmp_path, "pdf/otro.pdf", b"pdf")

    pendientes_filas = [
        (2, 20, "msg-2", "otro.pdf", "pdf", "application/pdf", 3, "b" * 64, "pdf/otro.pdf"),
        (1, 10, "msg-1", "factura.xml", "xml", "application/xml", 100, "a" * 64, "xml/factura.xml"),
    ]
    cursor = _FakeCursor(pendientes_filas=pendientes_filas, eventos=eventos)
    lector = _LectorPdfFalso()

    resultado = ejecutar(
        lector=lector,
        conectar=_conectar_fabrica(cursor, eventos),
        verificar_tesseract=_verificar_tesseract_ok,
        instante=_INSTANTE,
    )

    assert resultado == 0
    indice_insert_xml = next(
        i for i, e in enumerate(eventos) if e == "upsert_procesamiento_insert:1"
    )
    indice_insert_pdf = next(
        i for i, e in enumerate(eventos) if e == "upsert_procesamiento_insert:2"
    )
    assert indice_insert_xml < indice_insert_pdf


def test_xml_presente_no_invoca_al_lector_de_pdf(monkeypatch, tmp_path: Path):
    _preparar_entorno(monkeypatch, tmp_path)
    eventos: list[str] = []
    _escribir_archivo(tmp_path, "xml/factura.xml", _XML_FACTURA_VALIDA)

    pendientes_filas = [
        (1, 10, "msg-1", "factura.xml", "xml", "application/xml", 100, "a" * 64, "xml/factura.xml"),
    ]
    cursor = _FakeCursor(pendientes_filas=pendientes_filas, eventos=eventos)
    lector = _LectorPdfFalso()

    resultado = ejecutar(
        lector=lector,
        conectar=_conectar_fabrica(cursor, eventos),
        verificar_tesseract=_verificar_tesseract_ok,
        instante=_INSTANTE,
    )

    assert resultado == 0
    assert lector.llamadas == []


def test_fallo_de_un_documento_no_aborta_el_run(monkeypatch, tmp_path: Path):
    _preparar_entorno(monkeypatch, tmp_path)
    eventos: list[str] = []
    _escribir_archivo(tmp_path, "xml/roto.xml", _XML_MAL_FORMADO)
    _escribir_archivo(tmp_path, "xml/bueno.xml", _XML_FACTURA_VALIDA)

    pendientes_filas = [
        (1, 10, "msg-1", "roto.xml", "xml", "application/xml", 30, "a" * 64, "xml/roto.xml"),
        (2, 20, "msg-2", "bueno.xml", "xml", "application/xml", 100, "b" * 64, "xml/bueno.xml"),
    ]
    cursor = _FakeCursor(pendientes_filas=pendientes_filas, eventos=eventos)
    lector = _LectorPdfFalso()

    resultado = ejecutar(
        lector=lector,
        conectar=_conectar_fabrica(cursor, eventos),
        verificar_tesseract=_verificar_tesseract_ok,
        instante=_INSTANTE,
    )

    # El run entero se reporta en fallo (al menos un documento fallo) pero AMBOS documentos se
    # intentaron -- el fallo del primero no impidio procesar el segundo.
    assert resultado == 1
    assert any(e == "upsert_procesamiento_insert:1" for e in eventos)
    assert any(e == "upsert_procesamiento_insert:2" for e in eventos)
    assert any(e.startswith("insertar_error:PERMANENTE") for e in eventos)
    assert any(e.startswith("fijar_estado_documento:('PROCESADO', 2)") for e in eventos)


def test_preflight_tesseract_fallido_aborta_el_run_con_cero_filas_escritas(
    monkeypatch, tmp_path: Path
):
    _preparar_entorno(monkeypatch, tmp_path)
    eventos: list[str] = []

    def _verificar_tesseract_falla() -> None:
        from smartnet_worker.pdf_lectura import TesseractNotFoundError

        raise TesseractNotFoundError("tesseract no encontrado")

    cursor = _FakeCursor(eventos=eventos)
    lector = _LectorPdfFalso()

    resultado = ejecutar(
        lector=lector,
        conectar=_conectar_fabrica(cursor, eventos),
        verificar_tesseract=_verificar_tesseract_falla,
        instante=_INSTANTE,
    )

    assert resultado == 1
    # Ninguna escritura de negocio -- el UNICO evento de escritura es el EstadoIntegracion de
    # fallo del run completo.
    escrituras = [
        e
        for e in eventos
        if e.startswith("upsert_procesamiento")
        or e == "insertar_datos_extraidos"
        or e.startswith("fijar_tipo_documento")
        or e.startswith("fijar_estado_documento")
        or e.startswith("insertar_intento")
        or e.startswith("insertar_error")
    ]
    assert escrituras == []
    assert any(e.startswith("estado_integracion:") for e in eventos)


def test_xml_invalido_es_permanente_y_no_agenda_reintento(monkeypatch, tmp_path: Path):
    _preparar_entorno(monkeypatch, tmp_path)
    eventos: list[str] = []
    _escribir_archivo(tmp_path, "xml/roto.xml", _XML_MAL_FORMADO)

    pendientes_filas = [
        (1, 10, "msg-1", "roto.xml", "xml", "application/xml", 30, "a" * 64, "xml/roto.xml"),
    ]
    cursor = _FakeCursor(pendientes_filas=pendientes_filas, eventos=eventos)
    lector = _LectorPdfFalso()

    resultado = ejecutar(
        lector=lector,
        conectar=_conectar_fabrica(cursor, eventos),
        verificar_tesseract=_verificar_tesseract_ok,
        instante=_INSTANTE,
    )

    assert resultado == 1
    intento_evento = next(e for e in eventos if e.startswith("insertar_intento:"))
    # insertar_intento(procesamiento_id, numero_intento, resultado, ocurrido, detalle,
    # proximo_reintento) -- el ultimo elemento del tuple de parametros es proximo_reintento.
    assert intento_evento.rstrip(")").endswith("None")


def test_coincidencia_ambigua_deja_ambos_documentos_sin_asociar(monkeypatch, tmp_path: Path):
    """Dos huerfanos PDF con la MISMA clave que un huerfano XML -- ADR 0017: >1 candidato del
    mismo lado => ninguno se asocia. Ningun `asociar_documentos` (UPDATE...DocumentoAsociadoId) se
    emite."""
    _preparar_entorno(monkeypatch, tmp_path)
    eventos: list[str] = []

    huerfanos_filas = [
        (1, "XML", "factura.xml", "20123456789", "01", "F001-00000123"),
        (2, "PDF", "a.pdf", "20123456789", "01", "F001-00000123"),
        (3, "PDF", "b.pdf", "20123456789", "01", "F001-00000123"),
    ]
    cursor = _FakeCursor(huerfanos_filas=huerfanos_filas, eventos=eventos)
    lector = _LectorPdfFalso()

    resultado = ejecutar(
        lector=lector,
        conectar=_conectar_fabrica(cursor, eventos),
        verificar_tesseract=_verificar_tesseract_ok,
        instante=_INSTANTE,
    )

    assert resultado == 0
    assert not any("documentoasociadoid" in e for e in eventos)


def test_segunda_pasada_asocia_pdf_sin_clave_por_containment_del_nombre(
    monkeypatch, tmp_path: Path
):
    """El PDF huerfano no produjo clave propia; un XML huerfano con clave completa lo reclama
    porque su RUC + serie + numero aparecen como tokens distintos del nombre de archivo del PDF.
    La pasada exacta de 4 componentes no los empareja (el PDF no tiene clave)."""
    _preparar_entorno(monkeypatch, tmp_path)
    eventos: list[str] = []

    huerfanos_filas = [
        (1, "XML", "factura.xml", "20127765279", "01", "F96X-00001230"),
        (2, "PDF", "85877-20127765279-fa-f96x-00001230.pdf", None, None, None),
    ]
    cursor = _FakeCursor(
        huerfanos_filas=huerfanos_filas,
        eventos=eventos,
        procesamiento_id_por_documento={1: 501, 2: 502},
    )
    lector = _LectorPdfFalso()

    resultado = ejecutar(
        lector=lector,
        conectar=_conectar_fabrica(cursor, eventos),
        verificar_tesseract=_verificar_tesseract_ok,
        instante=_INSTANTE,
    )

    assert resultado == 0
    # asociar_documentos emite dos UPDATE ... DocumentoAsociadoId, uno por lado.
    updates_asociacion = [e for e in eventos if e.startswith("asociar_documentos:")]
    assert len(updates_asociacion) == 2


def test_pasada_exacta_de_cuatro_componentes_no_cambia_con_la_segunda_pasada(
    monkeypatch, tmp_path: Path
):
    """Regresion: un PDF que SI produjo su propia clave completa se empareja por la pasada exacta,
    nunca entra al residuo de la segunda pasada."""
    _preparar_entorno(monkeypatch, tmp_path)
    eventos: list[str] = []

    huerfanos_filas = [
        (1, "XML", "factura.xml", "20127765279", "01", "F96X-00001230"),
        (2, "PDF", "cualquier-nombre.pdf", "20127765279", "01", "F96X-00001230"),
    ]
    cursor = _FakeCursor(
        huerfanos_filas=huerfanos_filas,
        eventos=eventos,
        procesamiento_id_por_documento={1: 501, 2: 502},
    )
    lector = _LectorPdfFalso()

    resultado = ejecutar(
        lector=lector,
        conectar=_conectar_fabrica(cursor, eventos),
        verificar_tesseract=_verificar_tesseract_ok,
        instante=_INSTANTE,
    )

    assert resultado == 0
    updates_asociacion = [e for e in eventos if e.startswith("asociar_documentos:")]
    assert len(updates_asociacion) == 2


def test_base64_import_no_usado_sanity(monkeypatch, tmp_path: Path):
    # Sanity: pyodbc.IntegrityError sigue disponible para quien extienda estos fakes.
    assert issubclass(pyodbc.IntegrityError, Exception)
