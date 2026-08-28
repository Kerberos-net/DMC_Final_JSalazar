# SPA Theme Toggle Specification

## Purpose

Provide a minimal, accessible mechanism to select between light and dark
theme in the SPA, so the dark theme defined by `spa-design-tokens` is usable
in practice, not only declared.

## Requirements

### Requirement: Theme toggle control is accessible from the SPA

The system MUST expose a control, reachable from every in-scope screen
(login, detalle-validación), that lets the user switch between light and
dark theme.

#### Scenario: Toggle reachable from detalle-validación

- GIVEN the user is on the detalle-validación screen
- WHEN they look for the theme control
- THEN it is visible or reachable without leaving the screen

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

### Requirement: Theme control remains a native `<select>` in this change

The system MUST keep the theme control as a native `<select>` element in the app shell header. The handoff's sun/moon icon toggle and any sidebar navigation redesign are explicitly out of scope for item #18 (user decision 5).

#### Scenario: Theme control element type

- GIVEN the app shell header renders the theme control
- WHEN its DOM is inspected
- THEN the control is a `<select>` element with options for light, dark, and system

#### Scenario: No sidebar theme toggle introduced

- GIVEN the SPA after this change
- WHEN the layout is inspected
- THEN no sidebar sun/moon "Apariencia" toggle is added
