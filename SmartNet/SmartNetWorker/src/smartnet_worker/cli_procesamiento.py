"""Punto de entrada del worker de extraccion y asociacion (BACKLOG #6) — el UNICO orquestador de
este stage, mismo patron que `cli_gmail.py`/`cli_tipo_cambio.py`.

Un solo ciclo por invocacion (sin scheduler, sin polling en proceso): preflight de Tesseract
(design.md, Decision 7 -- una falla aqui aborta el run ENTERO, 0 filas escritas, nunca un
`PERMANENTE` por documento) -> lee `fact.DocumentoRecibido` pendiente (`documento_repo.
listar_pendientes`) -> **XML primero, todo el lote, despues cada PDF** (ADR 0017, literal) -> por
documento, UNA transaccion propia, aislada del resto del run (design.md, Decision 7's mismo
framing que #5's Decision 7) -> tras procesar todo el lote, una pasada de asociacion sobre
`fact.Procesamiento` sin pareja (`procesamiento_repo.listar_huerfanos`, Decision 6) -> `COMMIT` por
lado -> `fact.EstadoIntegracion` (`Nombre='WORKER'`), en su propia transaccion, fuera del negocio
(ADR 0003).

`lector`/`conectar`/`verificar_tesseract` son los puntos de sustitucion explicitos (design.md,
tabla de Seams): un `LectorPdfLocal(MotorTesseract(), ...)`, `pyodbc.connect` y
`pdf_lectura.verificar_tesseract` por defecto, sustituidos en pruebas por un `LectorPdf` falso, una
fabrica de conexion falsa y una funcion de preflight falsa, sin tocar ninguna otra linea de este
modulo.
"""

from __future__ import annotations

import sys
from collections.abc import Callable
from datetime import UTC, datetime
from pathlib import Path

import pyodbc

from smartnet_worker import afectacion, config, errores, pdf_texto, ubl
from smartnet_worker.comprobante import asociar, asociar_por_nombre_archivo
from smartnet_worker.config import ConfiguracionError
from smartnet_worker.documento_repo import (
    DocumentoPendiente,
    fijar_estado_documento,
    fijar_tipo_documento,
    listar_pendientes,
    refrescar_estado_email,
)
from smartnet_worker.estado_integracion import registrar_exito, registrar_fallo
from smartnet_worker.pdf_lectura import (
    LectorPdf,
    LectorPdfLocal,
    MotorTesseract,
    TesseractNotFoundError,
)
from smartnet_worker.pdf_lectura import verificar_tesseract as _verificar_tesseract_real
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

_SELECT_RUC_PROPIO = """
SELECT Valor FROM fact.Configuracion WHERE Seccion = 'EMPRESA' AND Clave = 'RUC'
"""

_MAX_MENSAJE_ERROR_LEN = 2000


def ejecutar(
    *,
    lector: LectorPdf | None = None,
    conectar: Callable[[str], object] = pyodbc.connect,
    verificar_tesseract: Callable[[], None] = _verificar_tesseract_real,
    instante: datetime | None = None,
) -> int:
    """Corre un ciclo completo de extraccion y asociacion. Devuelve 0 en exito, 1 en fallo —
    pensado para `sys.exit`, mismo patron single-run que `cli_gmail.ejecutar`/
    `cli_tipo_cambio.ejecutar`."""
    instante = instante or datetime.now(UTC)
    connection_string = config.obtener_connection_string()

    try:
        verificar_tesseract()
    except TesseractNotFoundError as error:
        return _registrar_fallo_run(conectar, connection_string, instante, str(error))

    try:
        raiz_almacenamiento = Path(config.obtener_raiz_almacenamiento())
    except ConfiguracionError as error:
        return _registrar_fallo_run(conectar, connection_string, instante, str(error))

    lector_pdf = (
        lector if lector is not None else LectorPdfLocal(MotorTesseract(), config.OCR_IDIOMA)
    )

    conexion_lectura = conectar(connection_string)
    try:
        cursor_lectura = conexion_lectura.cursor()
        ruc_propio = _leer_ruc_propio(cursor_lectura)
        pendientes = listar_pendientes(cursor_lectura, instante)
    finally:
        conexion_lectura.close()

    # ADR 0017 literal: TODOS los XML del lote antes que cualquier PDF.
    xml_docs = [d for d in pendientes if d.extension.lower() == "xml"]
    pdf_docs = [d for d in pendientes if d.extension.lower() == "pdf"]

    errores_run: list[str] = []
    for doc in xml_docs:
        try:
            _procesar_documento(
                doc, "XML", lector_pdf, ruc_propio, raiz_almacenamiento,
                conectar, connection_string, instante,
            )
        except Exception as error:  # noqa: BLE001 — aislamiento por documento (design.md, Decision 7).
            errores_run.append(f"{doc.documento_recibido_id}: {error}")
    for doc in pdf_docs:
        try:
            _procesar_documento(
                doc, "PDF", lector_pdf, ruc_propio, raiz_almacenamiento,
                conectar, connection_string, instante,
            )
        except Exception as error:  # noqa: BLE001 — aislamiento por documento.
            errores_run.append(f"{doc.documento_recibido_id}: {error}")

    try:
        _asociar_pendientes(conectar, connection_string)
    except Exception as error:  # noqa: BLE001 — la asociacion nunca aborta el run entero.
        errores_run.append(f"asociacion: {error}")

    conexion_estado = conectar(connection_string)
    try:
        cursor_estado = conexion_estado.cursor()
        if errores_run:
            resumen = (
                f"{len(errores_run)} de {len(pendientes)} documento(s) fallaron: "
                + "; ".join(errores_run)
            )
            registrar_fallo(cursor_estado, "WORKER", instante, resumen)
            conexion_estado.commit()
            return 1
        registrar_exito(cursor_estado, "WORKER", instante)
        conexion_estado.commit()
        return 0
    finally:
        conexion_estado.close()


