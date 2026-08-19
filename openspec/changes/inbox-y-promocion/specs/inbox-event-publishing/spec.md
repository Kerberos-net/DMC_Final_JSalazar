# Inbox Event Publishing Specification

## Purpose

Python reports one fact per finished document — success or failure — so the outcome of processing
is visible outside worker-private tables (ADR 0005). This is a notification, never a domain
operation request (ADR 0003).

## Requirements

### Requirement: One InboxEvent per finished document

The system MUST write exactly one `fact.InboxEvent` row for every `Procesamiento` row that
`cli_procesamiento.py` finishes, regardless of outcome, in a step separate from and after item #6's
already-closed processing transaction.

#### Scenario: Successful processing emits an event

- GIVEN a `Procesamiento` row committed with `Estado='COMPLETADO'`
- WHEN the InboxEvent publishing step runs
- THEN one `InboxEvent` row is inserted with `Tipo='PROCESAMIENTO_FINALIZADO'` and
  `EstadoConsumo='PENDIENTE'`
- AND `Payload` carries comprobante data, per-field evidence (`Fuente` only — no confidence value:
  no component computes or persists one, D4/ADR 0017), `AfectacionMixta`, and association warnings

#### Scenario: Failed processing still emits an event

- GIVEN a `Procesamiento` row committed with `Estado='ERROR'`
- WHEN the InboxEvent publishing step runs
- THEN one `InboxEvent` row is inserted with `Tipo='PROCESAMIENTO_FINALIZADO'` and
  `EstadoConsumo='PENDIENTE'`
- AND outcome (success/failure) is derivable only from the referenced `Procesamiento.Estado`, never
  from a second `Tipo` literal

### Requirement: Idempotent publishing

The system MUST NOT emit more than one `InboxEvent` per `Procesamiento` row across repeated runs of
the publishing step.

#### Scenario: Re-running the scan does not duplicate events

- GIVEN a `Procesamiento` row that already has a corresponding `InboxEvent`
- WHEN the publishing step runs again
- THEN no additional `InboxEvent` row is inserted for that `Procesamiento`

### Requirement: Data-partition boundary respected

The system MUST NOT read or write any table owned by .NET (`fact.Factura`,
`fact.FacturaExtraccion`) from the Python side, and MUST write `InboxEvent` using the `fact_worker`
role only (ADR 0003).

#### Scenario: Publishing step uses only worker-owned access

- GIVEN the publishing step is inserting an `InboxEvent` row
- WHEN the insert executes
- THEN it runs under `fact_worker` and touches only `Procesamiento` (read) and `InboxEvent`
  (insert)

### Requirement: Independent polling cadence

The publishing step SHOULD run on a fixed 1-minute cadence, independent of item #6's pipeline and
of the .NET consumer's own cadence, within ADR 0005's 15-minute visibility budget.

#### Scenario: Scan runs on its own schedule

- GIVEN the publishing step's scheduler
- WHEN one minute elapses since the last run
- THEN the step scans for `Procesamiento` rows lacking a corresponding `InboxEvent` and processes
  them
