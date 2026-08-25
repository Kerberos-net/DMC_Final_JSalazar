# Clasificación de Errores de Outbox Specification

## Purpose

Classify dispatch failures from `despacho_outbox.py::despachar_evento` into TRANSITORIO/DIFERIBLE/
PERMANENTE/OBSOLETO, persist the classification, and schedule retries — as a pure core (ADR 0019)
wrapped by an injected persistence/scheduling shell. Closes ADR 0010's recovery-loop gap.

## Requirements

### Requirement: Pure classification core
The system MUST classify a dispatch exception into TRANSITORIO, DIFERIBLE, PERMANENTE, or OBSOLETO
using a pure function with no database, HTTP, or clock access (ADR 0019), independent of any
specific handler shape (Drive/Sheets not yet built).

#### Scenario: Exception classified without side effects
- GIVEN a dispatch exception raised by `despachar_evento`
- WHEN the classification core evaluates it
- THEN it returns one of TRANSITORIO/DIFERIBLE/PERMANENTE/OBSOLETO
- AND no database write, HTTP call, or clock read occurs during classification

### Requirement: Error and retry persistence
The system MUST persist every classified failure on the `fact.OutboxEventIntegracion` row for that
event (`Estado`, `Intentos`, `UltimoError`, `Clasificacion`, `ProximoIntentoEn`), not on
`fact.ProcesamientoError` — corrected during design (D1, ratified): outbox dispatch has no
`ProcesamientoId` to satisfy that table's `NOT NULL` FK, and deriving one would require reading
`fact.Factura`, which `fact_worker` is denied (ADR 0003). `ProcesamientoError`/`ProcesamientoIntentos`
remain scoped to the ingestion path (#6) only.

#### Scenario: Failed dispatch persisted
- GIVEN a dispatch attempt fails
- WHEN classification completes
- THEN the `fact.OutboxEventIntegracion` row for that event is updated with the resulting
  classification, error message, and next-retry time
- AND its `Intentos` column reflects the attempt count

### Requirement: TRANSITORIO retry with growing backoff
The system MUST retry a TRANSITORIO failure up to 3 times with increasing wait between attempts.

#### Scenario: Transitorio retried and recovers
- GIVEN a dispatch fails and is classified TRANSITORIO
- WHEN it is retried with growing backoff
- THEN it succeeds within 3 attempts without further error persistence

#### Scenario: Transitorio exhausts retries
- GIVEN a TRANSITORIO failure is retried 3 times without success
- WHEN the 3rd retry fails
- THEN retries stop and the error is marked exhausted for notification (see
  `notificaciones-telegram-correo`)

### Requirement: DIFERIBLE producer for quota/rate-limit signals
The system MUST classify a 429/Retry-After response from the outbox path as DIFERIBLE and MUST
schedule its retry for the announced reopening window, not on a short fixed delay.

#### Scenario: Quota exceeded deferred to window
- GIVEN dispatch receives HTTP 429 with a `Retry-After` header
- WHEN classification runs
- THEN the error is classified DIFERIBLE
- AND the retry is scheduled at the window indicated by `Retry-After`, not within seconds

### Requirement: OBSOLETO errors excluded from active retry
The system MUST classify a failure caused by an event superseded by a later one as OBSOLETO and
MUST NOT schedule a retry for it.

#### Scenario: Superseded event not retried
- GIVEN a dispatch failure whose underlying event was superseded by a later event
- WHEN classification runs
- THEN the error is classified OBSOLETO
- AND no retry is scheduled