def _procesar_documento(
    doc: DocumentoPendiente,
    tipo_documento: str,
    lector: LectorPdf,
    ruc_propio: str | None,
    raiz_almacenamiento: Path,
    conectar: Callable[[str], object],
    connection_string: str,
    instante: datetime,
) -> None:
    """Extrae (XML puro / PDF via `LectorPdf` + `pdf_texto.extraer`, puro) y persiste en UNA
    transaccion propia (design.md, Decision 7). Cualquier fallo -- de extraccion o de escritura --
    se registra en su propia transaccion tras rollback (mismo patron que `cli_tipo_cambio.py`,
    Decision 6) y se relanza para que el llamador acumule el aislamiento por documento."""
    try:
        ruta = raiz_almacenamiento / doc.ruta_relativa
        if tipo_documento == "XML":
            comprobante_ubl = ubl.parsear(ruta.read_bytes())
            datos = _datos_desde_xml(comprobante_ubl)
        else:
            texto = "\n".join(lector.leer_paginas(ruta))
            datos = _datos_desde_pdf(pdf_texto.extraer(texto, doc.nombre_archivo, ruc_propio))

        conexion = conectar(connection_string)
        try:
            cursor = conexion.cursor()
            procesamiento_id = upsert_procesamiento(
                cursor, doc.documento_recibido_id, "COMPLETADO", instante, instante
            )
            insertar_datos_extraidos(cursor, procesamiento_id, datos)
            fijar_tipo_documento(cursor, doc.documento_recibido_id, tipo_documento)
            fijar_estado_documento(cursor, doc.documento_recibido_id, "PROCESADO")
            numero_intento = contar_intentos(cursor, procesamiento_id) + 1
            insertar_intento(
                cursor, procesamiento_id, numero_intento, "EXITO", instante, None, None
            )
            refrescar_estado_email(cursor, doc.email_id)
            conexion.commit()
        except Exception:
            conexion.rollback()
            raise
        finally:
            conexion.close()
    except Exception as error:  # noqa: BLE001 — punto de entrada de este documento: todo fallo se loguea.
        _registrar_fallo_documento(conectar, connection_string, doc, instante, error)
        raise


def _registrar_fallo_documento(
    conectar: Callable[[str], object],
    connection_string: str,
    doc: DocumentoPendiente,
    instante: datetime,
    error: Exception,
) -> None:
    """`errores.clasificar` decide `PERMANENTE`/`TRANSITORIO` (ADR 0010); `PERMANENTE` nunca
    agenda un proximo reintento (`errores.proximo_reintento` -> `None`)."""
    conexion = conectar(connection_string)
    try:
        cursor = conexion.cursor()
        procesamiento_id = upsert_procesamiento(
            cursor, doc.documento_recibido_id, "ERROR", instante, instante
        )
        clasificacion = errores.clasificar(error)
        mensaje = str(error)[:_MAX_MENSAJE_ERROR_LEN]
        insertar_error(cursor, procesamiento_id, mensaje, clasificacion.value, instante)
        numero_intento = contar_intentos(cursor, procesamiento_id) + 1
        proximo_reintento = errores.proximo_reintento(clasificacion, instante, numero_intento)
        insertar_intento(
            cursor, procesamiento_id, numero_intento, "FALLO", instante, mensaje, proximo_reintento
        )
        fijar_estado_documento(cursor, doc.documento_recibido_id, "ERROR")
        refrescar_estado_email(cursor, doc.email_id)
        conexion.commit()
    finally:
        conexion.close()


