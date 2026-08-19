"""Constructor puro del `Payload` de `fact.InboxEvent` (BACKLOG #7, WU1) — ni red, ni disco, ni DB,
ni reloj (ADR 0019). Une lo que `cli_inbox.py` ya leyo de `fact.Procesamiento`/
`fact.DocumentoRecibido`/`fact.DatosExtraidos` (ya comiteado por #6) en el JSON versionado que
design.md describe (Interfaces/Contracts).

Design D4 (confirmada, ver design.md Open Questions): `fuente` es el UNICO dato de evidencia por
campo — el `TipoDocumento` del documento, uniforme para todo el evento — NUNCA `confianza`: ningun
componente de #6 calcula un valor de confianza, y emitirlo inventaria un dato (ADR 0017 boundary).
Esto estrecha la propuesta Q2 deliberadamente (CLAUDE.md regla 1), no en silencio.
"""

from __future__ import annotations

import json
from dataclasses import dataclass
from datetime import date
from decimal import Decimal

_VERSION = 1

# Mismo orden que CK_FacturaExtraccion_CampoNombre (005_negocio.sql) — 'igv' nunca aparece:
# fact.DatosExtraidos (BACKLOG #6) no tiene columna Igv, asi que no hay evidencia posible para ese
# campo desde este lado.
_CAMPOS_EVIDENCIA = (
    ("tipo_comprobante", "tipoComprobante"),
    ("numero", "numero"),
    ("ruc_proveedor", "ruc"),
    ("nombre_proveedor", "nombreProveedor"),
    ("monto", "total"),
    ("moneda", "moneda"),
    ("fecha_emision", "fechaEmision"),
)


@dataclass(frozen=True)
class ComprobanteParaEvento:
    """Espejo de `procesamiento_repo.DatosExtraidos` (BACKLOG #6) — solo los campos que el
    payload necesita, sin acoplar este modulo al dataclass de escritura de #6."""

    tipo_comprobante: str | None
    numero: str | None
    ruc_proveedor: str | None
    nombre_proveedor: str | None
    monto: Decimal | None
    moneda: str | None
    fecha_emision: date | None
    campos_no_extraidos: str | None
    afectacion_mixta: bool | None


def construir_payload(
    *,
    estado_procesamiento: str,
    documento_recibido_id: int,
    tipo_documento: str,
    documento_asociado_id: int | None,
    comprobante: ComprobanteParaEvento | None,
) -> str:
    """Devuelve el JSON serializado (`fact.InboxEvent.Payload`, `NVARCHAR(MAX)`) — la forma de
    design.md's Interfaces/Contracts, `version=1`. `comprobante=None` es el caso `Estado='ERROR'`:
    #6 nunca escribe `fact.DatosExtraidos` para un documento fallido (spec.md 'Failed processing
    still emits an event'), asi que no hay comprobante que reportar."""
    cuerpo = {
        "version": _VERSION,
        "estadoProcesamiento": estado_procesamiento,
        "documento": {
            "documentoRecibidoId": documento_recibido_id,
            "tipoDocumento": tipo_documento,
            "documentoAsociadoId": documento_asociado_id,
        },
        "comprobante": _comprobante_dict(comprobante),
        "evidencia": _evidencia(comprobante, tipo_documento),
        "afectacionMixta": comprobante.afectacion_mixta if comprobante else None,
        "camposNoExtraidos": _lista_campos_no_extraidos(comprobante),
        # Sin tabla de advertencias en el esquema (no-migration scope, proposal.md): SIN_PAREJA es
        # la unica advertencia derivable puramente de DocumentoAsociadoId IS NULL.
        "advertenciasAsociacion": [] if documento_asociado_id is not None else ["SIN_PAREJA"],
    }
    return json.dumps(cuerpo, ensure_ascii=False)


def _comprobante_dict(c: ComprobanteParaEvento | None) -> dict | None:
    if c is None:
        return None
    return {
        "tipoComprobante": c.tipo_comprobante,
        "numero": c.numero,
        "rucProveedor": c.ruc_proveedor,
        "nombreProveedor": c.nombre_proveedor,
        "monto": str(c.monto) if c.monto is not None else None,
        "moneda": c.moneda,
        "fechaEmision": c.fecha_emision.isoformat() if c.fecha_emision else None,
    }


def _evidencia(c: ComprobanteParaEvento | None, tipo_documento: str) -> list[dict]:
    if c is None:
        return []
    filas = []
    for atributo, campo in _CAMPOS_EVIDENCIA:
        valor = getattr(c, atributo)
        if valor is None:
            continue
        filas.append({"campo": campo, "valor": str(valor), "fuente": tipo_documento})
    return filas


def _lista_campos_no_extraidos(c: ComprobanteParaEvento | None) -> list[str]:
    if c is None or not c.campos_no_extraidos:
        return []
    return [campo for campo in c.campos_no_extraidos.split(",") if campo]
