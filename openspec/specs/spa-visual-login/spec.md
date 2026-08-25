# SPA Visual Login Specification

## Purpose

Apply the design tokens to the existing `login-page` component so it is
legible and usable with styling applied, without altering its authentication
logic.

## Requirements

### Requirement: Login page consumes design tokens

The system MUST style `login-page` exclusively through the global tokens
defined in `spa-design-tokens` (color, typography, spacing) — the component
MUST NOT define its own color or font literals.

#### Scenario: Login renders in both themes

- GIVEN the SPA is toggled between light and dark theme
- WHEN `login-page` is rendered
- THEN its colors and typography resolve from the active theme's tokens

#### Scenario: No hardcoded color literals

- GIVEN the `login-page` stylesheet
- WHEN its color declarations are inspected
- THEN every declaration references a `var(--...)` token, not a literal value

### Requirement: Login error state uses the validation-error token

The system MUST render authentication error feedback (e.g. invalid
credentials) using the same validation-error token family used elsewhere in
the SPA, not a one-off color.

#### Scenario: Invalid credentials feedback

- GIVEN a login attempt fails
- WHEN `login-page` renders the error message
- THEN it uses the shared validation-error token