def _asociar_pendientes(conectar: Callable[[str], object], connection_string: str) -> None:
    """Pasada de asociacion (design.md, Decision 6): el conjunto candidato es TODO
    `fact.Procesamiento` sin pareja tras el lote (incluye lo recien commiteado por
    `_procesar_documento` Y lo huerfano de runs anteriores -- `listar_huerfanos` no distingue, asi
    que basta un solo parametro). Ambiguedad (>1 candidato del mismo lado con la misma clave) =>
    ninguna asociacion, decidido por `comprobante.asociar` (ADR 0017), nunca por este modulo."""
    conexion = conectar(connection_string)
    try:
        cursor = conexion.cursor()
        huerfanos = listar_huerfanos(cursor)
        pares_exactos = asociar((), huerfanos)
        # Segunda pasada acotada (ADR 0017 rev. 3): corre sobre el RESIDUO -- los huerfanos que la
        # pasada exacta de 4 componentes no emparejo -- verificando la clave del XML contra el
        # nombre de archivo del PDF. La pasada exacta queda byte-intacta.
        emparejados = {
            id_documento
            for par in pares_exactos
            for id_documento in (par.xml_documento_id, par.pdf_documento_id)
        }
        residuo = [d for d in huerfanos if d.documento_recibido_id not in emparejados]
        pares = (*pares_exactos, *asociar_por_nombre_archivo(residuo))
        for par in pares:
            procesamiento_xml = obtener_procesamiento_id(cursor, par.xml_documento_id)
            procesamiento_pdf = obtener_procesamiento_id(cursor, par.pdf_documento_id)
            asociar_documentos(
                cursor,
                procesamiento_xml,
                par.pdf_documento_id,
                procesamiento_pdf,
                par.xml_documento_id,
            )
        conexion.commit()
    except Exception:
        conexion.rollback()
        raise
    finally:
        conexion.close()


def _datos_desde_xml(c: ubl.ComprobanteUbl) -> DatosExtraidos:
    return DatosExtraidos(
        tipo_comprobante=c.clave.tipo,
        numero=f"{c.clave.serie}-{c.clave.numero}",
        ruc_proveedor=c.clave.ruc_emisor,
        nombre_proveedor=c.nombre_proveedor,
        monto=c.monto,
        moneda=c.moneda,
        fecha_emision=c.fecha_emision,
        campos_no_extraidos=",".join(c.campos_no_extraidos) or None,
        afectacion_mixta=afectacion.calcular_afectacion_mixta(c.codigos_afectacion),
    )


def _datos_desde_pdf(e: pdf_texto.ExtraccionPdf) -> DatosExtraidos:
    clave = e.clave
    return DatosExtraidos(
        tipo_comprobante=clave.tipo if clave else None,
        numero=f"{clave.serie}-{clave.numero}" if clave else None,
        ruc_proveedor=clave.ruc_emisor if clave else None,
        nombre_proveedor=None,
        monto=e.monto,
        moneda=e.moneda,
        fecha_emision=e.fecha_emision,
        campos_no_extraidos=",".join(e.campos_no_extraidos) or None,
        # PDF sin XML: sin comprobante que verificar, NUNCA True/False (design.md, spec.md
        # 'calculo-afectacion-mixta').
        afectacion_mixta=None,
    )


def _leer_ruc_propio(cursor) -> str | None:
    """`fact.Configuracion` clave `EMPRESA.RUC` (migracion 014) -- NULL-seeded es legitimo
    (Open Question 1, resuelta): sin ella, `pdf_texto.extraer` cae directo al respaldo de nombre de
    archivo SUNAT."""
    cursor.execute(_SELECT_RUC_PROPIO)
    fila = cursor.fetchone()
    return fila[0] if fila and fila[0] else None


def _registrar_fallo_run(
    conectar: Callable[[str], object], connection_string: str, instante: datetime, error: str
) -> int:
    conexion = conectar(connection_string)
    try:
        cursor = conexion.cursor()
        registrar_fallo(cursor, "WORKER", instante, error)
        conexion.commit()
    finally:
        conexion.close()
    return 1


def main() -> None:
    sys.exit(ejecutar())


if __name__ == "__main__":
    main()
