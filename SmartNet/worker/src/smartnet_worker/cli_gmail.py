"""Punto de entrada del worker de ingesta Gmail (BACKLOG #5) — el UNICO orquestador del paquete.

Un solo ciclo por invocacion (sin scheduler, sin polling en proceso): config -> `ClienteGmail` ->
lee las cuatro claves `INGESTA.*` de `fact.Configuracion` -> resuelve las dos etiquetas a ids ->
busca mensajes -> por mensaje candidato, UNA transaccion propia, aislada del resto del run
(design.md, Decision 7) -> `COMMIT` -> `aplicar_etiqueta` (NUNCA antes del commit) ->
`fact.EstadoIntegracion` (`Nombre='GMAIL'`), en su propia transaccion, fuera del negocio (ADR 0003).

`cliente`/`conectar` son puntos de sustitucion explicitos (design.md, tabla de Seams): un
`ClienteGmail` real y `pyodbc.connect` por defecto, sustituidos en pruebas por un
`ClienteGmailFalso` y una fabrica de conexion falsa, sin tocar ninguna otra linea de este modulo.
"""

from __future__ import annotations

import sys
from collections.abc import Callable
from datetime import UTC, date, datetime
from pathlib import Path

import pyodbc

from smartnet_worker import config
from smartnet_worker.almacenamiento import escribir
from smartnet_worker.config import ConfiguracionError
from smartnet_worker.documento_repo import insertar_documento, insertar_email
from smartnet_worker.estado_integracion import registrar_exito, registrar_fallo
from smartnet_worker.gmail import (
    calcular_hash,
    construir_consulta,
    es_candidato,
    extensiones_permitidas,
    parsear_mensaje,
    ruta_relativa,
)
from smartnet_worker.gmail_client import ClienteGmail

_CLAVES_INGESTA = (
    "ETIQUETA_ORIGEN",
    "ETIQUETA_PROCESADO",
    "FECHA_INICIO",
    "EXTENSIONES_PERMITIDAS",
)

_SELECT_CONFIGURACION_INGESTA = """
SELECT Clave, Valor FROM fact.Configuracion
WHERE Seccion = 'INGESTA' AND Clave IN (?, ?, ?, ?)
"""


def ejecutar(
    *,
    cliente: ClienteGmail | None = None,
    conectar: Callable[[str], object] = pyodbc.connect,
    instante: datetime | None = None,
) -> int:
    """Corre un ciclo completo de ingesta. Devuelve 0 en exito, 1 en fallo — pensado para
    `sys.exit`, mismo patron single-run que `cli_tipo_cambio.ejecutar`."""
    instante = instante or datetime.now(UTC)
    connection_string = config.obtener_connection_string()

    try:
        raiz_almacenamiento = Path(config.obtener_raiz_almacenamiento())
        credenciales_json = (
            None if cliente is not None else config.obtener_credenciales_gmail_json()
        )
    except ConfiguracionError as error:
        return _registrar_fallo_run(conectar, connection_string, instante, str(error))

    conexion_configuracion = conectar(connection_string)
    try:
        cursor_configuracion = conexion_configuracion.cursor()
        origen, procesado, fecha_inicio, extensiones_texto = _leer_configuracion_ingesta(
            cursor_configuracion
        )
    except ConfiguracionError as error:
        conexion_configuracion.close()
        return _registrar_fallo_run(conectar, connection_string, instante, str(error))
    conexion_configuracion.close()

    permitidas = extensiones_permitidas(extensiones_texto)
    consulta = construir_consulta(origen, procesado, fecha_inicio)

    try:
        cliente_gmail = (
            cliente if cliente is not None else ClienteGmail(credenciales_json, config.GMAIL_SCOPES)
        )
        etiquetas = cliente_gmail.resolver_etiquetas()
        if origen not in etiquetas:
            raise ConfiguracionError(
                f"La etiqueta ETIQUETA_ORIGEN='{origen}' no existe en el buzon de Gmail."
            )
        if procesado not in etiquetas:
            raise ConfiguracionError(
                f"La etiqueta ETIQUETA_PROCESADO='{procesado}' no existe en el buzon de Gmail."
            )
        etiqueta_procesado_id = etiquetas[procesado]
        ids_mensajes = cliente_gmail.buscar_mensajes(consulta)
    except Exception as error:  # noqa: BLE001 — cualquier fallo aqui aborta el run entero.
        return _registrar_fallo_run(conectar, connection_string, instante, str(error))

    errores: list[str] = []
    for mensaje_id in ids_mensajes:
        try:
            _procesar_mensaje(
                cliente_gmail,
                conectar,
                connection_string,
                raiz_almacenamiento,
                permitidas,
                mensaje_id,
                etiqueta_procesado_id,
                instante,
            )
        except Exception as error:  # noqa: BLE001 — aislamiento por mensaje (design.md, Decision 7).
            errores.append(f"{mensaje_id}: {error}")

    conexion_estado = conectar(connection_string)
    try:
        cursor_estado = conexion_estado.cursor()
        if errores:
            resumen = (
                f"{len(errores)} de {len(ids_mensajes)} mensaje(s) fallaron: " + "; ".join(errores)
            )
            registrar_fallo(cursor_estado, "GMAIL", instante, resumen)
            conexion_estado.commit()
            return 1
        registrar_exito(cursor_estado, "GMAIL", instante)
        conexion_estado.commit()
        return 0
    finally:
        conexion_estado.close()


