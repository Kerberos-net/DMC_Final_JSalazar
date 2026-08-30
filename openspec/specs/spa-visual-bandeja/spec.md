# SPA Visual Bandeja Specification

## Purpose

Apply the design tokens and the #18 visual playbook to the five inbox/bandeja
components (`inbox-page`, `inbox-filter`, `inbox-list`, `panel-errores`,
`confirmar-reproceso`) so the screen matches the design handoff (§2 dashboard,
§5 panel de errores). Item #20 did this using ONLY data already present on
`BandejaItem`; item #21 delivers the rest of the §2 dashboard — the enriched
row columns and the summary cards — and therefore DELIBERATELY UNFREEZES the
#13 bandeja query and `inbox.service.ts` for those additions.

A later follow-up (user instruction, this spec revision) makes the estado
summary chips CLICKABLE filters and removes the estado `<select>` from
`inbox-filter`. This adds a new, additive query param `estadoDerivado` (see
`bandeja` spec) — the raw `estado`/`EstadoConsumo` param and its semantics are
untouched. Still frozen and MUST NOT change: date-range/proveedor/orden filter
semantics, pagination, the `chipsDe()` per-indicator list column, and the
reprocesar 5-minute window.

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
subtitle in the handoff §2 form "<today's date, es-PE long form> · ¿Qué necesito
atender hoy?" (e.g. "30 de agosto de 2026 · ¿Qué necesito atender hoy?"), plus a
layout shell that places the filter bar above the list and the list below.

Next to the "Bandeja de facturas" section title the system MUST show a row of
estado filter chips fed from the `resumen` aggregate — "Todos" (`total`),
"Pendiente" (`pendientes`), "Validada" (`validadas`), "Error" (`conError`),
"Alerta" (`alertas`), "Descartada" (`descartadas`) — each a `<button>` sized to
the handoff §2 pill, tinted at rest with its estado token set
(`--estado-{pendiente,validada,error,alerta,descartada}-*`, "Todos" stays
neutral) so colour identifies the bucket, count in a dimmed `<span>`; the active
chip is filled with the accent token. The "Descartada" chip restores the
`Descartado` option the original estado `<select>` carried. Clicking a chip sets
the `estadoDerivado` filter signal and re-issues `GET /api/bandeja?estadoDerivado=…`
(see `bandeja` spec); the SPA sends the param for EVERY value INCLUDING `TODOS`,
because the API's no-param default is the narrower non-terminal view and "Todos"
must show every eligible row. The active chip carries `aria-pressed="true"`. The
row renders nothing until the first load completes (`resumen` is null). This
REPLACES the old estado `<select>` in `inbox-filter`, which is removed —
`inbox-filter` now carries only proveedor search, date range and orden.

#### Scenario: Header and shell present

- GIVEN the bandeja route is loaded
- WHEN `inbox-page` renders
- THEN a heading "Bandeja principal" and a "<date> · ¿Qué necesito atender hoy?"
  subtitle are shown, with `inbox-filter` above `inbox-list`

#### Scenario: Estado filter chips beside the section title

- GIVEN the aggregate reports total=12, pendientes=4, validadas=3, conError=2, alertas=2, descartadas=1
- WHEN `inbox-page` renders after the first load
- THEN six chip buttons "Todos 12 / Pendiente 4 / Validada 3 / Error 2 / Alerta 2 / Descartada 1"
  sit next to "Bandeja de facturas"
- WHEN the user clicks "Error"
- THEN `GET /api/bandeja?estadoDerivado=ERROR` is issued, `pagina` resets, and the
  "Error" chip becomes the active one

### Requirement: inbox-filter horizontal bar

The system MUST present the filter controls (proveedor search, date range
desde/hasta, orden) as a single tight inline row sized to the handoff §2
dashboard: no visible `<label>` text (the search carries a placeholder, the
other controls an `aria-label`), ~12px control text, narrow fixed-width date
pickers with an em-dash between them, and the search field taking the remaining
width. Estado filtering is NOT in this bar — it moved to the estado chip row
next to the section title.

On first load the date range MUST be pre-filled: `desde` = the first day of the
current month, `hasta` = today (local `yyyy-MM-dd`), so the initial query is
already scoped to the current month. Clearing a date sets its signal to null.
The control bindings and emitted events are otherwise unchanged from #13.

#### Scenario: Date range defaults to the current month

- GIVEN today is 2026-08-30
- WHEN `inbox-page` first loads
- THEN the initial `GET /api/bandeja` carries `desde=2026-08-01` and `hasta=2026-08-30`

#### Scenario: Filters laid out horizontally

- GIVEN `inbox-filter` renders
- WHEN its layout is inspected
- THEN the proveedor search, desde, hasta and orden controls sit on one inline
  row with no `<label>` text (no estado control), each accessible-named via
  placeholder or `aria-label`, the date pickers at the narrow fixed width, and
  each control's emitted event identical to #13

