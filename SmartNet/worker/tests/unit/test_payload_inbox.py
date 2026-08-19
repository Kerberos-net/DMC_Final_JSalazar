"""Suite de `payload_inbox.construir_payload` (BACKLOG #7, WU1) — funcion pura, sin DB/red/reloj
(ADR 0019). Cubre la forma exacta de design.md's Interfaces/Contracts: `version`,
`estadoProcesamiento`, `documento`, `comprobante`, `evidencia[]` con solo `fuente` (D4, confirmada
— nunca `confianza`), `afectacionMixta`, `camposNoExtraidos`, `advertenciasAsociacion`.
"""

from __future__ import annotations

import json
from datetime import date
from decimal import Decimal

from smartnet_worker.payload_inbox import ComprobanteParaEvento, construir_payload


def _comprobante(**overrides) -> ComprobanteParaEvento:
    base = dict(
        tipo_comprobante="01",
        numero="F001-123",
        ruc_proveedor="20100000001",
        nombre_proveedor="Proveedor SAC",
        monto=Decimal("1180.00"),
        moneda="PEN",
        fecha_emision=date(2026, 8, 10),
        campos_no_extraidos=None,
        afectacion_mixta=False,
    )
    base.update(overrides)
    return ComprobanteParaEvento(**base)


def test_documento_completado_produce_la_forma_completa_de_design_md():
    payload = json.loads(
        construir_payload(
            estado_procesamiento="COMPLETADO",
            documento_recibido_id=8,
            tipo_documento="XML",
            documento_asociado_id=9,
            comprobante=_comprobante(),
        )
    )

    assert payload["version"] == 1
    assert payload["estadoProcesamiento"] == "COMPLETADO"
    assert payload["documento"] == {
        "documentoRecibidoId": 8,
        "tipoDocumento": "XML",
        "documentoAsociadoId": 9,
    }
    assert payload["comprobante"] == {
        "tipoComprobante": "01",
        "numero": "F001-123",
        "rucProveedor": "20100000001",
        "nombreProveedor": "Proveedor SAC",
        "monto": "1180.00",
        "moneda": "PEN",
        "fechaEmision": "2026-08-10",
    }
    assert payload["afectacionMixta"] is False
    assert payload["camposNoExtraidos"] == []
    assert payload["advertenciasAsociacion"] == []


def test_evidencia_solo_lleva_campo_valor_fuente_nunca_confianza():
    payload = json.loads(
        construir_payload(
            estado_procesamiento="COMPLETADO",
            documento_recibido_id=1,
            tipo_documento="PDF",
            documento_asociado_id=None,
            comprobante=_comprobante(),
        )
    )

    evidencia_total = {e["campo"]: e for e in payload["evidencia"]}
    assert evidencia_total["total"] == {"campo": "total", "valor": "1180.00", "fuente": "PDF"}
    for fila in payload["evidencia"]:
        assert set(fila.keys()) == {"campo", "valor", "fuente"}
    # 'igv' nunca es evidencia: fact.DatosExtraidos (#6) no tiene columna Igv.
    assert "igv" not in evidencia_total


def test_evidencia_omite_campos_ausentes_del_comprobante():
    payload = json.loads(
        construir_payload(
            estado_procesamiento="COMPLETADO",
            documento_recibido_id=1,
            tipo_documento="XML",
            documento_asociado_id=None,
            comprobante=_comprobante(numero=None, fecha_emision=None),
        )
    )

    campos = {e["campo"] for e in payload["evidencia"]}
    assert "numero" not in campos
    assert "fechaEmision" not in campos
    assert "total" in campos


def test_documento_sin_pareja_agrega_advertencia_sin_pareja():
    payload = json.loads(
        construir_payload(
            estado_procesamiento="COMPLETADO",
            documento_recibido_id=1,
            tipo_documento="XML",
            documento_asociado_id=None,
            comprobante=_comprobante(),
        )
    )

    assert payload["advertenciasAsociacion"] == ["SIN_PAREJA"]


def test_campos_no_extraidos_se_parte_por_coma():
    payload = json.loads(
        construir_payload(
            estado_procesamiento="COMPLETADO",
            documento_recibido_id=1,
            tipo_documento="XML",
            documento_asociado_id=1,
            comprobante=_comprobante(campos_no_extraidos="igv,fechaEmision"),
        )
    )

    assert payload["camposNoExtraidos"] == ["igv", "fechaEmision"]


def test_documento_fallido_sin_comprobante_produce_comprobante_null():
    payload = json.loads(
        construir_payload(
            estado_procesamiento="ERROR",
            documento_recibido_id=2,
            tipo_documento="PDF",
            documento_asociado_id=None,
            comprobante=None,
        )
    )

    assert payload["comprobante"] is None
    assert payload["evidencia"] == []
    assert payload["afectacionMixta"] is None
    assert payload["camposNoExtraidos"] == []
