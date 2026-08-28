# Delta for SPA Design Tokens

## ADDED Requirements

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

## MODIFIED Requirements

### Requirement: WCAG AA contrast compliance per token pair

Each semantic color token, when used as text or icon color against its associated background, MUST meet WCAG AA contrast (≥ 4.5:1 for normal text, ≥ 3:1 for large text/status iconography) in both light and dark theme. `contraste.spec.ts` MUST assert every token pair — including the accent fill pair (white on `--accento`), the accent-text pair (`--accento-texto` on each surface it appears over), the estado "error" and estado "alerta" pairs (`--estado-error-texto` and `--estado-alerta-texto` over all four surface levels and over their own `--estado-error-fondo` / `--estado-alerta-fondo`), and every new surface/status pair — in BOTH themes.
(Previously: enumerated only the accent fill pair, accent-text pair, and "every new surface/status pair"; now the two new estado pairs are named explicitly in both themes.)

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
