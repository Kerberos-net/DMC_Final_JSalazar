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

from smartnet_worker.comprobante import asociar_por_nombre_archivo
from smartnet_worker.documento_repo import insertar_documento, insertar_email
from smartnet_worker.estado_integracion import registrar_exito
from smartnet_worker.gmail import AdjuntoGmail, MensajeGmail
from smartnet_worker.inbox_event_repo import (
    insertar_evento,
    insertar_evento_asociacion,
    listar_asociacion_no_notificada,
    listar_no_notificados,
)
from smartnet_worker.payload_inbox import construir_payload
from smartnet_worker.procesamiento_repo import (
    DatosExtraidos,
    asociar_documentos,
    insertar_datos_extraidos,
    listar_huerfanos,
    upsert_procesamiento,
)
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


# --- BACKLOG #6: extraccion y asociacion (procesamiento_repo.py, migracion 014) -----------------


def _email_y_documento_reales(cursor, gmail_message_id: str, nombre_archivo: str) -> int:
    """Inserta un `Email`+`DocumentoRecibido` real via `documento_repo.py` (mismo camino que #5) y
    devuelve el `DocumentoRecibidoId` — `fact.Procesamiento.DocumentoRecibidoId` es NOT NULL con FK
    real, asi que las pruebas de este item necesitan una fila padre real, nunca un id inventado."""
    m = MensajeGmail(
        gmail_message_id=gmail_message_id,
        remitente="proveedor@example.com",
        asunto="Factura de agosto",
        fecha_recepcion=datetime(2026, 8, 17, 9, 15, 0, tzinfo=UTC),
        adjuntos=(),
    )
    a = AdjuntoGmail(
        nombre=nombre_archivo,
        extension=nombre_archivo.rsplit(".", 1)[-1],
        mime_type="application/xml" if nombre_archivo.endswith(".xml") else "application/pdf",
        attachment_id="ANGjdJ_abc123",
        tamano_bytes=100,
    )
    email_id = insertar_email(cursor, m, datetime.now(UTC))
    assert email_id is not None
    hash_contenido = gmail_message_id.ljust(64, "0")[:64]
    insertar_documento(cursor, email_id, m, a, hash_contenido, f"2026/08/{nombre_archivo}")

    fila = cursor.execute(
        "SELECT DocumentoRecibidoId FROM fact.DocumentoRecibido WHERE HashContenido = ?",
        hash_contenido,
    ).fetchone()
    return fila[0]


def _datos_extraidos_minimos(*, afectacion_mixta: bool | None) -> DatosExtraidos:
    return DatosExtraidos(
        tipo_comprobante="01",
        numero="F001-123",
        ruc_proveedor="20123456789",
        nombre_proveedor="Proveedor SAC",
        monto=Decimal("118.00"),
        moneda="PEN",
        fecha_emision=date(2026, 8, 15),
        campos_no_extraidos=None,
        afectacion_mixta=afectacion_mixta,
    )


def test_procesamiento_datosextraidos_y_afectacionmixta_reales(worker_db):
    """`usr_worker` real inserta `fact.Procesamiento` + `fact.DatosExtraidos` (con
    `AfectacionMixta`, migracion 014) — 008 ya otorga SELECT/INSERT/UPDATE sobre ambas."""
    instante = datetime.now(UTC)
    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        cursor = conexion.cursor()
        documento_id = _email_y_documento_reales(cursor, "18d2f0a1b2c3aaaa", "factura.xml")
        procesamiento_id = upsert_procesamiento(
            cursor, documento_id, "COMPLETADO", instante, instante
        )
        insertar_datos_extraidos(
            cursor, procesamiento_id, _datos_extraidos_minimos(afectacion_mixta=True)
        )
        conexion.commit()

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        fila = conexion.cursor().execute(
            "SELECT AfectacionMixta FROM fact.DatosExtraidos WHERE ProcesamientoId = ?",
            procesamiento_id,
        ).fetchone()

    assert fila is not None
    assert fila[0] == 1


