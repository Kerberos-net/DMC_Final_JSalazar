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

The system MUST define two tokens derived from the same alert color family: a strong/blocking variant (solid background and border) and a subtle/informational variant (thin border or icon color only, no solid background).

**Ratified exception (user decision 1):** the accent hue (`--accento` blue) is deliberately reused across three roles — primary action, estado "Pendiente" chip, and the P00000 informational banner. This is a ratified, intentional departure from the brief rule "un estado = un color, nunca acento decorativo" and from first-pass decision D3 (pendiente = neutral gray). It MUST be preserved; a future reviewer MUST NOT "correct" it back to a single-role accent without re-ratification.

#### Scenario: Blocking token vs informational token share family

- GIVEN the strong alert token and the subtle alert token
- WHEN their hue values are compared
- THEN both derive from the same base alert hue, differing only in
  saturation/lightness/fill treatment

#### Scenario: Accent reuse across three roles is intentional

- GIVEN the primary button fill, the "Pendiente" chip, and the P00000 banner
- WHEN their base color is compared
- THEN all three resolve to the accent hue, and this is documented as a ratified exception rather than a defect

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

Each semantic color token, when used as text or icon color against its associated background, MUST meet WCAG AA contrast (≥ 4.5:1 for normal text, ≥ 3:1 for large text/status iconography) in both light and dark theme. `contraste.spec.ts` MUST assert every token pair — including the accent fill pair (white on `--accento`), the accent-text pair (`--accento-texto` on each surface it appears over), the estado "error" and estado "alerta" pairs (`--estado-error-texto` and `--estado-alerta-texto` over all four surface levels and over their own `--estado-error-fondo` / `--estado-alerta-fondo`), and every new surface/status pair — in BOTH themes.

#### Scenario: Alert text contrast passes AA

- GIVEN a text/background token pair used for a status or alert message
- WHEN its contrast ratio is measured in either theme
- THEN the ratio meets or exceeds the applicable WCAG AA threshold

#### Scenario: Accent-text pair passes AA in both themes

- GIVEN `--accento-texto` over each surface token it renders on
- WHEN contrast is measured in light and in dark theme
- THEN every pair meets or exceeds 4.5:1

#### Scenario: Accent button fill passes AA for its label

- GIVEN white button-label text over `--accento`
- WHEN contrast is measured in both themes
- THEN it meets or exceeds 4.5:1

#### Scenario: Estado error and alerta pairs pass AA in both themes

- GIVEN `--estado-error-texto` and `--estado-alerta-texto`
- WHEN contrast is measured over each of the four surface levels and over
  their own `--estado-error-fondo` / `--estado-alerta-fondo` in light and dark
- THEN every pair meets or exceeds the applicable WCAG AA threshold

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

### Requirement: Accent color family with distinct fill and text tokens

The system MUST define an accent color family: `--accento` for button fills / logo / active affordances (`#0071e3` light, `#0a84ff` dark), a companion `--accento-suave` soft tint, and a separate `--accento-texto` for links and accent-colored text (`#0a63c9` light, `#0a84ff` dark). Accent text MUST NOT reuse the button-fill value when that value fails AA as normal text.

#### Scenario: Link uses accent-text token, not fill token

- GIVEN a component renders an accent-colored text link
- WHEN it applies color
- THEN it references `--accento-texto`, not `--accento`

#### Scenario: Primary button uses fill token

- GIVEN a primary action button
- WHEN it renders its background
- THEN it references `--accento` with white foreground text

### Requirement: Four-level surface hierarchy

The system MUST define four distinct background surface tokens — app background, primary surface, secondary/raised surface, and sidebar surface — for both themes, with a warm dark palette (`#1c1c1e` / `#2c2c2e` / `#242426` / `#232326`).

#### Scenario: Secondary surface differs from primary

- GIVEN the primary surface token and the secondary surface token in a given theme
- WHEN their resolved values are compared
- THEN they are different colors

### Requirement: Elevation shadow scale

The system MUST define a graduated shadow scale from hairline to prominent (e.g. card, panel, modal/login), replacing the prior single near-flat shadow. Login/modal-level elevation MUST be visibly prominent (large blur/spread).

#### Scenario: Login card uses prominent elevation

- GIVEN the login card renders
- WHEN its box-shadow is inspected
- THEN it resolves to the prominent elevation token, not the hairline token

### Requirement: Radius scale 8 / 12 / 16 and pill

The system MUST define radius tokens for inputs/buttons (8px), inner cards (12px), modals/login (16px), and pills (20px), replacing the prior 6/8 pair.

#### Scenario: Pill radius token

- GIVEN a chip or cuadre pill renders
- WHEN it applies border-radius
- THEN it references the pill radius token (20px)

### Requirement: Segoe-first integer type scale

The system MUST define the typography scale as a Segoe-first font stack with rounded integer font sizes derived from the handoff's denser scale (no fractional/half-pixel sizes).

#### Scenario: No fractional font sizes

- GIVEN every typography size token
- WHEN their values are inspected
- THEN each is an integer pixel/rem value

### Requirement: Translucent hairline border tokens

The system MUST define border tokens as translucent hairlines (`rgba(0,0,0,0.08)` light, `rgba(255,255,255,0.09)` dark) rather than heavy solid borders.

#### Scenario: Border token is translucent

- GIVEN the hairline border token in either theme
- WHEN its value is inspected
- THEN it is an rgba value with alpha below 1

### Requirement: Estado "error" and "alerta" chip primitives

The system MUST define `.chip--error` and `.chip--alerta` primitives in
`@layer primitives`, following the same shape as the existing `.chip--validada`
primitive (padding, radius, font, token-driven text/background/border). They
MUST reference only estado tokens, never hue literals.

#### Scenario: Error chip primitive follows validada shape

- GIVEN `.chip--error` and `.chip--validada` in `@layer primitives`
- WHEN their declarations are compared
- THEN `.chip--error` uses the same box/typography shape and resolves its
  colors from `--estado-error-*` tokens

#### Scenario: Alerta chip primitive is token-driven

- GIVEN `.chip--alerta`
- WHEN its rules are inspected
- THEN every color declaration references a `var(--estado-alerta-*)` token,
  not a literal

### Requirement: Estado "error" and "alerta" token trios both themes

The system MUST define `--estado-error-{texto,fondo,borde}` and
`--estado-alerta-{texto,fondo,borde}` token trios in BOTH the light and dark
theme blocks. The `texto` value MUST be derived from the existing AA-tuned
`--error-ink` / `--alerta-ink` tokens respectively — no new hue literal may be
introduced (user decision 1, two-tier ramp). `paleta.spec.ts` theme-parity
MUST cover the new token names.

#### Scenario: Estado trios exist in both themes

- GIVEN the light theme and dark theme token blocks
- WHEN they are inspected for `--estado-error-*` and `--estado-alerta-*`
- THEN all six token names appear in each theme block

#### Scenario: Texto derives from existing inks, no new hue

- GIVEN `--estado-error-texto` and `--estado-alerta-texto`
- WHEN their resolved values are compared to `--error-ink` and `--alerta-ink`
- THEN each estado `texto` resolves from its corresponding existing ink and no
  new raw hex/RGB hue literal was added to `styles.css`

#### Scenario: Palette parity covers the new names

- GIVEN `paleta.spec.ts` theme-parity assertions
- WHEN they run
- THEN `--estado-error-{texto,fondo,borde}` and
  `--estado-alerta-{texto,fondo,borde}` are each asserted present in both themes
