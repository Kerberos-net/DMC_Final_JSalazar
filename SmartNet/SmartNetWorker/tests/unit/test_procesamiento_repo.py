"""Cursor falso, patron `test_documento_repo.py`: SQL y parametros exactos, `fact.` calificado,
`IntegrityError -> UPDATE` en `upsert_procesamiento` (design.md, Decision 9 / Open Question 4)."""

from __future__ import annotations

from datetime import UTC, datetime
from decimal import Decimal

import pyodbc

from smartnet_worker.procesamiento_repo import (
    DatosExtraidos,
    asociar_documentos,
    contar_intentos,
    insertar_datos_extraidos,
    insertar_error,
    insertar_intento,
    listar_huerfanos,
    obtener_procesamiento_id,
    upsert_procesamiento,
)


class _FakeCursor:
    """Registra cada `execute` (sentencia + parametros) en orden. `lanzar_integrity_error_en`
    dispara la excepcion en la N-esima llamada a `execute` (1-indexada) — permite simular que el
    primer INSERT choca contra `UQ_Procesamiento_DocumentoRecibido` (014) y el segundo `execute`
    (el UPDATE de reintento) tiene exito."""

    def __init__(
        self,
        *,
        lanzar_integrity_error_en: int | None = None,
        identity: int = 1,
        filas: list[tuple] | None = None,
    ):
        self.llamadas: list[tuple[str, tuple]] = []
        self._lanzar_integrity_error_en = lanzar_integrity_error_en
        self._identity = identity
        self._filas = filas or []

    def execute(self, sentencia: str, *parametros):
        self.llamadas.append((sentencia, parametros))
        if self._lanzar_integrity_error_en == len(self.llamadas):
            raise pyodbc.IntegrityError("23000", "Violacion de restriccion UNIQUE")

    def fetchone(self):
        return (self._identity,)

    def fetchall(self):
        return self._filas


# --- upsert_procesamiento ----------------------------------------------------------------------


def test_upsert_procesamiento_inserta_en_el_primer_intento():
    cursor = _FakeCursor(identity=7)
    iniciado = datetime(2026, 8, 19, 9, 0, 0, tzinfo=UTC)
    finalizado = datetime(2026, 8, 19, 9, 0, 5, tzinfo=UTC)

    resultado = upsert_procesamiento(cursor, 1, "COMPLETADO", iniciado, finalizado)

    assert resultado == 7
    assert len(cursor.llamadas) == 1
    sentencia, parametros = cursor.llamadas[0]
    assert "insert into fact.procesamiento" in sentencia.lower()
    assert "dbo." not in sentencia.lower()
    assert parametros == (1, "COMPLETADO", iniciado, finalizado)


def test_upsert_procesamiento_actualiza_en_un_reintento_por_integrity_error():
    cursor = _FakeCursor(lanzar_integrity_error_en=1, identity=7)
    iniciado = datetime(2026, 8, 19, 9, 0, 0, tzinfo=UTC)
    finalizado = datetime(2026, 8, 19, 9, 0, 5, tzinfo=UTC)

    resultado = upsert_procesamiento(cursor, 1, "ERROR", iniciado, finalizado)

    assert resultado == 7
    assert len(cursor.llamadas) == 2
    sentencia_update, parametros_update = cursor.llamadas[1]
    assert "update fact.procesamiento" in sentencia_update.lower()
    assert parametros_update == ("ERROR", iniciado, finalizado, 1)


# --- insertar_datos_extraidos -------------------------------------------------------------------


def test_insertar_datos_extraidos_escribe_afectacionmixta():
    cursor = _FakeCursor()
    d = DatosExtraidos(
        tipo_comprobante="01",
        numero="F001-123",
        ruc_proveedor="20123456789",
        nombre_proveedor="Proveedor SAC",
        monto=Decimal("118.00"),
        moneda="PEN",
        fecha_emision=None,
        campos_no_extraidos="FechaEmision",
        afectacion_mixta=True,
    )

    insertar_datos_extraidos(cursor, 5, d)

    sentencia, parametros = cursor.llamadas[0]
    assert "insert into fact.datosextraidos" in sentencia.lower()
    assert "afectacionmixta" in sentencia.lower()
    assert parametros == (
        5,
        "01",
        "F001-123",
        "20123456789",
        "Proveedor SAC",
        Decimal("118.00"),
        "PEN",
        None,
        "FechaEmision",
        True,
    )


# --- asociar_documentos --------------------------------------------------------------------------


