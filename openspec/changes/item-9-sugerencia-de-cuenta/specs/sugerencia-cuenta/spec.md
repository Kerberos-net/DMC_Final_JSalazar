# Spec: Sugerencia de cuenta (BACKLOG #9)

New capability: pure 3-tier ranking cascade (REGLAS.md §3, ADR 0011 rev. 4) plus a thin
orchestration layer over `ISugerenciaCuentaRepository` and `ResolverCandidatas` (both item #3,
already built). See `proposal.md` Out of Scope.

## Non-Goals (explicit scope boundaries)

- **No historical seeding.** `SugerenciaCuenta` starts empty; no WU loads legacy data (ADR 0011
  rev. 4).
- **No `Veces`/`UltimoUso` increment.** Recording usage on confirmation is item #11's
  `RegistrarUsoAsync`, already built.
- **No UI.** Selection/confirmation screens are item #12.

---

## Capability: `sugerencia-cuenta`

### Requirement: The account-ranking cascade applies three tiers in strict order

Per REGLAS.md §3 and ADR 0011 rev. 4, given `SugerenciaCuenta` rows and the live candidate set
from `ResolverCandidatas` for a `(proveedor, motivo)` pair, the function MUST rank in this order
and stop at the first tier producing a result: (1) most-used account for `(proveedor, motivo)`;
(2) most-used account for `motivo` globally; (3) first candidate ordered by `CuentaCodigo` ASC.
It MUST be pure — no DB, HTTP, or clock dependency (ADR 0019).

#### Scenario: Tier 1 resolves when provider-specific history exists
- **Given** `SugerenciaCuenta` has rows for `(proveedor, motivo)` with distinct `Veces` counts
- **When** the cascade runs
- **Then** it returns the row with the highest `Veces` for that exact `(proveedor, motivo)` pair,
  without consulting tiers 2 or 3

#### Scenario: Falls to tier 2 when the provider has no history for this motivo
- **Given** no `SugerenciaCuenta` row exists for `(proveedor, motivo)`, but rows exist for
  `motivo` with other providers
- **When** the cascade runs
- **Then** it returns the account most used for `motivo` globally

#### Scenario: Falls to tier 3 without a tie
- **Given** no `SugerenciaCuenta` row exists for `motivo`, at any provider, and the motivo has
  multiple live candidates
- **When** the cascade runs
- **Then** it returns the candidate whose `CuentaCodigo` is lowest, ascending order

#### Scenario: Falls to tier 3 with a tie in candidate codes handled by ordering alone
- **Given** no history exists at tier 1 or tier 2, and two candidates would otherwise be
  indistinguishable except by code
- **When** the cascade runs
- **Then** it deterministically returns the same lowest `CuentaCodigo` candidate on every run,
  regardless of input row order

### Requirement: Tiers 1 and 2 break ties by `Veces` DESC, then `UltimoUso` DESC, then `CuentaCodigo` ASC

Per ADR 0011 rev. 4, when two or more accounts tie at tier 1 or tier 2 on `Veces`, the cascade
MUST prefer the more recently used one; if `UltimoUso` also ties, it MUST fall back to
`CuentaCodigo` ascending — the same deterministic rule as tier 3.

#### Scenario: Tie in `Veces` at tier 1 resolved by `UltimoUso`
- **Given** two accounts for `(proveedor, motivo)` share the highest `Veces` but differ in
  `UltimoUso`
- **When** the cascade runs
- **Then** it returns the account with the more recent `UltimoUso`

#### Scenario: Tie in `Veces` and `UltimoUso` at tier 2 resolved by `CuentaCodigo`
- **Given** two accounts for `motivo` globally share both the highest `Veces` and the same
  `UltimoUso`
- **When** the cascade runs
- **Then** it returns the account with the lowest `CuentaCodigo`

### Requirement: Suggestions are filtered against the live candidate set before ranking

Per the proposal, a `SugerenciaCuenta` row MUST only be considered if its `CuentaCodigo` is
currently present in `ResolverCandidatas`' output for that `motivo` — never merely "exists in
`dbo.CuentaContable`". A row for an account no longer a valid candidate MUST be excluded, even if
it has usage history.

#### Scenario: A historically-used account no longer in the motivo's live candidates is excluded
- **Given** `SugerenciaCuenta` has the top-ranked row at tier 1 for an account that has since
  moved out of the motivo's prefix range (chart-of-accounts change) or whose motivo was
  deactivated (`MotivoAtributo.Activo = false`)
- **When** the cascade runs, filtering history against the current `ResolverCandidatas` output
- **Then** that account is not suggested; ranking proceeds among the remaining valid candidates
  (or falls to the next tier if none remain)

### Requirement: A brand-new provider with no history falls through to tier 3

#### Scenario: First-ever invoice for a provider, motivo has prior global history
- **Given** no `SugerenciaCuenta` row exists for this `proveedor` under any motivo, but other
  providers have used this `motivo`
- **When** the cascade runs
- **Then** tier 1 yields nothing, tier 2 returns the globally most-used account for `motivo`

#### Scenario: First-ever invoice for a provider, motivo also has no history anywhere
- **Given** no `SugerenciaCuenta` row exists for this `proveedor` or for `motivo` at all
- **When** the cascade runs
- **Then** tiers 1 and 2 yield nothing and tier 3 returns the lowest `CuentaCodigo` among the
  motivo's live candidates

### Requirement: The same cascade mechanism suggests the motivo, indexed only by provider

Per REGLAS.md §3 ("El mismo mecanismo, considerando solo el proveedor, sugiere el motivo"), the
system MUST apply the identical two-tier structure (most-used motivo for this provider, else no
suggestion) keyed by `proveedor` alone — no third tier, since there is no catalog-wide "first
motivo" concept.

#### Scenario: Motivo suggestion returns the provider's most-used motivo
- **Given** `SugerenciaCuenta` rows exist for a provider across multiple motivos with distinct
  `Veces` totals
- **When** the motivo cascade runs for that provider
- **Then** it returns the motivo with the highest aggregate `Veces` for that provider

### Requirement: Every suggestion carries an auditable rationale as data

Per REGLAS.md §3 ("La sugerencia nunca decide sola... muestra el fundamento"), the result MUST
expose the rationale as a structured value (tier reached, `Veces`, total observations), not only
as a display string, so item #12 can render text like "usado 14 de 15 veces con este proveedor".

#### Scenario: Tier-1 result exposes usage counts
- **Given** a tier-1 suggestion where the winning account has `Veces = 14` out of 15 total
  observations for `(proveedor, motivo)`
- **When** the result is returned
- **Then** it includes the tier reached, the winning `Veces`, and the total, sufficient to render
  the rationale text without recomputation

### Requirement: An orchestration method exposes cuenta + motivo + fundamento for item #11

The application layer MUST expose a single method that calls `ISugerenciaCuentaRepository` and
`ResolverCandidatas`, assembles the pure cascade's input, and returns suggested cuenta, motivo,
and rationale together, ready for item #11 to invoke without re-implementing orchestration.

#### Scenario: Orchestration returns a combined result for a given provider and motivo
- **Given** a `proveedor` and `motivo` selected in a draft invoice
- **When** the orchestration method is invoked
- **Then** it returns the suggested `CuentaCodigo` (or none, if no live candidates exist) together
  with its rationale, in one call
