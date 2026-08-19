"""Repositorio de `fact.InboxEvent` para el runtime Python — recibe un `cursor`, mismo patron que
`procesamiento_repo.py`/`documento_repo.py` (design.md, Interfaces/Contracts). `fact_worker` tiene
SELECT/INSERT sobre esta tabla (008_usuarios_y_permisos.sql) — NUNCA UPDATE: ese privilegio es de
`fact_api` (.NET), quien marca `PROMOVIDO`/`DESCARTADO` (ADR 0003, particion de datos).

`listar_no_notificados` sirve el conjunto candidato: todo `fact.Procesamiento` sin
`fact.InboxEvent` correspondiente, con un `LEFT JOIN fact.DatosExtraidos` porque un `Procesamiento`
en `Estado='ERROR'` nunca tiene fila #6 (spec.md 'Failed processing still emits an event').

`insertar_evento` es la puerta de idempotencia (design.md, Decision D3): un unico
`INSERT...SELECT...WHERE NOT EXISTS` atomico — nunca un `SELECT` previo separado (misma disciplina
anti-TOCTOU que `procesamiento_repo.upsert_procesamiento`); una fila duplicada por una carrera entre
dos runs es cosmetica porque D2 (item #7, WU3, lado .NET) sigue topando facturas a una sola. `Tipo`
es siempre el literal unico de `CK_InboxEvent_Tipo` — el outcome se deriva de
`Procesamiento.Estado`, nunca de un segundo literal de `Tipo`."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import date
from decimal import Decimal

_TIPO_EVENTO = "PROCESAMIENTO_FINALIZADO"

_LISTAR_NO_NOTIFICADOS = """
SELECT p.ProcesamientoId, p.Estado, p.DocumentoRecibidoId, dr.TipoDocumento, p.DocumentoAsociadoId,
       de.TipoComprobante, de.Numero, de.RucProveedor, de.NombreProveedor, de.Monto, de.Moneda,
       de.FechaEmision, de.CamposNoExtraidos, de.AfectacionMixta
FROM fact.Procesamiento p
JOIN fact.DocumentoRecibido dr ON dr.DocumentoRecibidoId = p.DocumentoRecibidoId
LEFT JOIN fact.DatosExtraidos de ON de.ProcesamientoId = p.ProcesamientoId
WHERE NOT EXISTS (SELECT 1 FROM fact.InboxEvent ie WHERE ie.ProcesamientoId = p.ProcesamientoId)
"""

_INSERTAR_EVENTO = """
INSERT INTO fact.InboxEvent (Tipo, ProcesamientoId, Payload)
SELECT ?, ?, ?
WHERE NOT EXISTS (SELECT 1 FROM fact.InboxEvent WHERE ProcesamientoId = ?)
"""


@dataclass(frozen=True)
class ProcesamientoNoNotificado:
    """Una fila `fact.Procesamiento` (+`DocumentoRecibido`, +`DatosExtraidos` si existe) sin
    `fact.InboxEvent` correspondiente — lista para que `payload_inbox.construir_payload` la
    convierta en el JSON del evento (`cli_inbox.py`, WU1)."""

    procesamiento_id: int
    estado: str
    documento_recibido_id: int
    tipo_documento: str
    documento_asociado_id: int | None
    tipo_comprobante: str | None
    numero: str | None
    ruc_proveedor: str | None
    nombre_proveedor: str | None
    monto: Decimal | None
    moneda: str | None
    fecha_emision: date | None
    campos_no_extraidos: str | None
    afectacion_mixta: bool | None


def listar_no_notificados(cursor) -> tuple[ProcesamientoNoNotificado, ...]:
    cursor.execute(_LISTAR_NO_NOTIFICADOS)
    return tuple(
        ProcesamientoNoNotificado(
            procesamiento_id=fila[0],
            estado=fila[1],
            documento_recibido_id=fila[2],
            tipo_documento=fila[3],
            documento_asociado_id=fila[4],
            tipo_comprobante=fila[5],
            numero=fila[6],
            ruc_proveedor=fila[7],
            nombre_proveedor=fila[8],
            monto=fila[9],
            moneda=fila[10],
            fecha_emision=fila[11],
            campos_no_extraidos=fila[12],
            afectacion_mixta=fila[13],
        )
        for fila in cursor.fetchall()
    )


def insertar_evento(cursor, procesamiento_id: int, payload: str) -> None:
    cursor.execute(_INSERTAR_EVENTO, _TIPO_EVENTO, procesamiento_id, payload, procesamiento_id)
