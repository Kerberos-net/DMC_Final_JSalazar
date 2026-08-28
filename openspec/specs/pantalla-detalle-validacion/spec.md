# Spec: pantalla-detalle-validacion (BACKLOG #12)

New capability — Angular screen orchestrating document review, asiento editing,
partial save, and validation. Consumes existing #11 endpoints and the new #12
read endpoints. No new accounting rule is introduced here.

## Capability: `pantalla-detalle-validacion`

### Requirement: Side-by-side layout shows document and editable form

The screen MUST render the source document (left, ~42% static) and the factura/asiento edit form (right, flex:1) simultaneously, per DESIGN_BRIEF.md's "documento + formulario" pattern. The form MUST show the full factura header field set defined in "`factura-form` renders and binds the factura header field set", `TipoCambioVenta` (when applicable), and the asiento líneas.

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

`factura-form` MUST derive its duplicate, unregistered-provider, OCR-missing, and unverified-afectación visual indicators from the corresponding `FacturaRespuesta` fields (`PosibleDuplicado`, `EsProveedorGenerico`, `TieneCamposNoExtraidos`, `AfectacionMixta`), not from placeholder/mock values. The OCR-missing indicator MUST be applied per individual field (via `.campo--resaltado`), not as a single generic sentence.

#### Scenario: Indicators match the persisted values

- GIVEN a `FacturaRespuesta` with `PosibleDuplicado: true`
- WHEN the screen renders the duplicate indicator
- THEN the indicator is shown, driven by that field's value

#### Scenario: Per-field OCR highlight

- GIVEN `TieneCamposNoExtraidos` is true for specific fields
- WHEN `factura-form` renders
- THEN each non-extracted field individually carries `.campo--resaltado`

### Requirement: "Validar" is hard-blocked while P00000 or a duplicate is unresolved

The system MUST disable the "Validar" action (button rendered disabled, request never sent) whenever `FacturaRespuesta.EsProveedorGenerico` is true OR `FacturaRespuesta.PosibleDuplicado` is true. There MUST NOT be an acknowledgement-checkbox bypass (the handoff's ack pattern is not adopted — user decision 2).

#### Scenario: Duplicate disables Validar

- GIVEN `PosibleDuplicado` is true
- WHEN the detalle screen renders
- THEN "Validar" is disabled and cannot dispatch the validar request

#### Scenario: P00000 disables Validar

- GIVEN `EsProveedorGenerico` is true
- WHEN the detalle screen renders
- THEN "Validar" is disabled and cannot dispatch the validar request

#### Scenario: Both conditions resolved re-enables Validar

- GIVEN neither `PosibleDuplicado` nor `EsProveedorGenerico` is true and no other block applies
- WHEN the screen renders
- THEN "Validar" is enabled

### Requirement: `factura-form` renders and binds the factura header field set

The system MUST render `factura-form` as a two-column field grid with each label as secondary-style text above its input, exposing:

- **Editable, two-way bound (no backend change):** `monto`, `moneda`, `fechaEmision`, `proveedorCodigo` plus a proveedor picker control — all already present in the GET projection and PATCH contract.
- **Editable, two-way bound (requires the `api-facturas` delta):** `tipoComprobante`, `numero`.
- **Read-only display, correctly formatted and tabular-aligned:** `base imponible`, `IGV`, `TC compra`. Editability of these is explicitly deferred and MUST be raised against REGLAS.md as separate work.
- **Derived read-only display:** `mes` contable and `día` contable, derived from `AsientoContable.FechaContable`.
- `glosa` is NOT in scope for this change (no column exists; needs versioned SQL).

#### Scenario: Editable fields are bound

- GIVEN a factura with a `BORRADOR` asiento
- WHEN the user edits `monto`, `moneda`, `fechaEmision`, `proveedorCodigo`, `tipoComprobante`, or `numero` and saves via "Guardar avance"
- THEN the edited value is sent in the PATCH body and the UI reflects the response

#### Scenario: Read-only accounting fields are displayed, not editable

- GIVEN `factura-form` renders `base imponible`, `IGV`, and `TC compra`
- WHEN the user inspects those fields
- THEN each shows its formatted value with tabular alignment and offers no edit control

#### Scenario: Derived period fields

- GIVEN `AsientoContable.FechaContable` is set
- WHEN `factura-form` renders `mes` and `día` contable
- THEN both display values derived from `FechaContable` and are not editable

#### Scenario: glosa absent

- GIVEN `factura-form` renders
- WHEN the field list is inspected
- THEN there is no `glosa` field

### Requirement: Dedicated "tipo de cambio faltante" indicator

The system MUST render a dedicated indicator (distinct from the generic OCR-missing highlight) when the factura is foreign-currency and no compra tipo de cambio is available, showing that the displayed amount is `0.00`.

#### Scenario: TC-faltante indicator visible

- GIVEN a foreign-currency factura with no available compra tipo de cambio
- WHEN `factura-form` / the detalle screen renders
- THEN a dedicated TC-faltante indicator is shown stating the value shown is 0.00
