"""Pruebas de integracion reales (marker `integracion`) — pyodbc real como `usr_worker` contra
una base efimera con el esquema versionado aplicado (design.md, Testing Strategy / tasks.md 3.9 y
4.7/4.8).

NOTA DE EJECUCION (ver README.md, "Limitaciones conocidas"): el subconjunto SBS (item #4) fue
ejecutado y quedo en verde en un entorno previo contra una instancia SQL Server 2025 local real,
con el esquema completo aplicado via `SmartNet.Db.Runner` y un LOGIN `usr_worker` efimero real. El
subconjunto Gmail (item #5, agregado aqui) sigue el mismo arnes (`conftest.py::worker_db`) — ver
README.md para el resultado real de esta tanda de implementacion.
"""

from __future__ import annotations

from datetime import UTC, date, datetime
from decimal import Decimal

import pyodbc
import pytest

from smartnet_worker.documento_repo import insertar_documento, insertar_email
from smartnet_worker.estado_integracion import registrar_exito
from smartnet_worker.gmail import AdjuntoGmail, MensajeGmail
from smartnet_worker.sbs import TipoCambioSbs
from smartnet_worker.tipo_cambio_repo import insertar_sbs

pytestmark = pytest.mark.integracion


def _tipo_cambio_de_hoy() -> TipoCambioSbs:
    return TipoCambioSbs(
        fecha=date.today(),
        compra=Decimal("3.798000"),
        venta=Decimal("3.802000"),
        fecha_consulta=datetime.now(UTC),
    )


def test_insertar_sbs_real_inserta_la_fila_de_hoy(worker_db):
    tc = _tipo_cambio_de_hoy()

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        cursor = conexion.cursor()
        resultado = insertar_sbs(cursor, tc)
        conexion.commit()

    assert resultado is True

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        fila = conexion.cursor().execute(
            "SELECT COUNT(*) FROM fact.TipoCambio WHERE Fecha = ? AND Origen = 'SBS'", tc.fecha
        ).fetchone()

    assert fila[0] == 1


def test_insertar_sbs_duplicado_para_la_misma_fecha_devuelve_false(worker_db):
    tc = _tipo_cambio_de_hoy()

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        cursor = conexion.cursor()
        insertar_sbs(cursor, tc)
        conexion.commit()

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        cursor = conexion.cursor()
        resultado = insertar_sbs(cursor, tc)
        conexion.commit()

    assert resultado is False


def test_registrar_exito_actualiza_exactamente_una_fila_de_estado_integracion(worker_db):
    instante = datetime.now(UTC)

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        cursor = conexion.cursor()
        registrar_exito(cursor, "SBS", instante)
        conexion.commit()

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        fila = conexion.cursor().execute(
            "SELECT COUNT(*) FROM fact.EstadoIntegracion "
            "WHERE Nombre = 'SBS' AND UltimoExito IS NOT NULL"
        ).fetchone()

    assert fila[0] == 1


# --- BACKLOG #5: Gmail (documento_repo.py, estado_integracion.py Nombre='GMAIL') -----------------


def _mensaje_gmail(gmail_message_id: str) -> MensajeGmail:
    return MensajeGmail(
        gmail_message_id=gmail_message_id,
        remitente="proveedor@example.com",
        asunto="Factura de agosto",
        fecha_recepcion=datetime(2026, 8, 17, 9, 15, 0, tzinfo=UTC),
        adjuntos=(),
    )


def _adjunto_gmail(nombre: str = "factura.pdf") -> AdjuntoGmail:
    return AdjuntoGmail(
        nombre=nombre,
        extension="pdf",
        mime_type="application/pdf",
        attachment_id="ANGjdJ_abc123",
        tamano_bytes=12345,
    )


def test_insertar_email_real_inserta_y_el_duplicado_devuelve_none(worker_db):
    m = _mensaje_gmail("18d2f0a1b2c3d4e5")
    fecha_deteccion = datetime.now(UTC)

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        cursor = conexion.cursor()
        primer_id = insertar_email(cursor, m, fecha_deteccion)
        conexion.commit()

    assert primer_id is not None

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        cursor = conexion.cursor()
        segundo_id = insertar_email(cursor, m, fecha_deteccion)
        conexion.commit()

    # UQ_Email_GmailMessageId (003_ingesta_y_procesamiento.sql) rechaza el duplicado; el
    # adaptador lo traduce a None, nunca lanza (design.md, Decision 4).
    assert segundo_id is None

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        fila = conexion.cursor().execute(
            "SELECT COUNT(*) FROM fact.Email WHERE GmailMessageId = ?", m.gmail_message_id
        ).fetchone()

    assert fila[0] == 1


def test_insertar_documento_real_referencia_el_email_por_fk(worker_db):
    m = _mensaje_gmail("18d2f0a1b2c3d4e6")
    a = _adjunto_gmail()

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        cursor = conexion.cursor()
        email_id = insertar_email(cursor, m, datetime.now(UTC))
        assert email_id is not None
        insertar_documento(cursor, email_id, m, a, "a" * 64, "2026/08/18d2f0a1b2c3d4e6/factura.pdf")
        conexion.commit()

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        fila = conexion.cursor().execute(
            "SELECT EmailId, Estado FROM fact.DocumentoRecibido WHERE HashContenido = ?", "a" * 64
        ).fetchone()

    assert fila is not None
    assert fila[0] == email_id
    assert fila[1] == "DESCARGADO"


def test_registrar_exito_con_nombre_gmail_afecta_exactamente_una_fila(worker_db):
    instante = datetime.now(UTC)

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        cursor = conexion.cursor()
        registrar_exito(cursor, "GMAIL", instante)
        conexion.commit()

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        fila = conexion.cursor().execute(
            "SELECT COUNT(*) FROM fact.EstadoIntegracion "
            "WHERE Nombre = 'GMAIL' AND UltimoExito IS NOT NULL"
        ).fetchone()

    assert fila[0] == 1


def test_usr_worker_no_puede_escribir_en_configuracion(worker_db):
    """Negativa: `008_usuarios_y_permisos.sql` solo otorga SELECT a `fact_worker` sobre
    `fact.Configuracion` (`GRANT SELECT ON OBJECT::fact.Configuracion TO fact_worker;`) — solo
    .NET (`fact_api`) escribe esa tabla (design.md, spec.md Non-Goals)."""
    with pytest.raises(pyodbc.Error):
        with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
            conexion.cursor().execute(
                "UPDATE fact.Configuracion SET Valor = 'Facturas' "
                "WHERE Seccion = 'INGESTA' AND Clave = 'ETIQUETA_ORIGEN'"
            )
            conexion.commit()
