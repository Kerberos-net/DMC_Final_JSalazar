# Factura Promotion Specification

## Purpose

`SmartNet.Inbox.Core`/`Infrastructure` consume `InboxEvent` and decide, inside .NET's own
transaction, whether the processed document becomes a `Factura` (ADR 0005). The decision is
structural (data-sufficiency), never a REGLAS.md §1-4 validation pass, and never trusts the
Python-side notification as an instruction to create anything.

## Requirements

### Requirement: Pure promotion decision

`SmartNet.Inbox.Core` MUST decide promote/no-promote and compute 5 of the 6 documented indicator
flags as a pure function over the `InboxEvent.Payload`, with no database, HTTP, or clock dependency
(ADR 0019 level 1), and MUST pass `PurityScanTests`. `EsReferenciaExterna` keeps its DDL default
(`0`) — `DatosExtraidos` has no reference-nota columns to derive it from in this item; notas de
crédito is item #10 (design D5, ADR 0005).

#### Scenario: Decision is computed without infrastructure

- GIVEN an `InboxEvent.Payload` deserialized in memory
- WHEN `SmartNet.Inbox.Core` evaluates it
- THEN it returns a promote/no-promote decision plus the 5 computed indicator flags without
  touching a database connection, an HTTP client, or `DateTime.Now`

### Requirement: Sufficient data promotes to Factura

The system MUST create `Factura` (`Estado='PENDIENTE_VALIDACION'`) plus its `FacturaExtraccion`
rows and the 5 computed indicator flags when the payload contains every field structurally required
to construct them, and MUST mark the source `InboxEvent` `EstadoConsumo='PROMOVIDO'` with the new
`FacturaId`.

#### Scenario: Complete comprobante data promotes successfully

- GIVEN a `PENDIENTE` `InboxEvent` whose `Payload` contains all fields required to construct
  `Factura` and `FacturaExtraccion`
- WHEN the hosted consumer processes it
- THEN a `Factura` row is created with `Estado='PENDIENTE_VALIDACION'` and `ProcesamientoId` set
- AND matching `FacturaExtraccion` rows are created recording source (`XML`/`PDF`) per field — no
  confidence value: no component computes or persists one (D4, ADR 0017)
- AND the 5 computed indicator flags are persisted as fields on `Factura`; `EsReferenciaExterna`
  keeps its DDL default
- AND the `InboxEvent` is updated to `EstadoConsumo='PROMOVIDO'`, `FacturaId=<new id>`

### Requirement: Insufficient data creates no Factura

The system MUST NOT create any `Factura` row — including no placeholder or `ERROR`-state row —
when the payload lacks a field structurally required to construct `Factura` or
`FacturaExtraccion`. It MUST instead mark the `InboxEvent` `EstadoConsumo='DESCARTADO'` with a
`MotivoDescarte`.

#### Scenario: Missing required field is discarded, not faked

- GIVEN a `PENDIENTE` `InboxEvent` whose `Payload` is missing a field structurally required to
  construct `Factura` (e.g. no comprobante number, no RUC, no monto)
- WHEN the hosted consumer processes it
- THEN no `Factura` row is created
- AND the `InboxEvent` is updated to `EstadoConsumo='DESCARTADO'` with a `MotivoDescarte`
  describing the missing field
- AND the document later surfaces in the Angular Inbox as pending manual review

#### Scenario: Structural check does not weigh REGLAS.md business rules

- GIVEN a `PENDIENTE` `InboxEvent` whose `Payload` has every field required to construct `Factura`
  and `FacturaExtraccion`, but whose values would fail a REGLAS.md §1-4 business validation
- WHEN the hosted consumer processes it
- THEN the document is still promoted to `Factura` (`PENDIENTE_VALIDACION`)
- AND REGLAS.md §1-4 validation is deferred to the existing validation flow, not evaluated here

### Requirement: Idempotent promotion

Re-running promotion for an `InboxEvent` whose underlying `Procesamiento` already produced a
`Factura` MUST NOT create a duplicate `Factura`, relying on `UQ_Factura_Procesamiento`.

