# Spec: api-asientos (BACKLOG #11)

New capability — REST edit + command orchestration for the AsientoContable aggregate, per ADR 0008.

## Capability: `api-asientos`

### Requirement: `PATCH /api/asientos/{id}` edits with optimistic concurrency

Same compare-and-swap contract as `PATCH /api/facturas/{id}`: `If-Match` required against
`fact.AsientoContable.Version`, `412` on mismatch.

#### Scenario: Matching If-Match succeeds
- **Given** an asiento with current `ETag` E1
- **When** `PATCH /api/asientos/{id}` is sent with `If-Match: E1`
- **Then** the row updates and the response returns the new `ETag`

#### Scenario: Stale If-Match is rejected with 412
- **Given** an asiento whose `Version` changed since the client's last `GET`
- **When** `PATCH /api/asientos/{id}` is sent with the stale `ETag`
- **Then** the response is `412 Precondition Failed` and zero rows update

#### Scenario: Editing a CONFIRMADO asiento without reabrir first is rejected
- **Given** an asiento in state `CONFIRMADO`
- **When** `PATCH /api/asientos/{id}` or a líneas command is attempted directly
- **Then** the response is `409 Conflict` — "Asiento ya confirmado", instructing the caller to
  `reabrir` with motivo first

### Requirement: Líneas are addressed by `LineaId`, never by position

`POST /api/asientos/{id}/lineas` assigns a stable `LineaId`; `PATCH`/`DELETE
/api/asientos/{id}/lineas/{lineaId}` MUST target that id.

#### Scenario: Adding a línea assigns a stable id
- **Given** a `BORRADOR` asiento
- **When** `POST /api/asientos/{id}/lineas` adds a línea
- **Then** the response includes a `LineaId` that remains stable across reorder/other add/deletes

#### Scenario: Editing a línea by id survives prior deletions
- **Given** an asiento where an earlier línea (lower position) was deleted
- **When** `PATCH /api/asientos/{id}/lineas/{lineaId}` targets a remaining línea by its `LineaId`
- **Then** the correct línea updates regardless of its current position

#### Scenario: Manual redistribution of líneas writes AuditoriaCorreccion
- **Given** líneas are being manually reallocated on a factura whose asiento was already `CONFIRMADO`
  (post-reabrir)
- **When** the líneas command succeeds
- **Then** an `AuditoriaCorreccion` row is written with `EntidadTipo=ASIENTO`,
  `Accion=REPARTO_MANUAL`

### Requirement: `POST /api/asientos/{id}/reabrir` requires motivo and writes REAPERTURA audit

Per ADR 0008, `reabrir` MUST reject a missing `motivo` in the body with `400 Bad Request`, and MUST
only apply to a `CONFIRMADO` asiento.

#### Scenario: Reabrir with motivo on a confirmed asiento
- **Given** an asiento `CONFIRMADO`
- **When** `POST /api/asientos/{id}/reabrir` is called with `{ "motivo": "Corrección de cuenta" }`
- **Then** the asiento returns to an editable state, and an `AuditoriaCorreccion` row is written
  with `Accion=REAPERTURA` and `Motivo` populated

#### Scenario: Reabrir without motivo is rejected
- **Given** an asiento `CONFIRMADO`
- **When** `POST /api/asientos/{id}/reabrir` is called with no `motivo`
- **Then** the response is `400 Bad Request`, and no state change or audit row is written

#### Scenario: Reabrir a BORRADOR asiento is a 409
- **Given** an asiento still `BORRADOR`
- **When** `POST /api/asientos/{id}/reabrir` is called
- **Then** the response is `409 Conflict` — there is nothing confirmed to reopen

### Requirement: `POST /api/asientos/{id}/anular` terminally cancels and frees the factura

`ANULADO` is terminal (ADR 0006). Anulación MUST write `AuditoriaCorreccion` with
`Accion=ANULACION` and MUST NOT reactivate — no `reactivar` endpoint exists (retired in ADR 0008
rev.3).

#### Scenario: Anular a confirmed asiento
- **Given** an asiento `CONFIRMADO`
- **When** `POST /api/asientos/{id}/anular` is called
- **Then** the asiento becomes `ANULADO`, an `AuditoriaCorreccion` row is written with
  `Accion=ANULACION`, and the factura becomes eligible for a new asiento (per `UQ_Asiento_Vigente`
  allowing at most one non-`ANULADO` asiento per factura)

#### Scenario: Anular an already-ANULADO asiento is rejected
- **Given** an asiento already `ANULADO`
- **When** `POST /api/asientos/{id}/anular` is called again
- **Then** the response is `409 Conflict` — terminal state, no transition possible

### Requirement: InvarianteIncumplida maps to RFC 9457 422 problem+json

Each `InvarianteContable` member surfaced through `InvariantesDeConfirmacion` (Global 1
`SumaDebeIgualHaber`, Global 2 `LineaSinCuenta`, Global 5 `TipoLineaInconsistente`, `Principal`,
`Destino`) maps to a distinct `422` `type` URI with `sumaDebe`/`sumaHaber` or equivalent conflicting
amounts as extension members, mirroring ADR 0008's example body. `ProveedorVarios` and
`FechaAnteriorAlCorte`, though members of the same enum, are business-state preconditions checked
before composición runs and MUST map to `409` (see api-facturas 409 scenario), not `422` — the
exact precondition-vs-invariant split for these two is confirmed in `sdd-design`.

#### Scenario: Línea sin cuenta maps to a distinct 422 type
- **Given** `InvariantesDeConfirmacion` returns `InvarianteIncumplida(LineaSinCuenta, ...)`
- **When** the orchestration layer maps it to HTTP
- **Then** the response is `422` with a `type` distinct from `asiento-descuadrado`, identifying the
  offending línea

#### Scenario: Destino bloque incompleto maps to its own 422 type
- **Given** `InvariantesDeConfirmacion` returns `InvarianteIncumplida(Destino, ...)`
- **When** mapped to HTTP
- **Then** the response is `422` with a `type` specific to an incomplete bloque destino
