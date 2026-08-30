# SPA Shell Navigation Specification

## Purpose

Provide the macOS sidebar navigation shell in `ShellLayout` as a faithful replica
of the design handoff (`handoff/Gestor de Facturas.dc.html`). Per the canvas the
authenticated shell has NO top header bar: product identity, the theme control
("Apariencia" card) and a profile row all live in the sidebar, and the routed
screen owns its own page title. This capability is layout/CSS plus a small
client-side state service; it introduces no backend contact and no new routes.

> Supersedes the earlier ratified scope ("exactly two routed destinations, no
> placeholder entries, theme `<select>` in a top header"). Reopened on user
> instruction to match the canvas; destinations without a route render as inert
> entries rather than being omitted.

## Requirements

### Requirement: Sidebar mirrors the handoff navigation

The sidebar MUST render the navigation in two groups. Primary group, in order:
`Bandeja principal`, `Registro de compra`, `Proveedores`, `Plan contable`,
`Tipo de cambio`. Utility group, in order: `Errores y notificaciones`,
`Sincronización`, `Configuración` — eight destinations total.

The following five destinations resolve to a route and MUST render as `<a>`
links: `Bandeja principal` (`/bandeja`), `Proveedores` (`/catalogos/proveedores`),
`Plan contable` (`/catalogos/plan-contable`), `Tipo de cambio`
(`/catalogos/tipo-cambio`), and `Configuración` (`/configuracion`).

The remaining three destinations — `Registro de compra`, `Errores y
notificaciones`, `Sincronización` — MUST render as inert entries: not a link,
`aria-disabled="true"`, `title` "Disponible próximamente" — so the navigation
matches the canvas without offering a dead click.

(Previously: seven destinations; only `Bandeja principal` and `Configuración`
were links and the other five, including `Proveedores` and `Plan contable`, were
inert. `Tipo de cambio` did not exist.)

#### Scenario: Handoff destinations appear in order

- GIVEN the SPA is loaded with a valid session
- WHEN `ShellLayout` renders the sidebar
- THEN the eight destinations appear in the order above, with a divider between
  the primary and utility groups

#### Scenario: Routed destinations are links

- GIVEN the sidebar renders
- WHEN its entries are inspected
- THEN `Bandeja principal`, `Proveedores`, `Plan contable`, `Tipo de cambio`, and
  `Configuración` are `<a>` links to their routes, and `Registro de compra`,
  `Errores y notificaciones`, and `Sincronización` are `aria-disabled` non-link entries

#### Scenario: Active destination is indicated

- GIVEN the current URL matches a routed destination (e.g. `/catalogos/proveedores`)
- WHEN the sidebar renders
- THEN that entry carries an active/selected visual state (solid accent fill) and
  the others do not

#### Scenario: sidebar.spec.ts asserts the new exact list

- GIVEN `sidebar.spec.ts`
- WHEN it runs
- THEN it asserts the exact ordered eight-entry list above, that exactly the five
  routed destinations are `<a>` links, and that eight hand-built glyphs render

### Requirement: Navigation is grouped with a single hairline divider

The sidebar MUST present the primary group and the utility group separated by
exactly one hairline divider that uses the existing translucent hairline border
token.

#### Scenario: Primary and utility groups separated by one divider

- GIVEN the sidebar renders in expanded state
- WHEN its structure is inspected
- THEN the primary group sits above one hairline divider and the utility group
  (ending in "Configuración") sits below it

### Requirement: Theme control and profile live in the sidebar

The shell MUST NOT render a top header bar. At the foot of the sidebar an
"Apariencia" card MUST hold the sol/luna theme toggle button (see
`spa-theme-toggle`), above a profile row that shows the current session user
(falling back to "Asistente contable" when the session name is not yet known).
Product identity (logo badge + "Facturas de Compra") sits at the top of the
sidebar next to the collapse toggle. The sidebar MUST fill the viewport height so
the "Apariencia" card and profile row sit flush with the bottom of the screen.

#### Scenario: Theme toggle in the Apariencia card

- GIVEN the authenticated shell renders
- WHEN the sidebar foot is inspected
- THEN an "Apariencia" card contains a `<button data-testid="toggle-tema">`, and
  there is no `.app-shell__header` element and no theme `<select>`

#### Scenario: Profile row reflects the session

- GIVEN a session reporting the user "María Contadora"
- WHEN the sidebar renders
- THEN the profile row shows "María Contadora"; with no session name it shows
  "Asistente contable"

### Requirement: Sidebar is expanded by default and collapsible

The sidebar MUST default to the expanded width of 216px on first load for a
viewer with no stored preference. A collapse toggle MUST switch between 216px
(expanded) and 60px (collapsed). In the collapsed state each destination MUST
remain operable (glyph visible, label hidden or shown on hover/title).

#### Scenario: First load with no stored preference

- GIVEN a viewer whose browser has no `fact.sidebar` value
- WHEN the shell renders
- THEN the sidebar is 216px wide (expanded)

#### Scenario: Toggle collapses and expands

- GIVEN the sidebar is expanded
- WHEN the viewer activates the collapse toggle
- THEN the sidebar becomes 60px wide and both destinations remain reachable
- WHEN the viewer activates the toggle again
- THEN the sidebar returns to 216px

### Requirement: Collapsed state persists per viewer in localStorage

The collapsed/expanded preference MUST be persisted in `localStorage` under the
key `fact.sidebar`, following the same client-only pattern as the theme
preference service. No API call and no server-side storage may be used. The
stored preference MUST be re-applied on reload for that browser only.

#### Scenario: Preference survives reload

- GIVEN the viewer collapsed the sidebar
- WHEN the viewer reloads the SPA in the same browser
- THEN the sidebar renders collapsed, read from `localStorage` key `fact.sidebar`

#### Scenario: Corrupt or absent value falls back to expanded

- GIVEN `fact.sidebar` holds an unrecognised or missing value
- WHEN the shell renders
- THEN the sidebar defaults to expanded and does not throw

### Requirement: Navigation glyphs are hand-built from div elements

Each destination's icon MUST be constructed from `<div>`/`<span>` elements and
token-driven CSS only — no `<svg>`, no icon font, no external image — per
`DESIGN.md`. Pre-existing inline `<svg>` in other components is out of scope and
untouched.

#### Scenario: Glyph markup contains no svg or icon font

- GIVEN a sidebar destination renders its glyph
- WHEN its markup and styles are inspected
- THEN the glyph is composed of `<div>` elements styled via `var(--...)` tokens,
  with no `<svg>`, `@font-face` icon, or `<img>`

### Requirement: Sidebar text and affordances meet WCAG AA over the sidebar surface

All sidebar text, glyphs, active-state indication, and the divider MUST resolve
from existing global tokens over the existing `--fondo-sidebar` surface and MUST
meet WCAG AA contrast (>= 4.5:1 normal text, >= 3:1 large text / iconography) in
BOTH light and dark theme. If an existing text token fails AA over
`--fondo-sidebar`, a dedicated "text on sidebar" token MUST be added to
`spa-design-tokens` and its `contraste.spec.ts` array rather than hardcoding a
literal in the component.

#### Scenario: Sidebar label contrast passes AA in both themes

- GIVEN the sidebar renders its nav labels and glyphs over `--fondo-sidebar`
- WHEN contrast is measured in light and in dark theme
- THEN every text/glyph pair meets or exceeds the applicable WCAG AA threshold

### Requirement: Shell CSS stays layout-only and within budget

The `ShellLayout` and `Sidebar` stylesheets MUST contain layout/composition rules
only, no color/typography/hue literals (every color via `var(--...)`), and MUST
stay within the `angular.json` `anyComponentStyle` hard cap (8kB error). The
per-file warning threshold is 6kB. Adding the eighth hand-built nav glyph
(`Tipo de cambio`) MUST reuse the existing glyph/token pattern and MUST keep
`Sidebar` under the 6kB warning; if the eighth glyph would breach 6kB, the glyph
CSS MUST be refactored to shared rules rather than raising the budget again.

(Previously: text referred to "seven hand-built nav glyphs" and `Sidebar` ~5.3kB.)

#### Scenario: Shell stylesheet has no literals and fits the budget

- GIVEN the `ShellLayout` and `Sidebar` stylesheets after the eighth glyph is added
- WHEN their declarations are inspected
- THEN every color references a token and the production build reports no
  `anyComponentStyle` budget error, and `Sidebar` stays under the 6kB warning
