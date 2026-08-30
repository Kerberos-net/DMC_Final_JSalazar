# SPA Shell Navigation Specification

## Purpose

Restore the macOS sidebar navigation shell in `ShellLayout` as ratified by
`DESIGN.md`. The running SPA renders only a header with a bare `<router-outlet>`.
This capability adds a persistent left sidebar with grouped navigation, a
hairline divider, a collapse toggle, and per-viewer persistence — scoped ONLY to
destinations that have a real route today. It is layout/CSS plus a small
client-side state service; it introduces no backend contact and no new routes.

## Requirements

### Requirement: Sidebar lists only destinations with existing routes

The sidebar MUST render exactly two navigation destinations, each linking to a
route that already resolves in `app.routes`: `Bandeja` and `Configuración`. The
sidebar MUST NOT render dead, disabled, "coming soon", or placeholder entries for
screens that do not yet exist (Registro de compra, Proveedores, Plan contable,
Sincronización).

#### Scenario: Only routed destinations appear

- GIVEN the SPA is loaded with a valid session
- WHEN `ShellLayout` renders the sidebar
- THEN exactly two nav links are shown, "Bandeja" and "Configuración"
- AND each navigates to its existing route with no disabled or placeholder items

#### Scenario: Active destination is indicated

- GIVEN the current URL matches the bandeja route
- WHEN the sidebar renders
- THEN the "Bandeja" entry carries an active/selected visual state and the other does not

### Requirement: Navigation is grouped with a single hairline divider

The sidebar MUST present `Bandeja` in a primary group and `Configuración` in a
utility group placed after exactly one hairline divider that uses the existing
translucent hairline border token.

#### Scenario: Primary and utility groups separated by one divider

- GIVEN the sidebar renders in expanded state
- WHEN its structure is inspected
- THEN "Bandeja" sits in the primary group, one hairline divider follows, then
  "Configuración" sits in the utility group

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

Each destination's icon (inbox glyph for Bandeja, gear glyph for Configuración)
MUST be constructed from `<div>` elements and token-driven CSS only — no `<svg>`,
no icon font, no external image — per `DESIGN.md`. Pre-existing inline `<svg>` in
other components is out of scope and untouched.

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

The `ShellLayout` stylesheet MUST contain layout/composition rules only, no
color/typography/hue literals (every color via `var(--...)`), and MUST stay
within the `angular.json` `anyComponentStyle` budget (4kB warn / 8kB error).

#### Scenario: Shell stylesheet has no literals and fits the budget

- GIVEN the `ShellLayout` stylesheet
- WHEN its declarations are inspected
- THEN every color references a token and the file is within the 4kB budget
