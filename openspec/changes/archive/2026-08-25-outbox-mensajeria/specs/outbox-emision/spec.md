# Outbox Emision Specification

## Purpose

Close the producer-side gap in ADR 0004's five-event catalog: `FACTURA_VALIDADA` and
`DOCUMENTACION_ACTUALIZADA` are already emitted but with bare-scalar, non-self-sufficient payloads;
`ASIENTO_CORREGIDO`, `ASIENTO_ANULADO`, and `FACTURA_CORREGIDA` are not emitted at all, across their
four emission points (`ValidarInternoAsync` reconfirm, `AnularAsync`, `PatchAsync`,
`ConfirmarAfectacionAsync`). Without them, factura reconfirmation, `/anular`, and invoice correction
produce no downstream fact, and Drive/Sheets (items #15/#16) have nothing to react to. This
capability also retrofits the two pre-existing events onto the same full-snapshot envelope so all
five catalog events are self-sufficient and consistent before any consumer handler is built.

## Requirements

### Requirement: ASIENTO_CORREGIDO on reconfirmation of a reopened asiento

The system MUST emit one `ASIENTO_CORREGIDO` `OutboxEvent` row, in the same SQL Server transaction
as the reconfirmation write, whenever `ServicioDeFacturas.ValidarInternoAsync` reconfirms a factura
whose `NumeroAsiento` already exists. `ReabrirAsync` is the only route back to BORRADOR, so a
pre-existing `NumeroAsiento` at reconfirm time is definitionally "reconfirmed after reopening"
(ADR 0006); the event is emitted at the reconfirmation commit, not at `ReabrirAsync` itself, because
reopening alone produces no new asiento state to announce.
(Previously: emission point was `ServicioDeAsientos.ReabrirAsync`; corrected to
`ServicioDeFacturas.ValidarInternoAsync`'s reconfirmation commit.)

#### Scenario: Reconfirming a reopened asiento emits the event

- GIVEN a factura previously reopened via `ReabrirAsync`, whose asiento's `NumeroAsiento` already
  exists from the prior validation
- WHEN `ValidarInternoAsync`'s reconfirmation transaction commits
- THEN one `OutboxEvent` row with `Tipo='ASIENTO_CORREGIDO'` is inserted in the same transaction
- AND its `Secuencia` is strictly greater than any prior event for that asiento's agregado

#### Scenario: ReabrirAsync alone emits no ASIENTO_CORREGIDO

- GIVEN a validated factura eligible for reopening
- WHEN `ReabrirAsync` commits, returning the factura to BORRADOR
- THEN no `ASIENTO_CORREGIDO` event is emitted by that transaction

#### Scenario: Failed reconfirmation emits no event

- GIVEN a reconfirmation attempt via `ValidarInternoAsync` that fails validation and rolls back
- WHEN the transaction is rolled back
- THEN no `ASIENTO_CORREGIDO` row exists for that attempt

### Requirement: ASIENTO_ANULADO on annulment

The system MUST emit one `ASIENTO_ANULADO` `OutboxEvent` row, in the same transaction as the
annulment write, whenever `ServicioDeAsientos.AnularAsync` completes.

#### Scenario: Annulling an asiento emits the event

- GIVEN a confirmed asiento eligible for annulment
- WHEN `AnularAsync` commits
- THEN one `OutboxEvent` row with `Tipo='ASIENTO_ANULADO'` is inserted in the same transaction

### Requirement: FACTURA_CORREGIDA on any accepted update to a validated invoice

The system MUST emit exactly one `FACTURA_CORREGIDA` `OutboxEvent` row per accepted correction
transaction on an already-validated `Factura`, regardless of which field(s) changed — not limited
to fiscal or monetary fields. There are two independent emission points —
`ServicioDeFacturas.PatchAsync` and `ServicioDeFacturas.ConfirmarAfectacionAsync` (`:384`) — and
each MUST emit at most one `FACTURA_CORREGIDA` row per correction transaction it commits; the two
points MUST NOT both fire for the same transaction.
(Previously: only `PatchAsync` was a confirmed emission point; `ConfirmarAfectacionAsync` add for
an `AfectacionMixta` change on a VALIDADA factura.)

#### Scenario: ConfirmarAfectacionAsync changing AfectacionMixta emits the event

- GIVEN a validated `Factura`
- WHEN `ConfirmarAfectacionAsync` accepts a change to `AfectacionMixta`
- THEN one `OutboxEvent` row with `Tipo='FACTURA_CORREGIDA'` is inserted in the same transaction
  as the confirmation write

#### Scenario: A single correction transaction never emits FACTURA_CORREGIDA twice

- GIVEN a validated `Factura`
- WHEN a single accepted correction transaction touches logic reachable from both `PatchAsync` and
  `ConfirmarAfectacionAsync` code paths (e.g. within the same request/transaction scope)
- THEN exactly one `OutboxEvent` row with `Tipo='FACTURA_CORREGIDA'` is inserted for that
  transaction, never one per emission point touched

#### Scenario: Correcting a non-fiscal field still emits the event

- GIVEN a validated `Factura`
- WHEN an accepted update changes a field that is neither fiscal nor monetary
- THEN one `OutboxEvent` row with `Tipo='FACTURA_CORREGIDA'` is inserted in the same transaction
  as the update

#### Scenario: Correcting multiple fields in one transaction emits exactly one event

- GIVEN a validated `Factura`
- WHEN a single accepted correction transaction updates several fields at once, including the
  fiscal identity `(RUC, tipo, número)`
- THEN exactly one `OutboxEvent` row with `Tipo='FACTURA_CORREGIDA'` is inserted for that
  transaction, never one per changed field

#### Scenario: Update to a non-validated invoice emits no FACTURA_CORREGIDA

- GIVEN a `Factura` not yet validated
- WHEN it is updated
- THEN no `FACTURA_CORREGIDA` event is emitted for that update

### Requirement: Self-sufficient payload for all five catalog events

Each of the five catalog events — `FACTURA_VALIDADA`, `DOCUMENTACION_ACTUALIZADA`,
`ASIENTO_CORREGIDO`, `ASIENTO_ANULADO`, and `FACTURA_CORREGIDA` — MUST carry a payload built by the
shared `PayloadOutbox` serializer containing the full current state the destination must reflect,
never a delta, per ADR 0004. `FACTURA_VALIDADA` and `DOCUMENTACION_ACTUALIZADA` MUST use the same
envelope shape as the other three, not the bare scalars (`numeroAsiento`, `NombreArchivo`,
`adjuntoId.ToString()`) they send today.
(Previously: this requirement applied only to the three new events —
`ASIENTO_CORREGIDO`/`ASIENTO_ANULADO`/`FACTURA_CORREGIDA`; extended to cover the pre-existing
`FACTURA_VALIDADA`/`DOCUMENTACION_ACTUALIZADA` payload retrofit.)

#### Scenario: FACTURA_CORREGIDA payload is self-sufficient

- GIVEN a `FACTURA_CORREGIDA` event emitted for a correction transaction
- WHEN its payload is inspected
- THEN it contains the complete post-correction invoice state needed to sync the destination,
  including `FacturaId` as sync key, not only the fields that changed

#### Scenario: ASIENTO_ANULADO payload is self-sufficient

- GIVEN an `ASIENTO_ANULADO` event
- WHEN its payload is inspected
- THEN it contains the full state needed for the destination to reflect the annulment without
  reading any other event

#### Scenario: FACTURA_VALIDADA payload carries the full snapshot, not a bare scalar

- GIVEN a `Factura` transitioning to VALIDADA
- WHEN its `FACTURA_VALIDADA` event payload is inspected
- THEN it contains the complete invoice state needed to sync the destination, including
  `FacturaId` and `numeroAsiento`, not `numeroAsiento` alone

#### Scenario: DOCUMENTACION_ACTUALIZADA payload carries the full snapshot, not a bare scalar

- GIVEN an attachment accepted for a `Factura`
- WHEN its `DOCUMENTACION_ACTUALIZADA` event payload is inspected
- THEN it contains the complete documentation state needed to sync the destination, including the
  owning `FacturaId`, not `NombreArchivo`/`adjuntoId.ToString()` alone

### Requirement: ValidarInternoAsync writes Factura.Estado = 'VALIDADA'

The system MUST write `fact.Factura.Estado = 'VALIDADA'` inside `ValidarInternoAsync`'s transaction,
via a new state-only `IUnidadDeTrabajo` member (not the ETag-based PATCH path, since
`ValidarInternoAsync` holds no ETag). Closes item #11's gap: previously no path set `Factura.Estado`,
leaving the `Estado == VALIDADA` guards on `DOCUMENTACION_ACTUALIZADA`/`FACTURA_CORREGIDA` always
false outside hand-set test state. When the state-only member reports the source row is in a
non-transitionable state (`NoTransicionable` — today only `DESCARTADA`), the system MUST reject the
`validar` request with 409 and roll back the transaction: no `Estado` write, no asiento confirmation
kept, and no `FACTURA_VALIDADA` event emitted (design D10, Open Question 5 resolved by the owner).
(Previously: three scenarios covering the happy path, rollback-on-failure, and downstream guards;
adds the `NoTransicionable`/409 rejection scenario.)

#### Scenario: Validating a factura writes its Estado to VALIDADA

- GIVEN a factura in BORRADOR with a valid asiento ready for confirmation
- WHEN `ValidarInternoAsync` commits
- THEN `fact.Factura.Estado` reads `'VALIDADA'`, written via the new state-only member, no ETag used

#### Scenario: Failed validation leaves Estado unchanged

- GIVEN a validation attempt that fails and rolls back
- WHEN the transaction is rolled back
- THEN `fact.Factura.Estado` is unchanged

#### Scenario: Downstream Estado == VALIDADA guards fire on genuinely validated state

- GIVEN a factura whose `Estado` was set to `VALIDADA` by `ValidarInternoAsync` (not hand-set), then
  an accepted attachment or correction
- WHEN that follow-up transaction commits
- THEN the corresponding `Estado == VALIDADA` guard passes and `DOCUMENTACION_ACTUALIZADA` or
  `FACTURA_CORREGIDA` is emitted, without test-only state manipulation

#### Scenario: Validating a discarded factura is rejected and rolled back

- GIVEN a factura whose `Estado` is `DESCARTADA`
- WHEN `ValidarInternoAsync` is invoked and the state-only member returns `NoTransicionable`
- THEN the endpoint responds 409 with `application/problem+json` (`CasoConflicto.FacturaDescartada`)
- AND the transaction commits nothing: `fact.Factura.Estado` remains `DESCARTADA`, no asiento is left
  `CONFIRMADO` from this attempt, and no `FACTURA_VALIDADA` `OutboxEvent` row is inserted

### Requirement: Same port, same transaction, monotonic sequence

The system MUST emit the three new event types, across their four emission points
(`ValidarInternoAsync` reconfirm, `AnularAsync`, `PatchAsync`, `ConfirmarAfectacionAsync`), through
the existing `IUnidadDeTrabajo.EmitirOutboxAsync` port, inside the same transaction as the domain
write, using `SeqOutbox` for a monotonic `Secuencia` per agregado — matching the `FACTURA_VALIDADA`
pattern.
(Previously: "three new emission points"; corrected to four, since `FACTURA_CORREGIDA` has two.)

#### Scenario: Emission reuses the existing port

- GIVEN any of the four emission points for the three new event types
- WHEN the domain operation writes its event
- THEN it calls `IUnidadDeTrabajo.EmitirOutboxAsync` with no new abstraction introduced
