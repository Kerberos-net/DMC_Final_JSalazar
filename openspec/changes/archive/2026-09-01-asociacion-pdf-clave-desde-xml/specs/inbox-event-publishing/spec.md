# Delta for Inbox Event Publishing

## MODIFIED Requirements

### Requirement: Idempotent publishing

The system MUST NOT emit a redundant `InboxEvent` for a `Procesamiento` row across repeated
runs of the publishing step. Emitting more than one row per `ProcesamientoId` is permitted
ONLY when a later, distinct fact must be reported that no existing event for that
`ProcesamientoId` already reflects — specifically a PDF `Procesamiento` whose
`DocumentoAsociadoId` transitioned from NULL to non-null after every existing event for that
row was emitted. Because `fact_worker` holds SELECT/INSERT only on `fact.InboxEvent` (no
UPDATE) and the schema allows multiple rows per `ProcesamientoId`, the re-emit MUST be a new
INSERT. The candidate predicate MUST be: the underlying `fact.DocumentoRecibido.TipoDocumento`
is `PDF` AND the `Procesamiento` currently has a non-null `DocumentoAsociadoId` AND no existing
`fact.InboxEvent` for that `ProcesamientoId` has a payload whose
`$.documento.documentoAsociadoId` is non-null. When that predicate is false, no additional row
is inserted. The XML side of an association MUST NOT be re-emitted (see the ADDED requirement).
(Previously: at most one `InboxEvent` per `Procesamiento`, ever; no re-emission on any later state change.)

#### Scenario: Re-running the scan does not duplicate events

- GIVEN a `Procesamiento` row that already has a corresponding `InboxEvent` reflecting its current state
- WHEN the publishing step runs again
- THEN no additional `InboxEvent` row is inserted for that `Procesamiento`

#### Scenario: Association reflected in the first event gets no second event — MODIFIED (regression guard)

- GIVEN a `Procesamiento` whose only `InboxEvent` was emitted when `DocumentoAsociadoId` was
  already non-null (same-run XML+PDF association)
- WHEN the publishing step runs again
- THEN no additional `InboxEvent` row is inserted

## ADDED Requirements

### Requirement: A PDF-only NULL→non-null `DocumentoAsociadoId` transition re-emits an InboxEvent

When a **PDF** `Procesamiento` row's `DocumentoAsociadoId` transitions from NULL to non-null
after its `InboxEvent`(s) were already emitted (for example, via the second association pass),
the system MUST insert a new `fact.InboxEvent` row whose payload reflects the association, so
the downstream .NET promotion/merge path can run. `asociar_documentos` writes
`DocumentoAsociadoId` on BOTH sides of a pair, so the XML `Procesamiento` also transitions;
the XML side MUST NOT be re-emitted, because shipped #25's `EsDocumentoAsociado` predicate
(`DocumentoAsociadoId != null && TipoDocumento == "PDF"`) is pinned false for XML, and a
re-emitted XML event would fall through into `PoliticaDePromocion.Decidir` → `PromoverAsync`
and create a SECOND `fact.Factura`. The candidate query MUST filter
`fact.DocumentoRecibido.TipoDocumento = 'PDF'`. The re-emit MUST reuse the existing payload
builder with `_VERSION` unchanged (1) — only the trigger is new — and MUST run under
`fact_worker` (SELECT/INSERT on `fact.InboxEvent`, read on `Procesamiento` /
`DocumentoRecibido`). Test level per ADR 0019: integration for the candidate query; `pytest`.
No `dotnet test`.

#### Scenario: Late PDF association produces a new event — NEW

- GIVEN a PDF `Procesamiento` whose `InboxEvent`(s) were all emitted while `DocumentoAsociadoId` was NULL
- WHEN `DocumentoAsociadoId` later becomes non-null and the publishing step runs
- THEN one new `InboxEvent` row is inserted for that `ProcesamientoId` with `EstadoConsumo='PENDIENTE'`
  and a payload that carries the association

#### Scenario: The XML side of the association is not re-emitted — NEW (regression guard)

- GIVEN an XML `Procesamiento` whose `DocumentoAsociadoId` transitioned NULL→non-null when its PDF was
  associated, and whose original `InboxEvent` was already emitted
- WHEN the publishing step runs
- THEN no new `InboxEvent` row is inserted for the XML `ProcesamientoId` (candidate query filters `TipoDocumento = 'PDF'`)

#### Scenario: Re-emit is not repeated once reflected — NEW

- GIVEN a `Procesamiento` for which the association-reflecting `InboxEvent` was already inserted
- WHEN the publishing step runs again
- THEN no further `InboxEvent` row is inserted for that `ProcesamientoId`

#### Scenario: Re-emit respects the data-partition boundary — NEW

- GIVEN the re-emit candidate query and insert execute
- WHEN they run
- THEN they touch only `fact.Procesamiento` (read) and `fact.InboxEvent` (insert) under `fact_worker`,
  and no `.NET`-owned table is read or written
