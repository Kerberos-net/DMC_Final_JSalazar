# Spec: pantalla-detalle-validacion (BACKLOG #12)

New capability — Angular screen orchestrating document review, asiento editing,
partial save, and validation. Consumes existing #11 endpoints and the new #12
read endpoints. No new accounting rule is introduced here.

## Capability: `pantalla-detalle-validacion`

### Requirement: Side-by-side layout shows document and editable form

The screen MUST render the source document (left, ~42% static) and the factura/asiento edit form (right, flex:1) simultaneously, per DESIGN_BRIEF.md's "documento + formulario" pattern. The form MUST show the full factura header field set defined in "`factura-form` renders and binds the factura header field set", `TipoCambioVenta` (when applicable), and the asiento líneas.

When a factura has more than one associated INGESTA/MANUAL document, the viewer's default selected document MUST be the first document whose MIME type is in the inline allow-list (`application/pdf`, `image/png`, `image/jpeg`), falling back to the first document when none is renderable. Default selection MUST NOT be strictly the earliest-fecha document.
(Previously: with multiple documents "one is shown by default" with the default being `documentos[0]` ordered by fecha — an XML row could be selected and render as a download-only placeholder.)

#### Scenario: Opening a factura with a rendered document
- **Given** a factura with an associated document
- **When** the user opens the detail screen for that factura
- **Then** the document renders on the left and the factura/asiento data
  populates the form on the right, both loaded before the user can edit

#### Scenario: Factura with an XML and a PDF document
- **Given** a factura whose `GET /api/facturas/{id}/documentos` returns both an INGESTA XML row and an INGESTA PDF row
- **When** the screen loads
- **Then** the viewer offers a selector to switch between documents
- **And** the PDF is selected and rendered inline by default, not the XML

#### Scenario: Factura with only a non-renderable document
- **Given** a factura whose only document is an XML row (no renderable MIME)
- **When** the screen loads
- **Then** the viewer selects that row and shows the existing non-renderable placeholder / download affordance, unchanged

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
remain editable after a successful save. After a successful factura `PATCH`, the
screen MUST refetch the factura and adopt the returned `FacturaRespuesta` and
fresh `ETag`, so a server-recomputed `PosibleDuplicado` (and the recomputed
`BasePEN`/`IgvPEN`/`NetoPEN` projection) is reflected without a page reload.

#### Scenario: Saving progress on a draft
- **Given** unsaved edits to factura fields and/or asiento líneas
- **When** the user selects "Guardar avance"
- **Then** the pending changes persist, the factura/asiento estado is
  unchanged, and the form remains open for further edits

#### Scenario: Correcting the número clears a stale duplicate without reload
- **Given** a factura shown with `PosibleDuplicado: true`
- **When** the user corrects `numero` and selects "Guardar avance" and the server
  recomputes `PosibleDuplicado: false`
- **Then** after the refetch the duplicate indicator disappears and "Validar" is
  re-enabled, with no page reload

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

`factura-form` MUST derive its duplicate, unregistered-provider, OCR-missing, and unverified-afectación visual indicators from the corresponding `FacturaRespuesta` fields (`PosibleDuplicado`, `EsProveedorGenerico`, `CamposNoExtraidos`, `AfectacionMixta`), not from placeholder/mock values. The OCR-missing highlight MUST be applied per individual field via a `campoResaltado(campo)` lookup against `FacturaRespuesta.CamposNoExtraidos`, applying `.campo--resaltado` only to the fields named in that list — never as a single generic sentence and never to every field at once.

#### Scenario: Indicators match the persisted values

- GIVEN a `FacturaRespuesta` with `PosibleDuplicado: true`
- WHEN the screen renders the duplicate indicator
- THEN the indicator is shown, driven by that field's value

#### Scenario: Per-field OCR highlight

- GIVEN `CamposNoExtraidos` is `["numero","total"]`
- WHEN `factura-form` renders
- THEN only the `numero` and `total` inputs carry `.campo--resaltado`; other fields do not

#### Scenario: No missing fields

- GIVEN `CamposNoExtraidos` is `[]`
- WHEN `factura-form` renders
- THEN no field carries `.campo--resaltado`

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

