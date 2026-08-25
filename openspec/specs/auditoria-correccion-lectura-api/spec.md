# Auditoría Corrección — Lectura (API) Specification

## Purpose

Expose a read-only view of `fact.AuditoriaCorreccion` per factura/asiento so
the SPA's correction-history panel can be populated with real data instead of
mock. Pure projection over data already written by
`IUnidadDeTrabajo.RegistrarAuditoriaAsync` — introduces no new accounting
rule and no schema change.

## Requirements

### Requirement: Correction history is readable per factura/asiento

The system MUST expose a read-only endpoint that returns every
`fact.AuditoriaCorreccion` entry for a given factura/asiento identifier,
ordered newest first, without mutating any state.

#### Scenario: Factura with existing correction history

- GIVEN a factura whose asiento has one or more `AuditoriaCorreccion` rows
- WHEN the correction-history endpoint is called for that factura/asiento
- THEN the response returns those entries ordered from newest to oldest

#### Scenario: Read endpoint does not write

- GIVEN any call to the correction-history endpoint
- WHEN the request completes successfully
- THEN no row is inserted, updated, or deleted in `fact.AuditoriaCorreccion`
  or any other table as a side effect

### Requirement: Each history entry includes field, previous value, new value, and timestamp

The system MUST project each `AuditoriaCorreccion` entry with, at minimum,
the corrected field/entity identifier, the previous value, the new value, the
`Accion` performed, and the timestamp of the correction.

#### Scenario: Entry shape for a field correction

- GIVEN an `AuditoriaCorreccion` row with `Accion=CORRECCION` on a factura
  field
- WHEN it is included in the endpoint response
- THEN the returned entry exposes the field identifier, previous value, new
  value, `Accion`, and timestamp

### Requirement: Absence of history returns an empty result, not an error

The system MUST return a successful empty result (not a 404/error) when a
factura/asiento has no `AuditoriaCorreccion` entries.

#### Scenario: Factura never corrected

- GIVEN a factura/asiento with zero `AuditoriaCorreccion` rows
- WHEN the correction-history endpoint is called
- THEN the response succeeds with an empty list

### Requirement: No new accounting rule is introduced

The read endpoint MUST NOT evaluate, enforce, or alter any
`InvarianteContable` or accounting rule — it is a pure projection of
already-persisted audit data.

#### Scenario: Endpoint call has no effect on invariants

- GIVEN a factura/asiento in any state
- WHEN the correction-history endpoint is called
- THEN no accounting invariant is evaluated or enforced as part of the call
