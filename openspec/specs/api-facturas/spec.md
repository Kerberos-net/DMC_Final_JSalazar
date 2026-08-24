# Spec: api-facturas (BACKLOG #11)

New capability — REST edit + command orchestration for the Factura aggregate, per ADR 0008.

## Capability: `api-facturas`

### Requirement: `PATCH /api/facturas/{id}` edits a draft with optimistic concurrency

The endpoint MUST require an `If-Match` header carrying the `ETag` (Base64 of `fact.Factura.Version`
rowversion) from the prior `GET`. The update MUST be a compare-and-swap (`WHERE Version = @expected`).

#### Scenario: Matching If-Match succeeds
- **Given** a factura with current `Version` V1 and `ETag` E1
- **When** `PATCH /api/facturas/{id}` is sent with `If-Match: E1` and a valid body
- **Then** the row updates, `Version` advances, and the response returns the new `ETag`

#### Scenario: Stale If-Match is rejected with 412
- **Given** a factura whose `Version` changed since the client's last `GET`
- **When** `PATCH /api/facturas/{id}` is sent with the stale `ETag`
- **Then** the response is `412 Precondition Failed`, `application/problem+json`, and zero rows update

#### Scenario: Correction to an already-validated factura writes AuditoriaCorreccion
- **Given** a `PATCH` edits a field of a factura whose asiento is `CONFIRMADO`
- **When** the edit succeeds
- **Then** a `fact.AuditoriaCorreccion` row is written with `EntidadTipo=FACTURA`, `Accion=CORRECCION`
  (or `TRASLADO_PERIODO` when the edit changes the accounting period)

### Requirement: `POST /api/facturas/{id}/abrir` creates the draft asiento if absent

Per ADR 0006, `abrir` MUST create the `BORRADOR` asiento when none exists for the factura, and MUST
NOT write `AuditoriaCorreccion` (not in the `Accion` enum).

#### Scenario: Opening a factura with no asiento
- **Given** a factura with no non-`ANULADO` asiento
- **When** `POST /api/facturas/{id}/abrir` is called
- **Then** a new `BORRADOR` asiento is created and no `AuditoriaCorreccion` row is written

#### Scenario: Opening a factura with no tipo de cambio (foreign currency)
- **Given** the factura is in foreign currency and `fact.TipoCambio` has no row for the fecha de
  emisión
- **When** `POST /api/facturas/{id}/abrir` is called
- **Then** the response is `409 Conflict` naming the missing tipo de cambio as the blocker

### Requirement: `POST /api/facturas/{id}/validar` confirms the factura and asiento (=ADR 0006 confirmar)

`validar` MUST assign the correlativo transactionally via `UPDATE fact.CorrelativoAsiento WITH
(UPDLOCK)`, freeze the asiento (`CONFIRMADO`), and evaluate every `InvarianteContable`. It MUST NOT
skip a correlativo number even when the surrounding transaction rolls back.

#### Scenario: Successful validar assigns correlativo and confirms
- **Given** a `BORRADOR` asiento passing all `InvarianteContable` checks, no open 409 condition
- **When** `POST /api/facturas/{id}/validar` is called
- **Then** `fact.CorrelativoAsiento.Ultimo` increments once, the asiento becomes `CONFIRMADO`, and the
  factura state advances per ADR 0006