#### Scenario: Reprocessing an already-promoted event is a safe no-op

- GIVEN an `InboxEvent` whose `ProcesamientoId` already has a `Factura` row (via
  `UQ_Factura_Procesamiento`)
- WHEN the hosted consumer attempts promotion for that event again
- THEN the unique-index violation is caught and treated as an idempotent no-op
- AND no second `Factura` row is created
- AND the `InboxEvent` ends in `EstadoConsumo='PROMOVIDO'` referencing the existing `Factura`

### Requirement: Promotion seeds the factura's BORRADOR asiento

When promotion creates a new `Factura` (`Estado='PENDIENTE_VALIDACION'`), the system MUST also
run the `abrir` compose+seed step for that factura in the same flow, producing an engine-
composed `BORRADOR` asiento (header projection + PRINCIPAL/DESTINO líneas, default cargo
account from `ServicioDeSugerencia`).

If the seed cannot be produced because the factura is foreign-currency and no vigente
`fact.TipoCambio` exists, promotion MUST still succeed — the `Factura` is created without an
asiento, and the detalle screen later offers "generar asiento" once a tipo de cambio exists.
The seed failure MUST NOT roll back or block the factura promotion.

The seed step MUST NOT run on the associated-document merge branch (a `PENDIENTE` event with
non-null `documentoAsociadoId`): that branch projects a `fact.DocumentoFactura` row onto an
already-promoted partner factura and creates no `Factura`, so it also creates no asiento
(#25/#26 behavior unchanged).

#### Scenario: Complete PEN factura is promoted with a seeded asiento — [new]
- GIVEN a `PENDIENTE` `InboxEvent` whose payload promotes to a PEN `Factura`
- WHEN the hosted consumer processes it
- THEN the `Factura` is created `PENDIENTE_VALIDACION` AND a `BORRADOR` asiento is seeded with
  engine-composed header scalars and PRINCIPAL/DESTINO líneas
- (test: E2E promotion→asiento / integration)

#### Scenario: Foreign-currency factura with no rate promotes without an asiento — [new]
- GIVEN a `PENDIENTE` event that promotes to a USD `Factura` whose fecha de emisión has no
  vigente `fact.TipoCambio`
- WHEN the consumer processes it
- THEN the `Factura` is created `PENDIENTE_VALIDACION` with no asiento; the event ends
  `EstadoConsumo='PROMOVIDO'`; the detalle screen later offers "generar asiento"
- (test: E2E / integration)
- NOTE FOR DESIGN: confirm this matches owner intent. If a failed seed should instead fail the
  promotion, flag it during design.

#### Scenario: Associated-PDF merge branch seeds no asiento (regression guard) — [new]
- GIVEN a `PENDIENTE` event with non-null `documentoAsociadoId` resolving to an already-
  promoted partner `Factura`
- WHEN the consumer processes it
- THEN a `fact.DocumentoFactura` row is inserted on the partner `FacturaId`, no new `Factura`
  is created, and no asiento seed runs (#25/#26 path unchanged)
- (test: E2E / integration)

#### Scenario: Idempotent re-promotion does not re-seed — [new]
- GIVEN an `InboxEvent` whose `ProcesamientoId` already produced a `Factura` (with or without
  an asiento)
- WHEN promotion is attempted again
- THEN the unique-index violation is an idempotent no-op and no second asiento is seeded
- (test: E2E / integration)

### Requirement: Data-partition boundary respected

The system MUST read `InboxEvent` and write `Factura`/`FacturaExtraccion`/`InboxEvent` only via
`SmartNet.Inbox.Infrastructure` under the `usr_api` role, and MUST NOT read `Procesamiento` or any
other Python-private table (ADR 0003).

#### Scenario: Consumer never touches Procesamiento

- GIVEN the hosted background consumer is running
- WHEN it evaluates and promotes/discards `InboxEvent` rows
- THEN it issues no query against `Procesamiento` or any other worker-private table

### Requirement: Associated-document event projects onto the partner factura

When a `PENDIENTE` `InboxEvent` carries a non-null `documentoAsociadoId`, the
system MUST NOT run the structural sufficiency check (`PoliticaDePromocion`) for
that event and MUST NOT create a second `Factura`. Instead it MUST resolve the
partner's already-promoted, non-`DESCARTADA` `Factura` by joining
`fact.DocumentoFactura` (on `DocumentoRecibidoId = documentoAsociadoId`) to
`fact.Factura`, using only tables granted to `usr_api` (ADR 0003). If found, it
MUST insert this event's single `fact.DocumentoFactura` row onto that
`FacturaId` in one transaction and mark the event `EstadoConsumo='PROMOVIDO'`
with that `FacturaId`. If not found, it MUST leave the event `PENDIENTE` for a
later cycle (self-heal). This branch never fires when `documentoAsociadoId` is
null. (New behavior — ADR 0019 level 1 unit for the branch decision; level 2
boundary contract for the merge insert.)

#### Scenario: Partner factura found — PDF projects, no second factura

- GIVEN a `PENDIENTE` event with non-null `documentoAsociadoId`
- AND a non-`DESCARTADA` `Factura` exists for that partner `DocumentoRecibidoId`
- WHEN the consumer processes the event
- THEN a `fact.DocumentoFactura` row for this event is inserted on the partner `FacturaId`
- AND no new `Factura` row is created and no sufficiency check runs
- AND the event ends `EstadoConsumo='PROMOVIDO'` with that `FacturaId`

#### Scenario: Partner not yet promoted — defer and self-heal

- GIVEN a `PENDIENTE` event with non-null `documentoAsociadoId`
- AND no non-`DESCARTADA` `Factura` is resolvable for the partner yet
- WHEN the consumer processes the event
- THEN no `Factura` and no `fact.DocumentoFactura` row is created
- AND the event stays `EstadoConsumo='PENDIENTE'`
- AND a later cycle promotes it once the partner factura exists

#### Scenario: Order independence

- GIVEN an XML event and its associated PDF event both `PENDIENTE`
- WHEN they are processed in either order across cycles
- THEN the end state is exactly one `Factura` with two `fact.DocumentoFactura` rows

#### Scenario: Paired XML discarded — associated PDF does not self-promote

- GIVEN a `PENDIENTE` event with non-null `documentoAsociadoId`
- AND the paired XML event was discarded (`EstadoConsumo='DESCARTADO'`, no non-`DESCARTADA` `Factura`)
- WHEN the consumer processes the associated PDF event
- THEN the lookup resolves nothing and no `Factura` is created for the PDF
- AND the PDF event does not promote on its own (it defers with the XML)

#### Scenario: Unassociated PDF still promotes on its own (regression guard)

- GIVEN a `PENDIENTE` PDF event with `documentoAsociadoId` null and all 4 required fields present
- WHEN the consumer processes it
- THEN the normal sufficiency path runs and creates its own `Factura` as today

#### Scenario: Re-emitted associated event is an idempotent no-op

- GIVEN an associated PDF event already projected onto a partner `FacturaId`
- AND `reprocesar` re-emits an event for the same `DocumentoRecibidoId`
- WHEN the consumer processes the re-emitted event
- THEN the merge insert hits `UQ_DocumentoFactura_DocumentoRecibidoId` (SQL 2601/2627), is caught, and no duplicate row is written
- AND no second `Factura` is created

#### Scenario: No spurious PosibleDuplicado from the paired PDF

- GIVEN an XML+PDF pair promoted via this branch
- WHEN promotion completes
- THEN only the XML's `Factura` exists and no second `Factura` with `PosibleDuplicado=1` is produced by the PDF event

### Requirement: Independent polling cadence

The hosted consumer SHOULD poll `InboxEvent` where `EstadoConsumo='PENDIENTE'` on a fixed 1-minute
cadence, independent of the Python publishing step, within ADR 0005's 15-minute visibility budget.

#### Scenario: Consumer runs on its own schedule

- GIVEN the hosted background service's timer
- WHEN one minute elapses since the last poll
- THEN it queries `InboxEvent` for `EstadoConsumo='PENDIENTE'` rows and processes each
