# Delta for spa-design-tokens

Revises archived `2026-08-24-diseno-visual-spa-item-12`. Conforms tokens to the ratified design handoff.

## ADDED Requirements

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

## MODIFIED Requirements

### Requirement: WCAG AA contrast compliance per token pair

Each semantic color token, when used as text or icon color against its associated background, MUST meet WCAG AA contrast (≥ 4.5:1 for normal text, ≥ 3:1 for large text/status iconography) in both light and dark theme. `contraste.spec.ts` MUST assert every token pair — including the accent fill pair (white on `--accento`), the accent-text pair (`--accento-texto` on each surface it appears over), and every new surface/status pair — in BOTH themes.
(Previously: required AA per pair but predated the accent family, `--accento-texto`, and the four-level surface set.)

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

### Requirement: Two-tier alert emphasis from one semantic family

The system MUST define two tokens derived from the same alert color family: a strong/blocking variant (solid background and border) and a subtle/informational variant (thin border or icon color only, no solid background).

**Ratified exception (user decision 1):** the accent hue (`--accento` blue) is deliberately reused across three roles — primary action, estado "Pendiente" chip, and the P00000 informational banner. This is a ratified, intentional departure from the brief rule "un estado = un color, nunca acento decorativo" and from first-pass decision D3 (pendiente = neutral gray). It MUST be preserved; a future reviewer MUST NOT "correct" it back to a single-role accent without re-ratification.
(Previously: two-tier alert family with no documented accent-reuse exception.)

#### Scenario: Blocking token vs informational token share family

- GIVEN the strong alert token and the subtle alert token
- WHEN their hue values are compared
- THEN both derive from the same base alert hue, differing only in saturation/lightness/fill treatment

#### Scenario: Accent reuse across three roles is intentional

- GIVEN the primary button fill, the "Pendiente" chip, and the P00000 banner
- WHEN their base color is compared
- THEN all three resolve to the accent hue, and this is documented as a ratified exception rather than a defect
