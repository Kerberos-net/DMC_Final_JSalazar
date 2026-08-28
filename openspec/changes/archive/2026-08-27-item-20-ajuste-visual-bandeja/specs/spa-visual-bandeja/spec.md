# SPA Visual Bandeja Specification

## Purpose

Apply the design tokens and the #18 visual playbook to the five inbox/bandeja
components (`inbox-page`, `inbox-filter`, `inbox-list`, `panel-errores`,
`confirmar-reproceso`) so the screen matches the design handoff (§2 dashboard,
§5 panel de errores) using ONLY data already present on `BandejaItem`. This is
CSS and template structure only; the #13 functional behavior (bandeja query,
filter semantics, pagination, `chipsDe()` per-indicator logic, reprocesar
5-minute window, `inbox.service.ts`) is frozen and MUST NOT change.

## Requirements

### Requirement: Inbox components consume design tokens

The system MUST style all five inbox components exclusively through the global
tokens defined in `spa-design-tokens`. Component CSS MUST be layout/composition
only, live outside the cascade layers, contain no color/typography/hue
literals, and stay within `angular.json` `anyComponentStyle` budgets (4kB warn
/ 8kB error).

#### Scenario: Inbox renders in both themes from tokens

- GIVEN the SPA is toggled between light and dark theme
- WHEN the bandeja screen renders
- THEN every in-scope component resolves color and typography from the active
  theme's token values

#### Scenario: Component stylesheet has no literals

- GIVEN any inbox component stylesheet
- WHEN its declarations are inspected
- THEN every color references a `var(--...)` token and the file is within the
  4kB/8kB budget

### Requirement: inbox-page header and layout shell

The system MUST render a page header with the title "Bandeja principal" and a
subtitle that answers "¿qué necesito atender hoy?", plus a layout shell that
places the filter bar above the list and the list below.

#### Scenario: Header and shell present

- GIVEN the bandeja route is loaded
- WHEN `inbox-page` renders
- THEN a heading "Bandeja principal" and an orienting subtitle are shown, with
  `inbox-filter` above `inbox-list`

### Requirement: inbox-filter horizontal bar

The system MUST present the filter controls (estado, date range desde/hasta,
proveedor, orden) as a single horizontal bar rather than stacked `<label>`
blocks. The filter inputs and their bound signals MUST remain unchanged from
#13 — this requirement is template/CSS layout only.

#### Scenario: Filters laid out horizontally

- GIVEN `inbox-filter` renders
- WHEN its layout is inspected
- THEN estado, desde, hasta, proveedor and orden controls sit on one
  horizontal row and each control's bound signal is identical to #13

### Requirement: inbox-list table with derived Estado chip column

The system MUST render `inbox-list` as a `.tabla` with a component-scoped
tabular-figures treatment on the date cell (NOT the global right-aligning
`.tabular-nums` primitive, which is wrong for a left-aligned date) and an
uppercase small-caps header row. The system MUST add one ADDITIVE derived
"Estado" chip column computed per row, FIRST MATCH WINS, in this precedence:

1. `estadoConsumo === 'DESCARTADO'` → `.chip--descartada` "Descartada"
   (unconditional — wins even when the row still carries error history)
2. else `errores.length > 0` → `.chip--error` "Error"
3. else `indicadores !== null && (esProveedorGenerico || posibleDuplicado)` →
   `.chip--alerta` "Alerta" (null-safe: `origen === 'INCIDENCIA'` rows have
   `indicadores: null`)
4. else `estadoConsumo === 'PROMOVIDO'` → `.chip--validada` "Validada"
5. else `estadoConsumo === 'PENDIENTE'` → `.chip--pendiente` "Pendiente"

`DESCARTADO` ranks FIRST, not last: a discarded row is a terminal lifecycle
fact and MUST show "Descartada" even with error history (user-ratified,
design D3). The derived Estado chip is presentation-only and is NOT a change
to #13's "indicators → chips" logic. The existing per-indicator `chipsDe()`
list column MUST remain unchanged.

#### Scenario: Discarded row with error history shows Descartada