def test_asociar_documentos_real_escribe_fk_en_ambos_lados(worker_db):
    """`asociar_documentos` escribe DOS `UPDATE` -- design.md Decision 6: el FK vive en AMBOS
    `Procesamiento.DocumentoAsociadoId` en la misma transaccion."""
    instante = datetime.now(UTC)
    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        cursor = conexion.cursor()
        documento_xml = _email_y_documento_reales(cursor, "18d2f0a1b2c3bbbb", "par_a.xml")
        documento_pdf = _email_y_documento_reales(cursor, "18d2f0a1b2c3cccc", "par_a.pdf")
        procesamiento_xml = upsert_procesamiento(
            cursor, documento_xml, "COMPLETADO", instante, instante
        )
        procesamiento_pdf = upsert_procesamiento(
            cursor, documento_pdf, "COMPLETADO", instante, instante
        )
        insertar_datos_extraidos(
            cursor, procesamiento_xml, _datos_extraidos_minimos(afectacion_mixta=False)
        )
        insertar_datos_extraidos(
            cursor, procesamiento_pdf, _datos_extraidos_minimos(afectacion_mixta=None)
        )
        asociar_documentos(
            cursor, procesamiento_xml, documento_pdf, procesamiento_pdf, documento_xml
        )
        conexion.commit()

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        filas = conexion.cursor().execute(
            "SELECT DocumentoRecibidoId, DocumentoAsociadoId FROM fact.Procesamiento "
            "WHERE ProcesamientoId IN (?, ?)",
            procesamiento_xml,
            procesamiento_pdf,
        ).fetchall()

    asociado_por_documento = {fila[0]: fila[1] for fila in filas}
    assert asociado_por_documento[documento_xml] == documento_pdf
    assert asociado_por_documento[documento_pdf] == documento_xml


def test_ck_procesamiento_no_autoasociacion_rechaza_autoasociacion(worker_db):
    """`CK_Procesamiento_NoAutoAsociacion` (014): un `Procesamiento` no puede apuntar su
    `DocumentoAsociadoId` a su propio `DocumentoRecibidoId` — invariante del motor, no de la
    disciplina del worker."""
    instante = datetime.now(UTC)
    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        cursor = conexion.cursor()
        documento_id = _email_y_documento_reales(cursor, "18d2f0a1b2c3dddd", "auto.xml")
        procesamiento_id = upsert_procesamiento(
            cursor, documento_id, "COMPLETADO", instante, instante
        )
        conexion.commit()

    with pytest.raises(pyodbc.Error):
        with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
            conexion.cursor().execute(
                "UPDATE fact.Procesamiento SET DocumentoAsociadoId = ? WHERE ProcesamientoId = ?",
                documento_id,
                procesamiento_id,
            )
            conexion.commit()


def test_usr_worker_no_puede_insertar_en_facturaextraccion(worker_db):
    """Negativa (ADR 0003, spec.md Non-Goals): `fact.FacturaExtraccion` es propiedad de .NET —
    `usr_worker` no tiene ningun GRANT sobre ella, el INSERT falla por DENY, nunca por FK."""
    with pytest.raises(pyodbc.Error):
        with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
            conexion.cursor().execute(
                "INSERT INTO fact.FacturaExtraccion "
                "(FacturaId, CampoNombre, ValorExtraido, Fuente) "
                "VALUES (1, 'ruc', '20123456789', 'XML')"
            )
            conexion.commit()


# --- BACKLOG #7 (WU1): inbox event publishing (inbox_event_repo.py, cli_inbox.py) --------------


