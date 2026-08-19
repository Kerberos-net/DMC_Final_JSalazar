"""Cursor falso, patron `test_tipo_cambio_repo.py`: SQL y parametros exactos, `'CANDIDATO'`/
`'DESCARGADO'` literales, `IntegrityError -> None` (design.md, Decision 4)."""

from __future__ import annotations

from datetime import UTC, datetime

import pyodbc

from smartnet_worker.documento_repo import insertar_documento, insertar_email
from smartnet_worker.gmail import AdjuntoGmail, MensajeGmail


class _FakeCursor:
    """Registra cada `execute` (sentencia + parametros) en orden, y responde `fetchone` con un
    valor configurable — `insertar_email` lee el id generado con `OUTPUT INSERTED.EmailId` en el
    MISMO `execute` que el INSERT (nunca un `SELECT SCOPE_IDENTITY()` separado — ver
    `documento_repo.py` para el porque)."""

    def __init__(self, *, lanzar_integrity_error: bool = False, identity: int = 42):
        self.llamadas: list[tuple[str, tuple]] = []
        self._lanzar_integrity_error = lanzar_integrity_error
        self._identity = identity

    def execute(self, sentencia: str, *parametros):
        self.llamadas.append((sentencia, parametros))
        if self._lanzar_integrity_error:
            raise pyodbc.IntegrityError("23000", "Violacion de restriccion UNIQUE")

    def fetchone(self):
        return (self._identity,)

    @property
    def sentencia(self) -> str:
        return self.llamadas[0][0]

    @property
    def parametros(self) -> tuple:
        return self.llamadas[0][1]


def _mensaje() -> MensajeGmail:
    return MensajeGmail(
        gmail_message_id="18d2f0a1b2c3d4e5",
        remitente="proveedor@example.com",
        asunto="Factura de agosto",
        fecha_recepcion=datetime(2026, 8, 17, 9, 15, 0, tzinfo=UTC),
        adjuntos=(),
    )


def _adjunto() -> AdjuntoGmail:
    return AdjuntoGmail(
        nombre="factura.pdf",
        extension="pdf",
        mime_type="application/pdf",
        attachment_id="ANGjdJ_abc123",
        tamano_bytes=12345,
    )


# --- insertar_email ---------------------------------------------------------------------------


def test_insertar_email_no_menciona_dbo_en_el_sql_emitido():
    cursor = _FakeCursor()

    insertar_email(cursor, _mensaje(), datetime(2026, 8, 17, 9, 16, 0, tzinfo=UTC))

    assert "dbo." not in cursor.sentencia.lower()
    assert "fact.email" in cursor.sentencia.lower()


def test_insertar_email_fija_estado_candidato_de_forma_hardcodeada_sin_parametro():
    cursor = _FakeCursor()
    m = _mensaje()
    fecha_deteccion = datetime(2026, 8, 17, 9, 16, 0, tzinfo=UTC)

    insertar_email(cursor, m, fecha_deteccion)

    assert "'candidato'" in cursor.sentencia.lower()
    # 5 parametros: GmailMessageId, Remitente, Asunto, FechaRecepcion, FechaDeteccion — Estado
    # NO viaja como parametro.
    assert cursor.parametros == (
        m.gmail_message_id,
        m.remitente,
        m.asunto,
        m.fecha_recepcion,
        fecha_deteccion,
    )


def test_insertar_email_retorna_el_id_generado_en_insercion_exitosa():
    cursor = _FakeCursor(identity=99)

    resultado = insertar_email(cursor, _mensaje(), datetime(2026, 8, 17, 9, 16, 0, tzinfo=UTC))

    assert resultado == 99


def test_insertar_email_lee_el_id_con_output_en_el_mismo_execute_que_el_insert():
    cursor = _FakeCursor(identity=7)

    insertar_email(cursor, _mensaje(), datetime(2026, 8, 17, 9, 16, 0, tzinfo=UTC))

    # Un unico execute: el INSERT con OUTPUT INSERTED.EmailId — nunca un SELECT SCOPE_IDENTITY()
    # en un execute separado (ese patron devuelve NULL con pyodbc, ver documento_repo.py).
    assert len(cursor.llamadas) == 1
    assert "output inserted.emailid" in cursor.llamadas[0][0].lower()


def test_insertar_email_retorna_none_cuando_ya_existe_gmail_message_id_integrity_error():
    cursor = _FakeCursor(lanzar_integrity_error=True)

    resultado = insertar_email(cursor, _mensaje(), datetime(2026, 8, 17, 9, 16, 0, tzinfo=UTC))

    assert resultado is None
    assert len(cursor.llamadas) == 1


# --- insertar_documento ------------------------------------------------------------------------


def test_insertar_documento_no_menciona_dbo_en_el_sql_emitido():
    cursor = _FakeCursor()

    insertar_documento(
        cursor, 1, _mensaje(), _adjunto(), "a" * 64, "2026/08/18d2/factura_aaaa1111.pdf"
    )

    assert "dbo." not in cursor.sentencia.lower()
    assert "fact.documentorecibido" in cursor.sentencia.lower()


def test_insertar_documento_fija_estado_descargado_de_forma_hardcodeada_sin_parametro():
    cursor = _FakeCursor()
    m = _mensaje()
    a = _adjunto()
    hash_hex = "b" * 64
    ruta = "2026/08/18d2f0a1b2c3d4e5/factura_bbbbbbbb.pdf"

    insertar_documento(cursor, 1, m, a, hash_hex, ruta)

    assert "'descargado'" in cursor.sentencia.lower()
    assert cursor.parametros == (
        1,
        m.gmail_message_id,
        a.nombre,
        a.extension,
        a.mime_type,
        a.tamano_bytes,
        hash_hex,
        ruta,
    )


def test_insertar_documento_no_lanza_cuando_ya_existe_email_hash_integrity_error():
    cursor = _FakeCursor(lanzar_integrity_error=True)

    resultado = insertar_documento(cursor, 1, _mensaje(), _adjunto(), "c" * 64, "ruta.pdf")

    assert resultado is None


def test_insertar_documento_trunca_nombre_archivo_a_255_caracteres():
    cursor = _FakeCursor()
    nombre_largo = "a" * 300 + ".pdf"
    adjunto_largo = AdjuntoGmail(
        nombre=nombre_largo,
        extension="pdf",
        mime_type="application/pdf",
        attachment_id="x",
        tamano_bytes=1,
    )

    insertar_documento(cursor, 1, _mensaje(), adjunto_largo, "d" * 64, "ruta.pdf")

    nombre_archivo_param = cursor.parametros[2]
    assert len(nombre_archivo_param) == 255
