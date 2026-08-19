"""Repositorio de `fact.Procesamiento` / `fact.DatosExtraidos` / `fact.ProcesamientoError` /
`fact.ProcesamientoIntentos` para el runtime Python — recibe un `cursor`, mismo patron que
`documento_repo.py`/`tipo_cambio_repo.py` (design.md, Interfaces/Contracts).

`upsert_procesamiento` es la puerta de idempotencia: `UQ_Procesamiento_DocumentoRecibido` (014) es
la que rechaza el segundo INSERT del mismo `DocumentoRecibidoId`, este adaptador solo traduce el
`IntegrityError` en un UPDATE del mismo `Procesamiento` — nunca un `SELECT` previo (misma disciplina
anti-TOCTOU que `insertar_email`/`insertar_documento`, decision explicita del usuario, design.md
Open Question 4). Decision 9: un unico `Procesamiento` por documento, escrito de nuevo en cada
estado terminal, nunca un segundo registro.

`asociar_documentos` escribe DOS `UPDATE`, uno por cada lado de la pareja (design.md, Decision 6):
el FK vive en AMBOS `Procesamiento.DocumentoAsociadoId` dentro de la MISMA transaccion que abrio el
llamador, para que "tengo mi pareja?" sea una lectura desde cualquiera de los dos lados, sin
convencion de direccion.

`listar_huerfanos` sirve el conjunto candidato de la asociacion (design.md, Decision 6): todo
`Procesamiento` cuyo `DocumentoAsociadoId IS NULL`, filtrado por `IX_Procesamiento_SinAsociar`
(014)."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import date, datetime
from decimal import Decimal

import pyodbc

from smartnet_worker.comprobante import Documento, construir_clave

_INTEGRACION_NOMBRE = "WORKER"

_UPSERT_INSERT = """
INSERT INTO fact.Procesamiento (DocumentoRecibidoId, Estado, IniciadoEn, FinalizadoEn)
OUTPUT INSERTED.ProcesamientoId
VALUES (?, ?, ?, ?)
"""

_UPSERT_UPDATE = """
UPDATE fact.Procesamiento
SET Estado = ?, IniciadoEn = ?, FinalizadoEn = ?
OUTPUT INSERTED.ProcesamientoId
WHERE DocumentoRecibidoId = ?
"""

_INSERT_DATOS_EXTRAIDOS = """
INSERT INTO fact.DatosExtraidos
    (ProcesamientoId, TipoComprobante, Numero, RucProveedor, NombreProveedor, Monto, Moneda,
     FechaEmision, CamposNoExtraidos, AfectacionMixta)
VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
"""

_UPDATE_ASOCIAR = """
UPDATE fact.Procesamiento SET DocumentoAsociadoId = ? WHERE ProcesamientoId = ?
"""

_INSERT_ERROR = """
INSERT INTO fact.ProcesamientoError
    (ProcesamientoId, Integracion, Mensaje, Clasificacion, OcurridoEn)
VALUES (?, ?, ?, ?, ?)
"""

_INSERT_INTENTO = """
INSERT INTO fact.ProcesamientoIntentos
    (ProcesamientoId, NumeroIntento, Resultado, OcurridoEn, Detalle, ProximoReintentoEn)