def test_asociar_documentos_emite_dos_update_uno_por_lado():
    cursor = _FakeCursor()

    asociar_documentos(
        cursor, procesamiento_a=10, documento_b=20, procesamiento_b=30, documento_a=40
    )

    assert len(cursor.llamadas) == 2
    sentencia_a, parametros_a = cursor.llamadas[0]
    sentencia_b, parametros_b = cursor.llamadas[1]
    assert "update fact.procesamiento" in sentencia_a.lower()
    assert "documentoasociadoid" in sentencia_a.lower()
    assert parametros_a == (20, 10)
    assert "update fact.procesamiento" in sentencia_b.lower()
    assert parametros_b == (40, 30)


# --- insertar_error / insertar_intento ------------------------------------------------------------


def test_insertar_error_literal_clasificacion_permanente():
    cursor = _FakeCursor()
    ocurrido = datetime(2026, 8, 19, 9, 0, 0, tzinfo=UTC)

    insertar_error(cursor, 1, "XML mal formado", "PERMANENTE", ocurrido)

    sentencia, parametros = cursor.llamadas[0]
    assert "insert into fact.procesamientoerror" in sentencia.lower()
    assert parametros == (1, "WORKER", "XML mal formado", "PERMANENTE", ocurrido)


def test_insertar_intento_permanente_no_agenda_reintento_proximo_reintento_null():
    cursor = _FakeCursor()
    ocurrido = datetime(2026, 8, 19, 9, 0, 0, tzinfo=UTC)

    insertar_intento(cursor, 1, 1, "FALLO", ocurrido, "XML mal formado", None)

    sentencia, parametros = cursor.llamadas[0]
    assert "insert into fact.procesamientointentos" in sentencia.lower()
    assert parametros == (1, 1, "FALLO", ocurrido, "XML mal formado", None)


# --- listar_huerfanos -----------------------------------------------------------------------------


def test_listar_huerfanos_filtra_documentoasociadoid_is_null():
    filas = [(1, "XML", "factura.xml", "20123456789", "01", "F001-00000123")]
    cursor = _FakeCursor(filas=filas)

    resultado = listar_huerfanos(cursor)

    sentencia, _ = cursor.llamadas[0]
    assert "documentoasociadoid is null" in sentencia.lower()
    assert "dr.nombrearchivo" in sentencia.lower()
    assert len(resultado) == 1
    documento = resultado[0]
    assert documento.documento_recibido_id == 1
    assert documento.tipo_documento == "XML"
    assert documento.nombre_archivo == "factura.xml"
    assert documento.clave is not None
    assert documento.clave.ruc_emisor == "20123456789"
    assert documento.clave.tipo == "01"
    assert documento.clave.serie == "F001"
    assert documento.clave.numero == "123"


def test_listar_huerfanos_sin_datos_extraidos_completos_produce_clave_none():
    filas = [(2, "PDF", "escaneo.pdf", None, None, None)]
    cursor = _FakeCursor(filas=filas)

    resultado = listar_huerfanos(cursor)

    assert resultado[0].clave is None
    assert resultado[0].nombre_archivo == "escaneo.pdf"


# --- obtener_procesamiento_id / contar_intentos (WU4: cli_procesamiento.py necesita el
# ProcesamientoId de un huerfano de un run ANTERIOR para escribir la asociacion, y el numero de
# intento previo para no pisar NumeroIntento en un reintento) -----------------------------------


def test_obtener_procesamiento_id_consulta_por_documento_recibido_id():
    cursor = _FakeCursor(identity=99)

    resultado = obtener_procesamiento_id(cursor, 5)

    assert resultado == 99
    sentencia, parametros = cursor.llamadas[0]
    assert "select" in sentencia.lower()
    assert "fact.procesamiento" in sentencia.lower()
    assert "dbo." not in sentencia.lower()
    assert parametros == (5,)


def test_contar_intentos_devuelve_cero_sin_intentos_previos():
    cursor = _FakeCursor(identity=0)

    resultado = contar_intentos(cursor, 5)

    assert resultado == 0
    sentencia, parametros = cursor.llamadas[0]
    assert "count" in sentencia.lower()
    assert "fact.procesamientointentos" in sentencia.lower()
    assert parametros == (5,)


def test_contar_intentos_devuelve_el_conteo_existente():
    cursor = _FakeCursor(identity=2)

    resultado = contar_intentos(cursor, 5)

    assert resultado == 2
