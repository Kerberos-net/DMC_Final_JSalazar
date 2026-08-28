# SPA Visual Login Specification

## Purpose

Apply the design tokens to the existing `login-page` component so it is
legible and usable with styling applied, without altering its authentication
logic. Item #18 additionally conforms the `login-page` structure to the
ratified design handoff.

## Requirements

### Requirement: Login page consumes design tokens

The system MUST style `login-page` exclusively through the global tokens defined in `spa-design-tokens` (color, typography, spacing, radius, elevation) — the component MUST NOT define its own color or font literals.

#### Scenario: Login renders in both themes

- GIVEN the SPA is toggled between light and dark theme
- WHEN `login-page` is rendered
- THEN its colors and typography resolve from the active theme's tokens

#### Scenario: No hardcoded color literals

- GIVEN the `login-page` stylesheet
- WHEN its color declarations are inspected
- THEN every declaration references a `var(--...)` token, not a literal value

### Requirement: Login error state uses the validation-error token

The system MUST render authentication error feedback (e.g. invalid credentials) as an inline message using the shared validation-error token family, not a one-off color and not the `.banner--error` block treatment.

#### Scenario: Invalid credentials feedback

- GIVEN a login attempt fails
- WHEN `login-page` renders the error message
- THEN it appears inline within the card using the shared validation-error token

### Requirement: Login card composition follows the handoff

The system MUST render `login-page` as a centered card containing, in order: a "GF" logo badge (accent background, white glyph), the title "Gestor de Facturas de Compra", a subtitle ("Inicia sesion para revisar y validar facturas"), the credential inputs, an inline error slot, the submit button, and a footer line ("Credenciales verificadas contra SQL Server"). The card MUST use the modal/login radius token and the prominent elevation token.

#### Scenario: Card renders all composition elements

- GIVEN the login screen loads
- WHEN its DOM is inspected
- THEN it contains the logo badge, title, subtitle, both inputs, an error slot, the submit button, and the footer line, in that vertical order

### Requirement: Inputs are placeholder-labeled

The system MUST present the usuario and contraseña inputs with placeholder-style labels and no separate visible `<label>` wrap. Each input MUST retain an accessible name (e.g. `aria-label`) equal to its field purpose.

#### Scenario: No visible label element

- GIVEN the login inputs render
- WHEN the DOM is inspected
- THEN there is no visible `<label>` text preceding each input, and each input exposes an accessible name

### Requirement: Full-width accent submit button

The system MUST render the "Ingresar" submit button as full card width using the `--accento` fill token with white label text.

#### Scenario: Submit button width and color

- GIVEN the login card renders
- WHEN the submit button is inspected
- THEN it spans the card width and its background resolves to `--accento`
