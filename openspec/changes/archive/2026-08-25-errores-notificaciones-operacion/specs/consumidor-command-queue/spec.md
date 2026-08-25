# Consumidor de CommandQueue Specification

## Purpose

Python worker that executes queued `REPROCESAR`/`RECONECTAR`/`SINCRONIZAR` commands from
`fact.CommandQueue`, closing the recovery loop that #11's endpoints already expose but that
nobody consumed.

## Requirements

### Requirement: Command consumption for recovery actions
The system MUST consume pending `fact.CommandQueue` rows of type `REPROCESAR`, `RECONECTAR`, and
`SINCRONIZAR` and execute the corresponding recovery action.

#### Scenario: Reprocesar command executed
- GIVEN a `REPROCESAR` command is enqueued for a document
- WHEN the consumer claims it
- THEN the corresponding dispatch/processing is re-executed
- AND the command is marked completed on success

#### Scenario: Reconectar command executed
- GIVEN a `RECONECTAR` command is enqueued for an integration
- WHEN the consumer claims it
- THEN the integration reconnection routine runs
- AND `EstadoIntegracion` reflects the outcome

#### Scenario: Sincronizar command executed
- GIVEN a `SINCRONIZAR` command is enqueued
- WHEN the consumer claims it
- THEN pending items for that integration are synchronized

### Requirement: READPAST lease-based idempotency
The system MUST claim `CommandQueue` rows using the `reclamo.py` READPAST lease pattern (5-minute
lease) so a crashed in-flight command becomes reclaimable without manual intervention.

#### Scenario: Command claimed exclusively
- GIVEN two consumer instances poll `CommandQueue` concurrently
- WHEN both attempt to claim the same pending row
- THEN only one succeeds via READPAST locking

#### Scenario: Crash mid-execution reclaims after lease expiry
- GIVEN a command is claimed and the consumer process crashes before completion
- WHEN the 5-minute lease expires
- THEN the command becomes claimable again by another consumer pass

### Requirement: No duplicate side effects on reprocesamiento
The system MUST re-execute a `REPROCESAR` command without creating duplicate downstream records
(consistent with TECH-DESIGN.md Flujo 5's "REPROCESAR reejecuta la operación sin crear
duplicados").

#### Scenario: Reprocesar does not duplicate
- GIVEN a document was already partially processed before the error
- WHEN `REPROCESAR` re-executes it
- THEN no duplicate row is created downstream (Drive folder, Sheets row, or event)

### Requirement: Partition-respecting execution
The consumer MUST NOT write to any .NET-owned domain table; it MAY only write to
`fact.CommandQueue` and `fact.EstadoIntegracion` (ADR 0003).

#### Scenario: Consumer stays within its data partition
- GIVEN the consumer executes a queued command
- WHEN it persists execution outcome
- THEN it writes only to `CommandQueue`/`EstadoIntegracion`, never to a `dbo.*` or .NET-owned table