#### Scenario: Invariant violation returns 422 problem+json
- **Given** the asiento's `SUM(Debe) != SUM(Haber)` (`InvarianteContable.SumaDebeIgualHaber`)
- **When** `POST /api/facturas/{id}/validar` is called
- **Then** the response is `422 Unprocessable Content` with `type` identifying
  `asiento-descuadrado`, `title`, `status: 422`, `detail`, and the conflicting amounts as extension
  members (matches ADR 0008's example body)

#### Scenario: Business-state 409 cases block validar
- **Given** one of: unresolved duplicate, comprobante emitted on a Sunday, foreign-currency factura
  with no tipo de cambio, unresolved `P00000 (Varios)` proveedor, `FechaContable` before the fecha
  de corte, NC referencing an internal factura that is missing/unvalidated/discarded/with vigente
  asiento anulado, or an unconfirmed afectación (PDF-only comprobante)
- **When** `POST /api/facturas/{id}/validar` is called
- **Then** the response is `409 Conflict` with `application/problem+json` naming the specific case

#### Scenario: Rollback does not reuse a skipped correlativo number
- **Given** the correlativo `UPDLOCK` increment succeeded but a later step in the same transaction
  fails
- **When** the transaction rolls back
- **Then** the next successful `validar` receives the next sequential number — the failed attempt's
  number is never reassigned nor silently reused

### Requirement: `POST /api/facturas/{id}/descartar` discards a factura without writing audit

`descartar` is not in the `Accion` enum; it MUST NOT write `AuditoriaCorreccion`.

#### Scenario: Discarding a duplicate factura
- **Given** a factura flagged as an unresolved duplicate
- **When** `POST /api/facturas/{id}/descartar` is called
- **Then** the factura is marked discarded and no `AuditoriaCorreccion` row is written

### Requirement: Adjuntos stay editable after validar and notify Drive archiving

`POST .../adjuntos` MUST emit `DOCUMENTACION_ACTUALIZADA` when the factura is already validated;
`DELETE .../adjuntos/{adjuntoId}` MUST additionally write `AuditoriaCorreccion` with
`Accion=ELIMINACION_ADJUNTO`.

#### Scenario: Adding an adjunto to a validated factura
- **Given** a factura already `CONFIRMADO`
- **When** `POST /api/facturas/{id}/adjuntos` is called with a new file
- **Then** the adjunto is stored and an `OutboxEvent` of type `DOCUMENTACION_ACTUALIZADA` is written
  in the same transaction; no `AuditoriaCorreccion` row is written for the addition

#### Scenario: Deleting an adjunto from a validated factura
- **Given** a factura already `CONFIRMADO` with an existing adjunto
- **When** `DELETE /api/facturas/{id}/adjuntos/{adjuntoId}` is called
- **Then** the adjunto is removed, `DOCUMENTACION_ACTUALIZADA` is emitted, and an
  `AuditoriaCorreccion` row is written with `Accion=ELIMINACION_ADJUNTO`

#### Scenario: Adjunto changes on a draft (not yet validated) factura
- **Given** a factura still `BORRADOR`
- **When** an adjunto is added or removed
- **Then** no `DOCUMENTACION_ACTUALIZADA` event and no `AuditoriaCorreccion` row are written

### Requirement: `FacturaRespuesta` projects existing duplicate/afectación/OCR indicators

`FacturaRespuesta` MUST additively expose `EsProveedorGenerico`,
`PosibleDuplicado`, `TieneCamposNoExtraidos`, and `AfectacionMixta` — the same
`fact.Factura` columns already read by `GET /api/bandeja`
(`SqlBandejaRepository.ListarAsync`). No existing `FacturaRespuesta` field
MUST change name, type, or meaning as part of this addition.

#### Scenario: Detail response includes the indicator fields

- GIVEN a factura persisted with `PosibleDuplicado=true` and
  `AfectacionMixta=null`
- WHEN its `FacturaRespuesta` is returned by the factura detail endpoint
- THEN the response includes `PosibleDuplicado: true` and
  `AfectacionMixta: null` alongside the existing fields

#### Scenario: Parity with the bandeja projection

- GIVEN a factura row with a given set of indicator column values
- WHEN the same factura is read via `GET /api/bandeja` and via the factura
  detail endpoint
- THEN `EsProveedorGenerico`, `PosibleDuplicado`, `TieneCamposNoExtraidos`,
  and `AfectacionMixta` resolve to the same values in both responses

#### Scenario: Existing consumers are unaffected

- GIVEN a client that only reads the `FacturaRespuesta` fields that existed
  before this change
- WHEN it parses the updated response
- THEN every previously existing field is present with its previous name,
  type, and value semantics unchanged