### Requirement: inbox-list table with derived Estado chip column

The system MUST render `inbox-list` as a `.tabla` with the handoff §2 compras
columns in this order: `Recibido` (`creadoEn`), `F. emisión`, `Proveedor`
(display name), `Tipo` (comprobante), `Número`, `Monto` (with `Moneda`),
`Estado`, `Detalle`, `Indicadores`, `Acciones`. The `Recibido`, `F. emisión`
and `Monto` cells MUST carry a component-scoped tabular-figures class (NOT the
global right-aligning `.tabular-nums` primitive); `Monto` is right-aligned,
the two dates left-aligned. The header row is uppercase small-caps.

`Tipo` MUST be rendered from a CLIENT-SIDE display-name map of the API code:
`01` → "Factura", `03` → "Boleta", `07` → "Nota de crédito"; any other non-null
code renders verbatim. Every factura-only cell (`F. emisión`, `Proveedor`,
`Tipo`, `Número`, `Monto`) MUST render "—" when its value is null — this
covers both `origen === 'INCIDENCIA'` rows and a `FACTURA` row missing a field
(e.g. an unextracted `numero`).

The system MUST keep one ADDITIVE derived "Estado" chip column computed per row,
FIRST MATCH WINS, in this precedence:

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
- WHEN the `Recibido` and `F. emisión` cells render
- THEN each carries a component-scoped tabular-figures class
  (`.inbox-list__fecha`, `font-variant-numeric: tabular-nums`, left-aligned),
  not the global `.tabular-nums` primitive, and `Monto` carries
  `.inbox-list__monto` (tabular, right-aligned)

#### Scenario: FACTURA row renders the compras columns

- GIVEN a `BandejaItem` with `origen === 'FACTURA'` and enriched fields present
- WHEN the row renders
- THEN it shows proveedor name, the mapped comprobante tipo, número,
  monto + moneda, and fecha de emisión

#### Scenario: INCIDENCIA row shows em dashes for factura-only cells

- GIVEN a `BandejaItem` with `origen === 'INCIDENCIA'` (enriched fields null)
- WHEN the row renders
- THEN the `F. emisión`, `Proveedor`, `Tipo`, `Número` and `Monto` cells each
  render "—", and the derived Estado chip still renders

#### Scenario: Comprobante code is mapped client-side

- GIVEN a row whose API `tipoComprobante` is `01`
- WHEN the tipo cell renders
- THEN it displays "Factura" (the API response still carries the code `01`)

### Requirement: inbox-page global summary cards

The system MUST render four summary cards in `inbox-page` — "Pendientes",
"Validadas", "Con error", "Alertas" — fed from the bandeja estado aggregate
(`resumen` on the `GET /api/bandeja` envelope). The cards MUST show GLOBAL
totals: independent of the active filter signals and of the current page. The
cards MUST be display-only — they MUST NOT act as filter shortcuts and MUST
NOT mutate any filter signal on interaction. Card values map to aggregate
buckets: Pendientes ← `pendientes`, Validadas ← `validadas`, Con error ←
`conError`, Alertas ← `alertas`. `descartadas` and `total` MUST NOT be
rendered as summary cards (they do appear as counts on the "Descartada" /
"Todos" filter chips — a separate control). The strip MUST render nothing
before the first load completes
(`resumen` is null). The cards use compact vertical padding (a shorter card than
the #21 first pass) so the strip stays low-profile above the list.

#### Scenario: Cards show global totals regardless of filters

- GIVEN the aggregate reports pendientes=12, validadas=40, conError=3, alertas=5
- WHEN the user applies an `estado=PENDIENTE` filter and moves to page 2
- THEN the four cards still show 12 / 40 / 3 / 5

#### Scenario: Cards are not filter shortcuts

- GIVEN the summary cards are rendered
- WHEN the user activates the "Con error" card
- THEN no filter signal changes and the list query is not re-issued

#### Scenario: Validadas card is non-zero when promoted rows exist

- GIVEN promoted facturas exist that the default list view excludes
- WHEN `inbox-page` renders after the first load
- THEN the "Validadas" card shows the promoted count, not 0

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

- `glosa`, `tipo de cambio`, `base imponible`, `IGV` row columns — BACKLOG #19
  / future
- The standalone BACKLOG #17 "Errores y notificaciones" route and its
  `panel-errores` spec
- `configuracion/*`
- Any change to the raw `estado`/`EstadoConsumo` param, the date-range/proveedor
  filter semantics, pagination, the `chipsDe()` per-indicator list column, or the
  reprocesar 5-minute window (the additive `estadoDerivado` param and the chip
  filter UI ARE in scope for this revision)

(Item #21 moved the summary cards and the enriched row columns — proveedor
display name, monto, moneda, número, tipo, fecha de emisión — IN scope, and
with them the #13 bandeja query and `inbox.service.ts` are no longer frozen
for additive read fields.)
