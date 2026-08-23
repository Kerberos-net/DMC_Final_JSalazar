"""ADR 0019 level-2 contract test (BACKLOG #7, WU4, tasks 4.4/4.5) -- `payload_inbox.construir_payload`
must produce the EXACT byte-for-byte-equivalent JSON structure that .NET's `PayloadInboxParser`
parses (`SmartNet.Inbox.Infrastructure.Tests.PayloadInboxContractTests`, task 4.6). Both suites read
the SAME fixture file (`tests/fixtures/inbox_event_payload.golden.json`) -- proving the two sides of
the wire format actually agree, not just that each side independently self-asserts its own shape.
"""

from __future__ import annotations

import json
from datetime import date
from decimal import Decimal
from pathlib import Path

from smartnet_worker.payload_inbox import ComprobanteParaEvento, construir_payload

_GOLDEN_PATH = Path(__file__).resolve().parents[1] / "fixtures" / "inbox_event_payload.golden.json"


def test_construir_payload_matches_the_golden_fixture_dotnet_also_reads():
    golden = json.loads(_GOLDEN_PATH.read_text(encoding="utf-8"))

    comprobante = ComprobanteParaEvento(
        tipo_comprobante="01",
        numero="F001-123",
        ruc_proveedor="20100000001",
        nombre_proveedor="Acme SAC",
        monto=Decimal("1180.00"),
        moneda="PEN",
        fecha_emision=date(2026, 8, 10),
        campos_no_extraidos=None,
        afectacion_mixta=False,
    )

    payload = json.loads(
        construir_payload(
            estado_procesamiento="COMPLETADO",
            documento_recibido_id=8,
            tipo_documento="XML",
            documento_asociado_id=9,
            nombre_archivo="factura.xml",
            mime_type="application/xml",
            ruta_relativa="2026/08/factura.xml",
            tamano_bytes=2048,
            comprobante=comprobante,
        )
    )

    assert payload == golden
