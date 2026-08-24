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
