# SPA Design Tokens Specification

## Purpose

Define the global CSS design tokens in `styles.css` — color, typography,
spacing, tabular alignment — for light and dark theme, that every in-scope
component consumes. Tokens are the single source of visual truth: one
semantic meaning maps to exactly one token.

## Requirements

### Requirement: Semantic status/alert color tokens exist

The system MUST define semantic CSS custom properties for status/alert
colors, distinct from raw palette values, so components reference meaning
(e.g. `--color-alerta-fuerte`) rather than a specific hue.

#### Scenario: Alert token referenced by name

- GIVEN a component needs to signal a blocking condition
- WHEN it applies color
- THEN it references a semantic alert token, not a literal hex/RGB value

### Requirement: Two-tier alert emphasis from one semantic family

The system MUST define two tokens derived from the same alert color family:
a strong/blocking variant (solid background and border) and a subtle/
informational variant (thin border or icon color only, no solid background).

#### Scenario: Blocking token vs. informational token share family

- GIVEN the strong alert token and the subtle alert token
- WHEN their hue values are compared
- THEN both derive from the same base alert hue, differing only in
  saturation/lightness/fill treatment

### Requirement: Distinct tokens for 412 conflict and 422 validation error

The system MUST define a dedicated token for edit-conflict (412) state,
separate from the token for validation-rule error (422) state. The two MUST
NOT resolve to the same color value in either theme.

#### Scenario: Conflict and validation-error tokens differ

- GIVEN `--color-conflicto` and `--color-error-validacion` in a given theme
- WHEN their resolved values are compared
- THEN they are different colors

### Requirement: Complete light and dark token sets

The system MUST define every semantic color token for both light and dark
themes at initial implementation — no token exists in only one theme.

#### Scenario: Token parity across themes

- GIVEN the full list of semantic color token names
- WHEN the light theme and dark theme definitions are compared
- THEN every token name present in one theme is also present in the other

### Requirement: WCAG AA contrast compliance per token pair

Each semantic color token, when used as text or icon color against its
associated background, MUST meet WCAG AA contrast (≥ 4.5:1 for normal text,
≥ 3:1 for large text/status iconography) in both light and dark theme.

#### Scenario: Alert text contrast passes AA

- GIVEN a text/background token pair used for a status or alert message
- WHEN its contrast ratio is measured in either theme
- THEN the ratio meets or exceeds the applicable WCAG AA threshold

### Requirement: Typography, spacing, and tabular-nums tokens exist

The system MUST define shared typography (font family/size/weight scale),
spacing scale, and a tabular-nums token for numeric alignment, available
globally for component consumption.

#### Scenario: Component consumes shared typography token

- GIVEN any in-scope component renders body text
- WHEN it applies a font size
- THEN it references a shared typography token rather than a literal value

### Requirement: Shared token budget concentration

The system MUST concentrate shared visual rules (color, typography, spacing)
in `styles.css` tokens rather than redefining them per component, so
per-component CSS is limited to layout/composition.

#### Scenario: No component redefines a color literal

- GIVEN any in-scope component stylesheet
- WHEN its rules are inspected for color declarations
- THEN every color declaration references a `var(--...)` token, not a literal
  hex/RGB/HSL value
