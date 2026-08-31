# Delta for pantalla-detalle-validacion (BACKLOG #19)

Makes `base imponible`, `IGV` and `glosa` editable while the factura is
`PENDIENTE_VALIDACION`, drives the OCR highlight per individual field, and
refetches the factura after "Guardar avance" so a server-recomputed
`PosibleDuplicado` shows without a page reload.

> **State model note.** `fact.Factura` has no `BORRADOR` state
> (`CK_Factura_Estado` = `PENDIENTE_VALIDACION | VALIDADA | DESCARTADA`);
> `BORRADOR` is an `AsientoContable` state. Editability of the contable fields is
> gated on `FacturaRespuesta.estado == PENDIENTE_VALIDACION`.

## MODIFIED Requirements

### Requirement: `factura-form` renders and binds the factura header field set

(Previously: `base imponible` / `IGV` / `TC compra` were read-only display only, and `glosa` was explicitly out of scope with no field.)

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

### Requirement: Duplicate/afectación indicators reflect real persisted values

(Previously: the OCR-missing highlight was derived from the single `TieneCamposNoExtraidos` boolean.)

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

### Requirement: "Guardar avance" persists edits without a state transition

(Previously: no refetch after save.)

The screen MUST offer a "Guardar avance" action that persists pending factura and/or asiento línea edits via the existing PATCH/POST/DELETE endpoints, without invoking `validar`. Both the document side and the form side MUST remain editable after a successful save. After a successful factura `PATCH`, the screen MUST refetch the factura and adopt the returned `FacturaRespuesta` and fresh `ETag`, so a server-recomputed `PosibleDuplicado` (and the recomputed `BasePEN`/`IgvPEN`/`NetoPEN` projection) is reflected without a page reload.

#### Scenario: Saving progress on a draft
- GIVEN unsaved edits to factura fields and/or asiento líneas
- WHEN the user selects "Guardar avance"
- THEN the pending changes persist, the factura/asiento estado is unchanged, and the form remains open for further edits

#### Scenario: Correcting the número clears a stale duplicate without reload
- GIVEN a factura shown with `PosibleDuplicado: true`
- WHEN the user corrects `numero` and selects "Guardar avance" and the server recomputes `PosibleDuplicado: false`
- THEN after the refetch the duplicate indicator disappears and "Validar" is re-enabled, with no page reload

## ADDED Requirements

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

## Resolved decisions

- **NC `07` con referencia interna** — contable inputs (`base imponible`, `IGV`,
  `glosa`) are editable while `PENDIENTE_VALIDACION`, and `IGV` is NOT force-zeroed
  for this case (owner-confirmed, REGLAS §6).