def test_reintentar_el_scan_no_duplica_eventos(worker_db):
    """Requirement 'Idempotent publishing' (spec.md, inbox-event-publishing) — un segundo
    `insertar_evento` para el MISMO `ProcesamientoId` es un no-op por el
    `INSERT...SELECT...WHERE NOT EXISTS` atomico (design.md, Decision D3), nunca una segunda
    fila."""
    instante = datetime.now(UTC)
    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        cursor = conexion.cursor()
        documento_id = _email_y_documento_reales(cursor, "18d2f0a1b2c3eeee", "inbox.xml")
        procesamiento_id = upsert_procesamiento(
            cursor, documento_id, "COMPLETADO", instante, instante
        )
        insertar_datos_extraidos(
            cursor, procesamiento_id, _datos_extraidos_minimos(afectacion_mixta=False)
        )
        conexion.commit()

        no_notificados = listar_no_notificados(cursor)
        fila = next(f for f in no_notificados if f.procesamiento_id == procesamiento_id)
        payload = construir_payload(
            estado_procesamiento=fila.estado,
            documento_recibido_id=fila.documento_recibido_id,
            tipo_documento=fila.tipo_documento,
            documento_asociado_id=fila.documento_asociado_id,
            nombre_archivo=fila.nombre_archivo,
            mime_type=fila.mime_type,
            ruta_relativa=fila.ruta_relativa,
            tamano_bytes=fila.tamano_bytes,
            comprobante=None,
        )
        insertar_evento(cursor, procesamiento_id, payload)
        conexion.commit()

        # Segundo intento del MISMO ProcesamientoId — no-op, ninguna fila nueva.
        insertar_evento(cursor, procesamiento_id, payload)
        conexion.commit()

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        fila = conexion.cursor().execute(
            "SELECT COUNT(*) FROM fact.InboxEvent WHERE ProcesamientoId = ?", procesamiento_id
        ).fetchone()

    assert fila[0] == 1


def _datos_extraidos_clave(*, ruc, tipo, numero) -> DatosExtraidos:
    return DatosExtraidos(
        tipo_comprobante=tipo,
        numero=numero,
        ruc_proveedor=ruc,
        nombre_proveedor="Proveedor SAC",
        monto=Decimal("118.00"),
        moneda="PEN",
        fecha_emision=date(2026, 8, 15),
        campos_no_extraidos=None,
        afectacion_mixta=None,
    )


def _datos_extraidos_vacios() -> DatosExtraidos:
    return DatosExtraidos(
        tipo_comprobante=None,
        numero=None,
        ruc_proveedor=None,
        nombre_proveedor=None,
        monto=None,
        moneda=None,
        fecha_emision=None,
        campos_no_extraidos="Clave,Monto,Moneda,FechaEmision",
        afectacion_mixta=None,
    )


def test_segunda_pasada_containment_toca_solo_procesamiento_y_documentorecibido(worker_db):
    """ADR 0017 rev. 3: `listar_huerfanos` expone `dr.NombreArchivo`; el XML huerfano reclama al
    PDF sin clave por containment; `asociar_documentos` escribe el FK en ambos lados. Todas las
    escrituras caen en tablas de `usr_worker` (particion de datos, ADR 0003)."""
    instante = datetime.now(UTC)
    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        cursor = conexion.cursor()
        doc_xml = _email_y_documento_reales(cursor, "18d2f0a1b2c30001", "factura.xml")
        doc_pdf = _email_y_documento_reales(
            cursor, "18d2f0a1b2c30002", "85877-20127765279-fa-f96x-00001230.pdf"
        )
        proc_xml = upsert_procesamiento(cursor, doc_xml, "COMPLETADO", instante, instante)
        proc_pdf = upsert_procesamiento(cursor, doc_pdf, "COMPLETADO", instante, instante)
        insertar_datos_extraidos(
            cursor,
            proc_xml,
            _datos_extraidos_clave(ruc="20127765279", tipo="01", numero="F96X-00001230"),
        )
        insertar_datos_extraidos(cursor, proc_pdf, _datos_extraidos_vacios())
        conexion.commit()

        huerfanos = listar_huerfanos(cursor)
        residuo = [h for h in huerfanos if h.documento_recibido_id in (doc_xml, doc_pdf)]
        pares = asociar_por_nombre_archivo(residuo)
        assert pares == tuple(pares) and len(pares) == 1
        par = pares[0]
        assert par.xml_documento_id == doc_xml
        assert par.pdf_documento_id == doc_pdf
        asociar_documentos(cursor, proc_xml, doc_pdf, proc_pdf, doc_xml)
        conexion.commit()

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        filas = conexion.cursor().execute(
            "SELECT DocumentoRecibidoId, DocumentoAsociadoId FROM fact.Procesamiento "
            "WHERE ProcesamientoId IN (?, ?)",
            proc_xml,
            proc_pdf,
        ).fetchall()

    asociado = {f[0]: f[1] for f in filas}
    assert asociado[doc_xml] == doc_pdf
    assert asociado[doc_pdf] == doc_xml


