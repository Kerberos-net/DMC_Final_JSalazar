# Outbox Consumo Specification

## Purpose

`fact.OutboxEvent` and `fact.CommandQueue` exist since item #1, but nothing reads them: no
consumer claims rows, applies the obsolescence guard, or dispatches to a handler. This capability
is destination-agnostic plumbing — no Drive/Sheets handler (items #15/#16) is implemented here —
that the future consumers plug into.

## Requirements

### Requirement: Batch claim via READPAST behind an interface

The system MUST claim batches of `OutboxEvent`/`CommandQueue` rows using SQL Server `READPAST`,
and this dependency MUST be isolated behind an interface (e.g. `IReclamoDeLote`) so no
destination-agnostic dispatch code imports the SQL-Server-specific implementation directly
(ADR 0002's one declared engine-specific exception).

#### Scenario: Dispatcher depends only on the interface

- GIVEN the dispatcher module that routes claimed rows to handlers
- WHEN its imports are inspected
- THEN it references only the batch-claim interface, never a `READPAST`/SQL-Server-specific
  symbol

#### Scenario: Concurrent claims do not double-process a row

- GIVEN two claim cycles running close together
- WHEN both attempt to claim the same pending row
- THEN `READPAST` causes the second cycle to skip the row already locked by the first, and the row
  is processed by exactly one cycle

### Requirement: Five-minute claim lease with reclaim on expiry

A batch claim MUST hold a lease of exactly **5 minutes** (confirmed by the project owner, design
Open Question 3), within ADR 0005's 15-minute visibility budget. A claim whose lease expires without
being finalized (committed or marked `OBSOLETO`) MUST become reclaimable by a subsequent claim cycle.

#### Scenario: A claim held past its 5-minute lease becomes reclaimable

- GIVEN a row claimed by one cycle and left unfinalized for longer than 5 minutes
- WHEN a later claim cycle runs
- THEN that row is claimable again by the later cycle

#### Scenario: A claim finalized within the lease is not reclaimed

- GIVEN a row claimed and finalized (committed or marked `OBSOLETO`) within 5 minutes
- WHEN a later claim cycle runs
- THEN that row is not offered for reclaim

### Requirement: Obsolescence guard precedes handler dispatch

Before any handler runs, the system MUST compare the claimed event's `Secuencia` against the
per-destination progress recorded in `OutboxEventIntegracion`. If the claimed `Secuencia` does not
exceed the recorded one, the system MUST mark the claim `OBSOLETO` and MUST NOT invoke the handler.

#### Scenario: A stale event is marked OBSOLETO without dispatch

- GIVEN `OutboxEventIntegracion` already reflects `Secuencia=5` for a given `Factura`/destination
- WHEN a claimed event for that agregado/destination carries `Secuencia=3`
- THEN the claim is marked `OBSOLETO`
- AND no handler is invoked for that claim

#### Scenario: A current event proceeds to dispatch

- GIVEN `OutboxEventIntegracion` reflects `Secuencia=5` for a given agregado/destination
- WHEN a claimed event for that agregado/destination carries `Secuencia=6`
- THEN the guard passes and the handler is invoked

### Requirement: OBSOLETO is never an error or alert

`OBSOLETO` MUST be treated as a distinct, non-error terminal outcome (ADR 0010): it MUST NOT be
routed through item #17's `TRANSITORIO`/`DIFERIBLE`/`PERMANENTE` classification, MUST NOT increment
any error/retry counter, and MUST NOT trigger a notification.

#### Scenario: OBSOLETO does not count as a retry-eligible error

- GIVEN a claim marked `OBSOLETO` by the guard
- WHEN incidence/error metrics are computed
- THEN the `OBSOLETO` claim is excluded from error, retry, and alert counts

#### Scenario: A burst of corrections produces OBSOLETO claims with no alert

- GIVEN several rapid corrections on the same `Factura` produce several superseded events
- WHEN the consumer processes them
- THEN each superseded claim is marked `OBSOLETO` and no alert is raised for the burst

### Requirement: One-minute independent consumer cadence

The consumer SHOULD run on a fixed 1-minute cadence, on its own scheduler independent of the
InboxEvent-publishing side (item #11) and of any other component, within ADR 0005's 15-minute
visibility budget.

#### Scenario: Consumer cycle runs on its own schedule

- GIVEN the consumer's scheduler
- WHEN one minute elapses since the last run
- THEN the consumer claims and processes pending `OutboxEvent`/`CommandQueue` rows on that cycle,
  independent of the InboxEvent-publishing schedule

### Requirement: Bidirectional boundary contract tests

The system MUST have contract tests (ADR 0019 nivel 2) exercising `OutboxEvent` and `CommandQueue`
from both sides — .NET writes read back by Python, and Python writes/updates read back by .NET —
against the real applied schema, and MUST verify the `usr_api`/`usr_worker` permission matrix
(ADR 0003).

#### Scenario: .NET-written OutboxEvent is readable by Python under usr_worker

- GIVEN an `OutboxEvent` row inserted by .NET under `usr_api`
- WHEN Python reads it under `usr_worker`
- THEN the read succeeds and every field round-trips with the same type and value

#### Scenario: usr_worker cannot write tables it does not own

- GIVEN the `usr_worker` role
- WHEN it attempts to write a table outside its granted set (e.g. `fact.Factura`)
- THEN the write is rejected by the database permission grant

#### Scenario: usr_api cannot read worker-private tables

- GIVEN the `usr_api` role
- WHEN it attempts to read a table reserved for the worker (e.g. `Procesamiento`)
- THEN the read is rejected by the database permission grant