> **State model note.** `fact.Factura` has no `BORRADOR` state
> (`CK_Factura_Estado` = `PENDIENTE_VALIDACION | VALIDADA | DESCARTADA`);
> `BORRADOR` is an `AsientoContable` state. Editability of the contable fields is
> gated on `FacturaRespuesta.estado == PENDIENTE_VALIDACION`.

The system MUST render `factura-form` as a two-column field grid with each label as secondary-style text above its input, exposing:

- **Editable, two-way bound (no backend change):** `monto`, `moneda`, `fechaEmision`, `proveedorCodigo` plus a proveedor picker control.
- **Editable, two-way bound (requires the `api-facturas` delta):** `tipoComprobante`, `numero`.
- **Editable while `estado == PENDIENTE_VALIDACION` (requires the `api-facturas` delta):** `base imponible`, `IGV`, `glosa`. When `estado` is `VALIDADA` (or `DESCARTADA`) these render as read-only, correctly formatted and tabular-aligned, and offer no edit control. `IGV` MUST be disabled (forced `0`, non-editable) when `tipoComprobante` is boleta (`03`) or `Afectacion` is `EXONERADA`/`INAFECTA` (REGLAS §5) — EXCEPT for a nota de crédito `07` con referencia interna, whose `IGV` stays editable (REGLAS §6 inheritance; a non-zero value is accepted by the API). `TC compra` stays read-only display.
- **Derived read-only display:** `mes` contable and `día` contable, derived from `AsientoContable.FechaContable`.

#### Scenario: Editable header fields are bound
- GIVEN a factura with `estado == PENDIENTE_VALIDACION`
- WHEN the user edits `monto`, `moneda`, `fechaEmision`, `proveedorCodigo`, `tipoComprobante`, or `numero` and saves via "Guardar avance"
- THEN the edited value is sent in the PATCH body and the UI reflects the response

#### Scenario: Contable fields are editable before validation
- GIVEN a factura with `estado == PENDIENTE_VALIDACION`
- WHEN the user edits `base imponible`, `IGV` or `glosa` and selects "Guardar avance"
- THEN the values are sent in the PATCH body and the form reflects the persisted response

#### Scenario: Contable fields are read-only once validated
- GIVEN a factura with `estado == VALIDADA`
- WHEN `factura-form` renders `base imponible`, `IGV`, `glosa`
- THEN each shows its formatted value with tabular alignment and offers no edit control

#### Scenario: IGV is locked for boleta / non-gravada
- GIVEN a non-NC factura where `tipoComprobante` is `03` or `Afectacion` is `EXONERADA`/`INAFECTA`
- WHEN `factura-form` renders while `estado == PENDIENTE_VALIDACION`
- THEN the `IGV` input is shown as `0` and disabled

#### Scenario: IGV stays editable for an NC 07 con referencia interna
- GIVEN a nota de crédito `07` con referencia interna, `estado == PENDIENTE_VALIDACION`
- WHEN `factura-form` renders
- THEN the `IGV` input is editable and a non-zero value can be saved via "Guardar avance"

#### Scenario: Derived period fields
- GIVEN `AsientoContable.FechaContable` is set
- WHEN `factura-form` renders `mes` and `día` contable
- THEN both display values derived from `FechaContable` and are not editable

### Requirement: Missing-tipo-de-cambio 409 on Validar is surfaced distinctly

When `validar` returns `409` because a required tipo de cambio is missing (the existing `SinTipoCambio` conflict, still raised for foreign-currency facturas and NC `07` con referencia externa; see the `api-facturas` narrowing), the screen MUST surface that specific 409 reason distinctly from a `412` conflict (per the existing "Validar ... surfaces its outcomes distinctly" requirement) and MUST keep local edits intact. "Guardar avance" MUST remain available in this state.

#### Scenario: Missing-rate 409 on Validar
- GIVEN a foreign-currency factura with no applicable tipo de cambio
- WHEN the user selects "Validar" and the API returns `409`
- THEN the screen shows the missing-tipo-de-cambio reason, does not offer reload-and-discard, and "Guardar avance" still works

#### Scenario: Edited base surfaces a newly-live §7 invariant 422
- GIVEN the user edited `base imponible` so the asiento's hand-built cargo líneas no longer sum to it
- WHEN the user selects "Validar" and the API returns `422` on the §7 "cargos = base imponible" invariant
- THEN the screen shows that invariant message distinctly from a `412`, keeps local edits, and the user can re-align the líneas and re-validate

