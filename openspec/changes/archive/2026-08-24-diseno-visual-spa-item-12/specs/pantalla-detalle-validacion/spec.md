# Delta for pantalla-detalle-validacion

## ADDED Requirements

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
</content>