- GIVEN a `BandejaItem` with `estadoConsumo === 'DESCARTADO'` and
  `errores.length > 0`
- WHEN the row renders
- THEN the Estado column shows a `.chip--descartada` labeled "Descartada"
  (the DESCARTADO branch wins over the error branch)

#### Scenario: Non-discarded row with errors shows Error chip

- GIVEN a `BandejaItem` with `estadoConsumo !== 'DESCARTADO'` and
  `errores.length > 0`
- WHEN the row renders
- THEN the Estado column shows a `.chip--error` labeled "Error" and the
  `chipsDe()` indicator column is unchanged

#### Scenario: Promoted row without errors or alert indicators

- GIVEN a `BandejaItem` with `estadoConsumo === 'PROMOVIDO'`, no errores, and
  no `esProveedorGenerico`/`posibleDuplicado`
- WHEN the row renders
- THEN the Estado column shows a `.chip--validada` labeled "Validada"

#### Scenario: Date cell uses component-scoped tabular figures

- GIVEN any inbox-list row
- WHEN the date cell renders
- THEN it carries a component-scoped tabular-figures class
  (`.inbox-list__fecha`, `font-variant-numeric: tabular-nums`, left-aligned),
  not the global `.tabular-nums` primitive

### Requirement: panel-errores restrained card treatment

The system MUST style `panel-errores` to transmit urgency without a full red
fill: error rows use `--estado-error-texto` and a hairline border, following
the #18 `.alerta--informativa` pattern (1px border, no fill), NOT
`.alerta--bloqueante`. The panel MUST render nothing when `errores` is empty.
Each row MUST show clasificación, mensaje, and `ocurridoEn` (date rendered with
a component-scoped tabular-figures treatment, not the global `.tabular-nums`
primitive).

#### Scenario: Errors present

- GIVEN a `BandejaItem` with one or more `errores`
- WHEN `panel-errores` renders
- THEN each row shows clasificación, mensaje and a tabular-figures `ocurridoEn`,
  using `--estado-error-texto` text and a hairline border with no solid red fill

#### Scenario: No errors

- GIVEN a `BandejaItem` with an empty `errores` array
- WHEN `panel-errores` renders
- THEN it produces no visible output

### Requirement: confirmar-reproceso modal card

The system MUST style `confirmar-reproceso` as a centered modal card using the
modal radius and elevation tokens plus a manually rendered backdrop element.
The `<dialog>` MUST stay non-modal (toggled via the `.open` attribute, no
`showModal()`, no `::backdrop`) per #13 D6. The two existing buttons MUST keep
their current behavior.

#### Scenario: Dialog open

- GIVEN the reprocesar confirmation is triggered
- WHEN `confirmar-reproceso` opens via the `.open` attribute
- THEN a centered card with modal radius/elevation tokens and a manual
  backdrop element renders, and both buttons retain their #13 behavior

### Requirement: New estado pairs pass WCAG AA in both themes

The system MUST ensure every new surface/text pair introduced for the bandeja
passes WCAG AA in both light and dark theme. `contraste.spec.ts` MUST assert
`--estado-error-texto` and `--estado-alerta-texto` over all four surface levels
and over their own `--estado-error-fondo` / `--estado-alerta-fondo`.

#### Scenario: Estado chip text contrast

- GIVEN `--estado-error-texto` and `--estado-alerta-texto`
- WHEN contrast is measured over each of the four surfaces and over their own
  `-fondo` token in each theme
- THEN every pair meets or exceeds WCAG AA (≥ 4.5:1 normal, ≥ 3:1 large/icon)

## Out of Scope

- Summary/counter cards (Pendientes/Validadas/Con error/Alertas) — BACKLOG #21
- Rich data columns (proveedor display name, monto, moneda, número, tipo,
  fecha de emisión, glosa, tipo de cambio, base imponible, IGV) — BACKLOG #21
- The standalone BACKLOG #17 "Errores y notificaciones" route and its
  `panel-errores` spec
- `configuracion/*`
- Any change to the #13 bandeja query, filter semantics, pagination,
  `chipsDe()` per-indicator logic, reprocesar 5-minute window, or
  `inbox.service.ts`
