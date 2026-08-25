# SPA Visual Detalle-Validación Specification

## Purpose

Apply the design tokens to the existing detalle-validación screen and its
sub-components (`detalle-page`, `factura-form`, `asiento-lineas`,
`visor-documento`, `conflicto-banner`) so users can distinguish, at a glance,
blocking vs. informational alerts and 412 vs. 422 states — without altering
functional logic.

## Requirements

### Requirement: Detalle-validación components consume design tokens

The system MUST style `detalle-page`, `factura-form`, `asiento-lineas`,
`visor-documento`, and `conflicto-banner` exclusively through the global
tokens defined in `spa-design-tokens` — no component redefines its own color
or typography values.

#### Scenario: Detalle screen renders in both themes

- GIVEN the SPA is toggled between light and dark theme
- WHEN the detalle-validación screen is rendered
- THEN every in-scope component resolves colors and typography from the
  active theme's token values

### Requirement: Blocking indicators use the strong alert token

The system MUST render blocking conditions — duplicate invoice, unregistered
provider P00000 — with the strong alert token (solid background/border) and
MUST prevent the `Validar` action's affordance from appearing available while
unresolved.

#### Scenario: Duplicate invoice indicator

- GIVEN a factura is flagged as a duplicate
- WHEN `factura-form` renders the indicator
- THEN it uses the strong alert token with solid background and border

#### Scenario: Unregistered provider P00000 indicator

- GIVEN a factura references provider P00000
- WHEN `factura-form` renders the indicator
- THEN it uses the strong alert token with solid background and border

### Requirement: Informational indicators use the subtle alert token

The system MUST render informational conditions — OCR fields not extracted,
unverified affectation pending confirmation — with the subtle alert token
(thin border or icon, no solid background).

#### Scenario: OCR field not extracted

- GIVEN a field was not extracted by OCR
- WHEN `factura-form` renders the field
- THEN it uses the subtle alert token, without a solid background

#### Scenario: Unverified affectation

- GIVEN an afectación is pending confirmation
- WHEN `asiento-lineas` renders the line
- THEN it uses the subtle alert token, without a solid background

### Requirement: 412 vs. 422 visually distinct

The system MUST render `conflicto-banner` (412 edit conflict) using the
dedicated conflict token/icon, and inline validation errors in
`factura-form`/`asiento-lineas` (422) using the dedicated validation-error
token/icon. The two MUST NOT share color or icon.

#### Scenario: Edit conflict banner

- GIVEN a save attempt returns HTTP 412
- WHEN `conflicto-banner` renders
- THEN it uses the conflict token and its own icon, distinct from validation
  error styling

#### Scenario: Validation rule error

- GIVEN a save attempt returns HTTP 422
- WHEN the affected field/line renders its error
- THEN it uses the validation-error token and its own icon, distinct from
  conflict styling

### Requirement: Correction history panel collapsed by default

The system MUST render the correction-history panel next to the asiento as an
expandable/collapsible panel that is collapsed by default on initial render.

#### Scenario: Initial render

- GIVEN `asiento-lineas` renders with correction history available
- WHEN the component first mounts
- THEN the history panel is in the collapsed state

#### Scenario: User expands history

- GIVEN the history panel is collapsed
- WHEN the user activates the panel's expand control
- THEN the panel shows the list of corrections (field, previous value, new
  value, timestamp) without navigating away from the screen

### Requirement: History panel has a defined empty-state visual treatment

The system MUST style an explicit empty state for the correction-history
panel (no strong-alert or error tokens) for when `asiento-lineas` has no
history entries to show, distinct from the populated-list treatment.

#### Scenario: Empty history renders without alert styling

- GIVEN `asiento-lineas` expands the history panel with zero entries
- WHEN the empty state renders
- THEN it uses neutral/informational styling, not the strong or subtle alert
  tokens

### Requirement: Tabular alignment for amounts and dates

The system MUST render monetary amounts and dates in `factura-form` and
`asiento-lineas` using the tabular-nums token so values align vertically in
columns.

#### Scenario: Asiento amount column

- GIVEN `asiento-lineas` renders multiple lines with monetary amounts
- WHEN the amounts are displayed
- THEN their digits align vertically using the tabular-nums token

### Requirement: Component CSS budget compliance

Each of `detalle-page`, `factura-form`, `asiento-lineas`, `visor-documento`,
and `conflicto-banner` MUST keep its component stylesheet under Angular's
`anyComponentStyle` budget thresholds defined in `angular.json`.

#### Scenario: Build-time budget check per component

- GIVEN any in-scope component's stylesheet
- WHEN the Angular build runs budget checks
- THEN it does not trigger the `anyComponentStyle` warning or error threshold
