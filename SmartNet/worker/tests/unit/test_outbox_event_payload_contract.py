"""ADR 0019 level-2 contract test (BACKLOG #14, Fase 5, tasks.md 5.1) -- proves the Python consumer
never mutates the outbox envelope `PayloadOutbox.Serializar` (.NET) produces: `EventoReclamado`
treats `Payload` as an OPAQUE string end to end (design.md, Interfaces/Contracts: "the consumer
treats Payload as an opaque string in #14"). Both suites read the SAME fixture file
(`tests/fixtures/outbox_event_payload.golden.json`) -- the .NET side
(`PayloadOutboxContractTests.cs`, same mechanism as `PayloadInboxContractTests.cs`) asserts
`PayloadOutbox.Serializar` PRODUCES it byte-for-byte; this side asserts `OutboxRepo.reclamar`'s
row->dataclass mapping PASSES IT THROUGH unchanged, never re-serializing or re-parsing it.
"""

from __future__ import annotations

import json
from datetime import UTC, datetime
from pathlib import Path

from smartnet_worker.outbox_repo import OutboxRepo

_GOLDEN_PATH = Path(__file__).resolve().parents[1] / "fixtures" / "outbox_event_payload.golden.json"


class _CursorFalsoConUnaFilaReclamada:
    """Fake cursor -- same shape as `test_outbox_repo.py`'s fakes: `execute` ignores the SQL/params
    and `fetchall` returns exactly one row shaped like `OutboxRepo.reclamar`'s SELECT
    (`OutboxEventId, Integracion, FacturaId, Tipo, Payload, Secuencia`), carrying the fixture text
    verbatim as the Payload column so the assertion below would catch even a whitespace-changing
    round trip."""

    def __init__(self, payload_texto: str) -> None:
        self._fila = (1, "DRIVE", 100, "FACTURA_VALIDADA", payload_texto, 1, 0)

    def execute(self, sql, *parametros):
        return self

    def fetchall(self):
        return [self._fila]


def test_eventoreclamado_payload_es_identico_byte_a_byte_al_que_produce_dotnet():
    payload_texto_dotnet = _GOLDEN_PATH.read_text(encoding="utf-8")
    # .NET escribe el payload como una sola linea NVARCHAR(MAX) via System.Text.Json.Serialize sin
    # indentar (PayloadOutbox.Serializar, ver PayloadOutboxContractTests.cs); la fixture en disco
    # esta pretty-printed para revision humana, asi que la unica normalizacion legitima aqui es UN
    # round-trip json.loads/json.dumps compacto -- la MISMA forma que llegaria por
    # fact.OutboxEvent.Payload -- nunca una reescritura de campos.
    payload_normalizado = json.dumps(json.loads(payload_texto_dotnet), separators=(",", ":"))

    cursor = _CursorFalsoConUnaFilaReclamada(payload_normalizado)
    repo = OutboxRepo(cursor)

    reclamados = repo.reclamar(destinos=("DRIVE",), limite=10, ahora=datetime.now(UTC))

    assert len(reclamados) == 1
    # Identidad de CADENA, no solo de estructura -- reclamo.py/outbox_repo.py nunca deben tocar
    # `Payload` en ningun punto de la infraestructura del consumidor (design.md: "opaque string").
    assert reclamados[0].payload == payload_normalizado
    assert json.loads(reclamados[0].payload) == json.loads(payload_texto_dotnet)
