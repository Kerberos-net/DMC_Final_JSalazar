# Spec: pantalla-detalle-validacion (BACKLOG #12)

New capability — Angular screen orchestrating document review, asiento editing,
partial save, and validation. Consumes existing #11 endpoints and the new #12
read endpoints. No new accounting rule is introduced here.

## Capability: `pantalla-detalle-validacion`

### Requirement: Side-by-side layout shows document and editable form

The screen MUST render the source document (left) and the factura/asiento
edit form (right) simultaneously, per DESIGN_BRIEF.md's "documento +
formulario" pattern. The form MUST show fields for factura header data,
`TipoCambioVenta` (when applicable), and the asiento líneas.

#### Scenario: Opening a factura with a rendered document
- **Given** a factura with an associated document
- **When** the user opens the detail screen for that factura
- **Then** the document renders on the left and the factura/asiento data
  populates the form on the right, both loaded before the user can edit

#### Scenario: Factura with multiple documents
- **Given** a factura with more than one associated document (recibido and/or
  manual)
- **When** the screen loads
- **Then** the viewer offers a way to switch between documents; one is shown
  by default

### Requirement: Asiento líneas are editable inline

The screen MUST support inline edit-in-place per línea, an explicit
"add línea" action, and delete-in-place with a confirmation dialog before
the delete request is sent. Línea order MUST NOT be treated as a persisted
invariant.

#### Scenario: Editing a línea inline
- **Given** an editable (non-`CONFIRMADO`) asiento with existing líneas
- **When** the user edits a field on one línea and confirms
- **Then** only that línea's edit is sent, and the UI reflects the response

#### Scenario: Deleting a línea requires confirmation
- **Given** an editable asiento with at least one línea
- **When** the user triggers delete on a línea
- **Then** a confirmation dialog MUST appear before the delete request is
  sent; canceling MUST leave the línea unchanged

### Requirement: "Guardar avance" persists edits without a state transition

The screen MUST offer a "Guardar avance" action that persists pending factura
and/or asiento línea edits via the existing PATCH/POST/DELETE endpoints,
without invoking `validar`. Both the document side and the form side MUST
remain editable after a successful save.

#### Scenario: Saving progress on a draft
- **Given** unsaved edits to factura fields and/or asiento líneas
- **When** the user selects "Guardar avance"
- **Then** the pending changes persist, the factura/asiento estado is
  unchanged, and the form remains open for further edits

### Requirement: "Validar" triggers the confirm transaction and surfaces its outcomes distinctly

The screen MUST offer a "Validar" action invoking `POST
/api/facturas/{id}/validar`. It MUST distinguish three outcome classes to the
user: success (factura `VALIDADA`, asiento `CONFIRMADO`), a `412` concurrency
conflict, and a business/invariant failure (`422` or `409`).

#### Scenario: Successful validation
- **Given** a factura/asiento passing all invariants with no open 409
  condition
- **When** the user selects "Validar"
- **Then** the screen reflects factura `VALIDADA` and asiento `CONFIRMADO`,
  and no further edit is offered without `reabrir`

#### Scenario: 412 conflict shows a reload banner, never auto-merges
- **Given** the factura or asiento was modified by another user since the
  screen last fetched it
- **When** "Validar" (or "Guardar avance") returns `412 Precondition Failed`
- **Then** the screen MUST show a blocking banner distinct from invariant
  errors, offering only a "recargar" action that refetches factura, asiento,
  and `If-Match` tokens, discarding local unsaved edits

#### Scenario: 422 invariant violation is shown distinctly from a conflict
- **Given** `validar` returns `422` (e.g. `asiento-descuadrado`, línea sin
  cuenta)
- **When** the response is received
- **Then** the screen shows the specific invariant message from the
  problem+json body, without offering the reload-and-discard action, and
  keeps local edits intact

#### Scenario: 409 business precondition is shown distinctly from a conflict
- **Given** `validar` returns `409` (e.g. missing tipo de cambio, duplicate,
  comprobante on Sunday)
- **When** the response is received
- **Then** the screen shows the specific 409 reason from the problem+json
  body, without offering the reload-and-discard action, and keeps local
  edits intact

### Requirement: Correction history panel is populated from real audit data

`asiento-lineas` MUST fetch entries from the correction-history read endpoint
(`auditoria-correccion-lectura-api`) and render them — field, previous value,
new value, timestamp — inside the collapsible history panel. The panel MUST
NOT be populated with placeholder/mock data.

#### Scenario: History panel shows real entries

- GIVEN a factura/asiento with existing `AuditoriaCorreccion` entries
- WHEN the user expands the history panel
- THEN the panel lists those entries with field, previous value, new value,
  and timestamp, sourced from the audit read endpoint response

#### Scenario: No history available

- GIVEN a factura/asiento with zero `AuditoriaCorreccion` entries
- WHEN the user expands the history panel
- THEN the panel indicates there is no correction history, without an error
  state

### Requirement: Afectación confirmation field appears when unverified

`factura-form` MUST show an explicit control letting the user confirm the
afectación when `FacturaRespuesta.AfectacionMixta` is `null` (unverified). A
confirmed action MUST invoke the existing `CONFIRMACION_AFECTACION` audit
action; it MUST NOT introduce a new accounting rule.

#### Scenario: Unverified afectación shows the confirmation control

- GIVEN a factura whose `AfectacionMixta` is `null`
- WHEN `factura-form` renders
- THEN the confirmation control is visible and actionable

#### Scenario: Verified afectación hides the confirmation control

- GIVEN a factura whose `AfectacionMixta` is not `null`
- WHEN `factura-form` renders
- THEN the confirmation control is not shown

#### Scenario: Confirming afectación writes the existing audit action

- GIVEN the confirmation control is visible
- WHEN the user confirms the afectación
- THEN the existing `CONFIRMACION_AFECTACION` audit action is invoked and no
  new accounting invariant is introduced by this interaction

### Requirement: Duplicate/afectación indicators reflect real persisted values

`factura-form` MUST derive its duplicate, unregistered-provider, OCR-missing,
and unverified-afectación visual indicators from the corresponding
`FacturaRespuesta` fields (`PosibleDuplicado`, `EsProveedorGenerico`,
`TieneCamposNoExtraidos`, `AfectacionMixta`), not from placeholder/mock
values.

#### Scenario: Indicators match the persisted values

- GIVEN a `FacturaRespuesta` with `PosibleDuplicado: true`
- WHEN `factura-form` renders the duplicate indicator
- THEN the indicator is shown, driven by that field's value