### Requirement: Dedicated "tipo de cambio faltante" indicator

The system MUST render a dedicated indicator (distinct from the generic OCR-missing highlight) when the factura is foreign-currency and no compra tipo de cambio is available, showing that the displayed amount is `0.00`.

#### Scenario: TC-faltante indicator visible

- GIVEN a foreign-currency factura with no available compra tipo de cambio
- WHEN `factura-form` / the detalle screen renders
- THEN a dedicated TC-faltante indicator is shown stating the value shown is 0.00

### Requirement: The asiento is assumed present and its base/IGV drive the form

Because promotion seeds the `BORRADOR` asiento, `factura-form` MUST populate `base imponible`
and `IGV` from the asiento's `BasePEN`/`IgvPEN` projection on load, and the asiento líneas
section MUST render. When (exceptionally) no asiento exists — a foreign-currency factura
promoted with no tipo de cambio — the screen MUST show a "generar asiento" affordance that
calls `POST /api/facturas/{id}/abrir` once a rate is available, instead of blank amounts with
no recourse.

#### Scenario: Detalle load shows base/IGV from the seeded asiento — [new]
- GIVEN a factura promoted with a seeded asiento
- WHEN the detalle screen loads
- THEN `base imponible` and `IGV` are populated from the asiento projection and the líneas
  section lists the PRINCIPAL + DESTINO líneas
- (test: SPA unit)

#### Scenario: Factura with no asiento shows "generar asiento" — [new]
- GIVEN a foreign-currency factura promoted without an asiento
- WHEN the detalle screen loads
- THEN a "generar asiento" action is shown; invoking it (once a tipo de cambio exists) calls
  `abrir` and the screen refetches
- (test: SPA unit)

### Requirement: "Recomponer asiento" action on a BORRADOR asiento

The screen MUST offer a "recomponer asiento" action, visible only while the asiento is
`BORRADOR`, that calls `POST /api/facturas/{id}/recomponer`, then refetches the factura and
asiento. A confirmation dialog MUST warn that manual line edits will be replaced before the
request is sent. The action MUST NOT be shown for a `CONFIRMADO` asiento.

#### Scenario: Recomponer regenerates the líneas from the screen — [new]
- GIVEN a `BORRADOR` asiento with manually split líneas
- WHEN the user triggers "recomponer asiento" and confirms the warning
- THEN `recomponer` is called, and after the refetch the líneas show the fresh engine seed
- (test: SPA unit)

#### Scenario: Recomponer hidden on a CONFIRMADO asiento — [new]
- GIVEN a factura whose asiento is `CONFIRMADO`
- WHEN the detalle screen renders
- THEN the "recomponer asiento" action is not shown
- (test: SPA unit)

### Requirement: Cabecera↔detalle descuadre marker

The screen MUST show a read-only descuadre marker (reusing the read-only marker introduced by
BACKLOG #23) whenever the sum of the PRINCIPAL cargo líneas does not equal the header
`BasePEN` (gravada) / `NetoPEN` (otherwise). The marker MUST explain that `validar` is blocked
until the líneas are re-aligned or the asiento is recompuesto. It MUST clear once the líneas
match the header again.

#### Scenario: Marker appears after a base edit unbalances the líneas — [new]
- GIVEN a seeded asiento, and the user edits `base imponible` so the cargo líneas no longer
  sum to the new `BasePEN`, then selects "Guardar avance"
- WHEN the screen re-renders after the refetch
- THEN the cabecera↔detalle descuadre marker is shown and "Validar" surfaces the §7 `422`
  distinctly (per the existing outcome-distinction requirement)
- (test: SPA unit)

#### Scenario: Marker clears after recomponer — [new]
- GIVEN the descuadre marker is shown
- WHEN the user runs "recomponer asiento"
- THEN after the refetch the líneas match the header and the marker is gone
- (test: SPA unit)

#### Scenario: Manual line editing and #19 editable base/IGV/glosa still work (regression) — [new]
- GIVEN a `PENDIENTE_VALIDACION` factura with a `BORRADOR` asiento
- WHEN the user edits an asiento línea inline (#12) or edits `base imponible` / `IGV` /
  `glosa` (#19) and selects "Guardar avance"
- THEN the edits persist exactly as before this change; only the descuadre marker and
  "recomponer" affordance are added on top
- (test: SPA unit)