def test_reemision_pdf_only_candidate_query_y_no_repeticion(worker_db):
    """design.md D5/D6: `listar_asociacion_no_notificada` devuelve solo el lado PDF de una
    asociacion tardia sin evento que la refleje; `insertar_evento_asociacion` inserta una fila y
    una segunda llamada es no-op (NOT EXISTS payload-aware). Solo toca `fact.InboxEvent` (insert) y
    lee `fact.Procesamiento`/`fact.DocumentoRecibido`."""
    instante = datetime.now(UTC)
    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        cursor = conexion.cursor()
        doc_xml = _email_y_documento_reales(cursor, "18d2f0a1b2c30003", "tardio.xml")
        doc_pdf = _email_y_documento_reales(cursor, "18d2f0a1b2c30004", "tardio.pdf")
        proc_xml = upsert_procesamiento(cursor, doc_xml, "COMPLETADO", instante, instante)
        proc_pdf = upsert_procesamiento(cursor, doc_pdf, "COMPLETADO", instante, instante)
        insertar_datos_extraidos(
            cursor, proc_xml, _datos_extraidos_minimos(afectacion_mixta=False)
        )
        insertar_datos_extraidos(cursor, proc_pdf, _datos_extraidos_vacios())
        # Un primer evento del PDF SIN asociacion (como lo emitio #6 antes de la pareja tardia).
        fila = next(
            f for f in listar_no_notificados(cursor) if f.procesamiento_id == proc_pdf
        )
        payload_sin_pareja = construir_payload(
            estado_procesamiento=fila.estado,
            documento_recibido_id=fila.documento_recibido_id,
            tipo_documento=fila.tipo_documento,
            documento_asociado_id=None,
            nombre_archivo=fila.nombre_archivo,
            mime_type=fila.mime_type,
            ruta_relativa=fila.ruta_relativa,
            tamano_bytes=fila.tamano_bytes,
            comprobante=None,
        )
        insertar_evento(cursor, proc_pdf, payload_sin_pareja)
        # Asociacion tardia.
        asociar_documentos(cursor, proc_xml, doc_pdf, proc_pdf, doc_xml)
        conexion.commit()

        candidatos = listar_asociacion_no_notificada(cursor)
        ids = {c.procesamiento_id for c in candidatos}
        assert proc_pdf in ids
        assert proc_xml not in ids  # D5: lado XML nunca se re-emite
        candidato = next(c for c in candidatos if c.procesamiento_id == proc_pdf)
        payload_asociado = construir_payload(
            estado_procesamiento=candidato.estado,
            documento_recibido_id=candidato.documento_recibido_id,
            tipo_documento=candidato.tipo_documento,
            documento_asociado_id=candidato.documento_asociado_id,
            nombre_archivo=candidato.nombre_archivo,
            mime_type=candidato.mime_type,
            ruta_relativa=candidato.ruta_relativa,
            tamano_bytes=candidato.tamano_bytes,
            comprobante=None,
        )
        insertar_evento_asociacion(cursor, proc_pdf, payload_asociado)
        conexion.commit()
        # Segunda llamada: no-op.
        insertar_evento_asociacion(cursor, proc_pdf, payload_asociado)
        conexion.commit()
        assert listar_asociacion_no_notificada(cursor) == () or proc_pdf not in {
            c.procesamiento_id for c in listar_asociacion_no_notificada(cursor)
        }

    with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
        total = conexion.cursor().execute(
            "SELECT COUNT(*) FROM fact.InboxEvent WHERE ProcesamientoId = ?", proc_pdf
        ).fetchone()

    assert total[0] == 2  # el evento sin-pareja original + exactamente una re-emision


def test_usr_worker_no_puede_escribir_en_factura(worker_db):
    """Negativa (ADR 0003 particion de datos): `fact.Factura` es propiedad de .NET —
    `usr_worker` no tiene GRANT sobre ella, el INSERT falla por DENY, nunca por FK."""
    with pytest.raises(pyodbc.Error):
        with pyodbc.connect(worker_db["worker_connection_string"]) as conexion:
            conexion.cursor().execute(
                "INSERT INTO fact.Factura "
                "(TipoComprobante, TotalOrig, Moneda, FechaEmision) "
                "VALUES ('01', 100.00, 'PEN', '2026-08-15')"
            )
            conexion.commit()