VALUES (?, ?, ?, ?, ?, ?)
"""

# design.md, Decision 6: el conjunto candidato es todo Procesamiento sin pareja, servido por
# IX_Procesamiento_SinAsociar (014). RucProveedor/TipoComprobante/Numero vienen de DatosExtraidos —
# construir_clave() rehace la ClaveComprobante normalizada desde los tres, igual que ubl.py/
# pdf_texto.py hicieron al escribirlos.
_LISTAR_HUERFANOS = """
SELECT p.DocumentoRecibidoId, dr.TipoDocumento, de.RucProveedor, de.TipoComprobante, de.Numero
FROM fact.Procesamiento p
JOIN fact.DocumentoRecibido dr ON dr.DocumentoRecibidoId = p.DocumentoRecibidoId
JOIN fact.DatosExtraidos de ON de.ProcesamientoId = p.ProcesamientoId
WHERE p.DocumentoAsociadoId IS NULL
"""


@dataclass(frozen=True)
class DatosExtraidos:
    """Fila `fact.DatosExtraidos` ya lista para persistir — construida por el llamador
    (`cli_procesamiento.py`, WU4) a partir de `ubl.ComprobanteUbl` o `pdf_texto.ExtraccionPdf` mas
    `afectacion.calcular_afectacion_mixta`. Este modulo no decide nada, solo escribe (ADR 0019)."""

    tipo_comprobante: str | None
    numero: str | None
    ruc_proveedor: str | None
    nombre_proveedor: str | None
    monto: Decimal | None
    moneda: str | None
    fecha_emision: date | None
    campos_no_extraidos: str | None
    afectacion_mixta: bool | None


def upsert_procesamiento(
    cursor, documento_id: int, estado: str, iniciado: datetime, finalizado: datetime
) -> int:
    """INSERT en el primer intento; en un reintento, `UQ_Procesamiento_DocumentoRecibido` (014)
    rechaza el INSERT duplicado con `IntegrityError` y este adaptador reintenta como UPDATE —
    Decision 9's regla de un unico `Procesamiento` por documento, escrito de nuevo en su estado
    terminal. Devuelve el `ProcesamientoId`, leido con `OUTPUT INSERTED` en el mismo `execute`."""
    try:
        cursor.execute(_UPSERT_INSERT, documento_id, estado, iniciado, finalizado)
    except pyodbc.IntegrityError:
        cursor.execute(_UPSERT_UPDATE, estado, iniciado, finalizado, documento_id)
    return int(cursor.fetchone()[0])


def insertar_datos_extraidos(cursor, procesamiento_id: int, d: DatosExtraidos) -> None:
    cursor.execute(
        _INSERT_DATOS_EXTRAIDOS,
        procesamiento_id,
        d.tipo_comprobante,
        d.numero,
        d.ruc_proveedor,
        d.nombre_proveedor,
        d.monto,
        d.moneda,
        d.fecha_emision,
        d.campos_no_extraidos,
        d.afectacion_mixta,
    )


def asociar_documentos(
    cursor, procesamiento_a: int, documento_b: int, procesamiento_b: int, documento_a: int
) -> None:
    """Dos `UPDATE`, uno por lado (design.md, Decision 6) — el FK se escribe en AMBOS
    `Procesamiento.DocumentoAsociadoId` dentro de la MISMA transaccion que abrio el llamador."""
    cursor.execute(_UPDATE_ASOCIAR, documento_b, procesamiento_a)
    cursor.execute(_UPDATE_ASOCIAR, documento_a, procesamiento_b)


def insertar_error(
    cursor, procesamiento_id: int, mensaje: str, clasificacion: str, ocurrido: datetime
) -> None:
    cursor.execute(
        _INSERT_ERROR, procesamiento_id, _INTEGRACION_NOMBRE, mensaje, clasificacion, ocurrido
    )


def insertar_intento(
    cursor,
    procesamiento_id: int,
    numero_intento: int,
    resultado: str,
    ocurrido: datetime,
    detalle: str | None,
    proximo_reintento: datetime | None,
) -> None:
    """`proximo_reintento=None` es el literal `ProximoReintentoEn IS NULL` de `errores.py` para un
    `PERMANENTE` — nunca reintentado (design.md, Decision 8)."""
    cursor.execute(
        _INSERT_INTENTO,
        procesamiento_id,
        numero_intento,
        resultado,
        ocurrido,
        detalle,
        proximo_reintento,
    )


def listar_huerfanos(cursor) -> tuple[Documento, ...]:
    cursor.execute(_LISTAR_HUERFANOS)
    huerfanos: list[Documento] = []
    for documento_recibido_id, tipo_documento, ruc, tipo, numero in cursor.fetchall():
        clave = None
        if ruc and tipo and numero:
            clave = construir_clave(ruc, tipo, numero)
        huerfanos.append(
            Documento(
                documento_recibido_id=documento_recibido_id,
                tipo_documento=tipo_documento,
                clave=clave,
            )
        )
    return tuple(huerfanos)
