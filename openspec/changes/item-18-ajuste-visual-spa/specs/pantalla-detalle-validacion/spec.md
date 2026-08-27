# Delta for pantalla-detalle-validacion

Revises BACKLOG #12 spec. Fills functional holes in `factura-form` and hard-gates "Validar".

## ADDED Requirements

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

## MODIFIED Requirements

### Requirement: Side-by-side layout shows document and editable form

The screen MUST render the source document (left, ~42% static) and the factura/asiento edit form (right, flex:1) simultaneously, per DESIGN_BRIEF.md's "documento + formulario" pattern. The form MUST show the full factura header field set defined in "`factura-form` renders and binds the factura header field set", `TipoCambioVenta` (when applicable), and the asiento líneas.
(Previously: form fields described only loosely as "factura header data"; only proveedor and RUC were actually editable.)

#### Scenario: Opening a factura with a rendered document

- **Given** a factura with an associated document
- **When** the user opens the detail screen for that factura
- **Then** the document renders on the left and the factura/asiento data populates the form on the right, both loaded before the user can edit

#### Scenario: Factura with multiple documents

- **Given** a factura with more than one associated document (recibido and/or manual)
- **When** the screen loads
- **Then** the viewer offers a way to switch between documents; one is shown by default

### Requirement: Duplicate/afectación indicators reflect real persisted values

`factura-form` MUST derive its duplicate, unregistered-provider, OCR-missing, and unverified-afectación visual indicators from the corresponding `FacturaRespuesta` fields (`PosibleDuplicado`, `EsProveedorGenerico`, `TieneCamposNoExtraidos`, `AfectacionMixta`), not from placeholder/mock values. The OCR-missing indicator MUST be applied per individual field (via `.campo--resaltado`), not as a single generic sentence.
(Previously: indicators bound to real values but OCR-missing shown only as one generic sentence, and duplicate/P00000 rendered inside `factura-form` rather than as banners above the split.)

#### Scenario: Indicators match the persisted values

- GIVEN a `FacturaRespuesta` with `PosibleDuplicado: true`
- WHEN the screen renders the duplicate indicator
- THEN the indicator is shown, driven by that field's value

#### Scenario: Per-field OCR highlight

- GIVEN `TieneCamposNoExtraidos` is true for specific fields
- WHEN `factura-form` renders
- THEN each non-extracted field individually carries `.campo--resaltado`
