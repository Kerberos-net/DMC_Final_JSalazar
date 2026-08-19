# Factura Promotion Specification

## Purpose

`SmartNet.Inbox.Core`/`Infrastructure` consume `InboxEvent` and decide, inside .NET's own
transaction, whether the processed document becomes a `Factura` (ADR 0005). The decision is
structural (data-sufficiency), never a REGLAS.md §1-4 validation pass, and never trusts the
Python-side notification as an instruction to create anything.

## Requirements

### Requirement: Pure promotion decision

`SmartNet.Inbox.Core` MUST decide promote/no-promote and compute the 6 indicator flags as a pure
function over the `InboxEvent.Payload`, with no database, HTTP, or clock dependency (ADR 0019
level 1), and MUST pass `PurityScanTests`.

#### Scenario: Decision is computed without infrastructure

- GIVEN an `InboxEvent.Payload` deserialized in memory
- WHEN `SmartNet.Inbox.Core` evaluates it
- THEN it returns a promote/no-promote decision plus the 6 indicator flags without touching a
  database connection, an HTTP client, or `DateTime.Now`

### Requirement: Sufficient data promotes to Factura

The system MUST create `Factura` (`Estado='PENDIENTE_VALIDACION'`) plus its `FacturaExtraccion`
rows and the 6 indicator flags when the payload contains every field structurally required to
construct them, and MUST mark the source `InboxEvent` `EstadoConsumo='PROMOVIDO'` with the new
`FacturaId`.

#### Scenario: Complete comprobante data promotes successfully

- GIVEN a `PENDIENTE` `InboxEvent` whose `Payload` contains all fields required to construct
  `Factura` and `FacturaExtraccion`
- WHEN the hosted consumer processes it
- THEN a `Factura` row is created with `Estado='PENDIENTE_VALIDACION'` and `ProcesamientoId` set
- AND matching `FacturaExtraccion` rows are created recording source (`XML`/`PDF`) and confidence
  per field
- AND the 6 indicator flags are persisted as fields on `Factura`
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

### Requirement: Data-partition boundary respected

The system MUST read `InboxEvent` and write `Factura`/`FacturaExtraccion`/`InboxEvent` only via
`SmartNet.Inbox.Infrastructure` under the `usr_api` role, and MUST NOT read `Procesamiento` or any
other Python-private table (ADR 0003).

#### Scenario: Consumer never touches Procesamiento

- GIVEN the hosted background consumer is running
- WHEN it evaluates and promotes/discards `InboxEvent` rows
- THEN it issues no query against `Procesamiento` or any other worker-private table

### Requirement: Independent polling cadence

The hosted consumer SHOULD poll `InboxEvent` where `EstadoConsumo='PENDIENTE'` on a fixed 1-minute
cadence, independent of the Python publishing step, within ADR 0005's 15-minute visibility budget.

#### Scenario: Consumer runs on its own schedule

- GIVEN the hosted background service's timer
- WHEN one minute elapses since the last poll
- THEN it queries `InboxEvent` for `EstadoConsumo='PENDIENTE'` rows and processes each
