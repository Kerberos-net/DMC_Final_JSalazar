# SPA Theme Toggle Specification

## Purpose

Provide a minimal, accessible mechanism to select between light and dark
theme in the SPA, so the dark theme defined by `spa-design-tokens` is usable
in practice, not only declared.

## Requirements

### Requirement: Theme toggle control is accessible from the authenticated screens

The system MUST expose a control, reachable from every authenticated
in-scope screen (bandeja, detalle-validación, configuración), that lets the
user switch between light and dark theme. The `/login` screen is out of
scope for this control (see `spa-visual-login`: login renders without the
app shell chrome).

#### Scenario: Toggle reachable from detalle-validación

- GIVEN the user is on the detalle-validación screen
- WHEN they look for the theme control
- THEN it is visible or reachable without leaving the screen

#### Scenario: Login screen does not show the theme control

- GIVEN the user is on the `/login` screen
- WHEN the DOM is inspected
- THEN there is no theme `<select>` and no app shell header

### Requirement: Default theme resolution without explicit choice

The system MUST default to the operating system's `prefers-color-scheme`
when the user has not made an explicit theme choice.

#### Scenario: First visit follows OS preference

- GIVEN a user with no stored theme preference and an OS set to dark mode
- WHEN they load the SPA
- THEN the SPA renders in dark theme

### Requirement: Theme choice persists client-side only

The system MUST persist an explicit user theme choice in the browser
(`localStorage` or equivalent client storage) and MUST NOT introduce a new
backend API surface for theme preference.

#### Scenario: Explicit choice survives reload

- GIVEN a user explicitly selects dark theme
- WHEN they reload the SPA
- THEN it renders in dark theme without a new selection

#### Scenario: Missing preference falls back cleanly

- GIVEN a browser with no stored theme preference (e.g. storage cleared)
- WHEN the SPA loads
- THEN it falls back to `prefers-color-scheme`, or light theme if that is
  also unavailable, without error

### Requirement: Theme control is a sol/luna toggle button in the sidebar

> Supersedes the earlier "native `<select>` in the app shell header" rule (item
> #18, user decision 5). Reopened on user instruction to match the design canvas
> (`handoff/Gestor de Facturas.dc.html`): no shell header, and an icon toggle.

The theme control MUST be a `<button>` (`data-testid="toggle-tema"`) inside the
sidebar "Apariencia" card at the foot of `ShellLayout`'s sidebar. Activating it
MUST flip the effective theme between light and dark and persist that as an
explicit `claro` / `oscuro` choice (`localStorage` key `fact.tema`, no backend).
The button MUST show a sun glyph while the effective theme is light and a moon
glyph while it is dark, and carry an `aria-label` naming the target theme
("Cambiar a tema oscuro" / "Cambiar a tema claro"). No theme `<select>` remains
anywhere in the app.

The pre-bootstrap default is unchanged: a viewer with no stored `fact.tema`
value still follows the OS `prefers-color-scheme` ("sistema" resolution in
`TemaService`); the toggle simply records the first explicit choice.

#### Scenario: Theme control element type

- GIVEN the sidebar "Apariencia" card renders
- WHEN its DOM is inspected
- THEN the control is a `<button data-testid="toggle-tema">` and there is no
  theme `<select>` in the document

#### Scenario: Toggle flips and persists

- GIVEN the effective theme is light
- WHEN the viewer activates the toggle
- THEN `data-tema` on `<html>` becomes `oscuro` and `localStorage.fact.tema` is
  `oscuro`; activating it again returns both to `claro`