def _procesar_mensaje(
    cliente: ClienteGmail,
    conectar: Callable[[str], object],
    connection_string: str,
    raiz_almacenamiento: Path,
    permitidas: frozenset[str],
    mensaje_id: str,
    etiqueta_procesado_id: str,
    instante: datetime,
) -> None:
    """Procesa un mensaje candidato en su propia transaccion. Un mensaje sin ningun adjunto
    candidato no abre conexion ni escribe nada (spec.md: 'sin candidatos -> sin fila, sin
    etiqueta'). La etiqueta se aplica SOLO tras un `COMMIT` exitoso — si `insertar_email` devuelve
    `None` (mensaje ya ingestado, design.md Decision 4), se salta la descarga por completo y la
    etiqueta se reaplica igual (auto-sanacion de un commit previo cuyo etiquetado fallo)."""
    payload = cliente.obtener_mensaje(mensaje_id)
    mensaje = parsear_mensaje(payload)
    candidatos = [
        adjunto for adjunto in mensaje.adjuntos if es_candidato(adjunto.nombre, permitidas)
    ]
    if not candidatos:
        return

    conexion = conectar(connection_string)
    try:
        cursor = conexion.cursor()
        email_id = insertar_email(cursor, mensaje, instante)
        if email_id is not None:
            for adjunto in candidatos:
                datos = cliente.obtener_adjunto(mensaje_id, adjunto.attachment_id)
                hash_hex = calcular_hash(datos)
                ruta = ruta_relativa(mensaje, adjunto, hash_hex)
                escribir(raiz_almacenamiento, ruta, datos)
                insertar_documento(cursor, email_id, mensaje, adjunto, hash_hex, ruta)
        conexion.commit()
    except Exception:
        conexion.rollback()
        raise
    finally:
        conexion.close()

    cliente.aplicar_etiqueta(mensaje_id, etiqueta_procesado_id)


def _leer_configuracion_ingesta(cursor) -> tuple[str, str, date, str]:
    """Lee las cuatro claves `INGESTA.*` de `fact.Configuracion` (solo lectura, `usr_worker`,
    008_usuarios_y_permisos.sql). Lanza `ConfiguracionError` si alguna tiene `Valor IS NULL` o no
    existe — antes de cualquier llamada a Gmail (spec.md, 'sondeo-acotado-gmail')."""
    cursor.execute(_SELECT_CONFIGURACION_INGESTA, *_CLAVES_INGESTA)
    valores = dict(cursor.fetchall())
    faltantes = [clave for clave in _CLAVES_INGESTA if not valores.get(clave)]
    if faltantes:
        raise ConfiguracionError(
            "fact.Configuracion (Seccion='INGESTA') tiene valor NULL o ausente para: "
            + ", ".join(faltantes)
        )
    fecha_inicio = date.fromisoformat(valores["FECHA_INICIO"])
    return (
        valores["ETIQUETA_ORIGEN"],
        valores["ETIQUETA_PROCESADO"],
        fecha_inicio,
        valores["EXTENSIONES_PERMITIDAS"],
    )


def _registrar_fallo_run(
    conectar: Callable[[str], object], connection_string: str, instante: datetime, error: str
) -> int:
    conexion = conectar(connection_string)
    try:
        cursor = conexion.cursor()
        registrar_fallo(cursor, "GMAIL", instante, error)
        conexion.commit()
    finally:
        conexion.close()
    return 1


def main() -> None:
    sys.exit(ejecutar())


if __name__ == "__main__":
    main()
