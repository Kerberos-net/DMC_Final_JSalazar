# Spec: Sugerencia de cuenta (BACKLOG #9)

New capability: pure 3-tier ranking cascade (REGLAS.md §3, ADR 0011 rev. 4) plus a thin
orchestration layer over `ISugerenciaCuentaRepository` and `ResolverCandidatas` (both item #3,
already built). See proposal.md Out of Scope.

## Non-Goals
- No historical seeding: `SugerenciaCuenta` starts empty (ADR 0011 rev. 4), no WU loads legacy data.
- No `Veces`/`UltimoUso` increment — item #11's `RegistrarUsoAsync`, already built.
- No UI — item #12.

## Capability: sugerencia-cuenta

### Requirement 1: The account-ranking cascade applies three tiers in strict order
**Given** `SugerenciaCuenta` rows and the live candidate set from `ResolverCandidatas` for a
`(proveedor, motivo)` pair  
**When** ranking is requested  
**Then** the function ranks in order: (1) most-used for `(proveedor, motivo)`; (2) most-used for `motivo` globally; (3) first candidate by `CuentaCodigo` ASC. Pure — no DB/HTTP/clock (ADR 0019).

#### Scenario: Tier 1 resolves with provider-specific history
When a provider has used a specific account for this motivo before, return the most-used account from that pair's history.

#### Scenario: Falls to tier 2, no provider-specific history
When a provider has no history for this motivo but the motivo has global history, return the most-used account across all providers for this motivo.

#### Scenario: Falls to tier 3, no tie
When there is no prior history for either the provider+motivo pair or the motivo globally, return the first candidate by `CuentaCodigo` ASC.

#### Scenario: Falls to tier 3, deterministic regardless of input row order
Tier 3 results are deterministic even if input rows are reordered — determined by `CuentaCodigo` ASC, not by caller order.

### Requirement 2: Tiers 1-2 break ties by Veces DESC, UltimoUso DESC, CuentaCodigo ASC
**Given** multiple accounts with equal `Veces` at tier 1 or 2  
**When** ranking  
**Then** use `UltimoUso DESC` to break the tie; if still tied, use `CuentaCodigo ASC`.

#### Scenario: Tier-1 tie in Veces resolved by UltimoUso DESC
When two accounts tie on Veces for the same (proveedor, motivo), the one with more recent UltimoUso wins.

#### Scenario: Tier-2 tie in Veces and UltimoUso resolved by CuentaCodigo ASC
When two accounts tie on both Veces and UltimoUso for the same motivo, the one with lower CuentaCodigo wins.

### Requirement 3: Suggestions are filtered against the live candidate set before ranking
**Given** `SugerenciaCuenta` rows and the live candidate set from `ResolverCandidatas`  
**When** ranking  
**Then** a `SugerenciaCuenta` row is only considered if its `CuentaCodigo` is currently in `ResolverCandidatas`' output for that motivo.

#### Scenario: Historically-used account no longer in live candidates is excluded
When an account was historically used but is no longer a valid candidate (chart-of-accounts change or motivo deactivated), it is excluded from ranking; the cascade proceeds among remaining candidates or falls to the next tier.

### Requirement 4: A brand-new provider with no history falls through to tier 3
**Given** a provider with no prior usage history  
**When** ranking for a motivo  
**Then** the cascade falls through tiers 1-2 to tier 3.

#### Scenario: First invoice for provider, motivo has prior global history
When a brand-new provider has no history for this motivo but the motivo itself has global history, use tier 2 (global motivo history).

#### Scenario: First invoice for provider, motivo has no history anywhere
When a brand-new provider and a motivo with zero history anywhere, use tier 3 (first candidate by CuentaCodigo ASC).

### Requirement 5: The same cascade mechanism suggests the motivo, indexed only by provider
**Given** a provider with usage history  
**When** motivo suggestion is requested  
**Then** return the provider's most-used motivo by aggregate `Veces`; no third tier (no catalog-wide default motivo).

#### Scenario: Motivo suggestion returns provider's most-used motivo
When a provider has used multiple motivos, return the one with the highest total Veces.

### Requirement 6: Every suggestion carries an auditable rationale as data
**Given** a ranking result  
**When** the result is returned  
**Then** it exposes tier reached, winning `Veces`, and total observations as structured data (not just display string) so item #12 can render explanations like "usado 14 de 15 veces con este proveedor".

#### Scenario: Tier-1 result exposes usage counts and rationale data
When tier 1 resolves, the result includes the winning account, the count of times used in the (proveedor, motivo) pair, and the total uses of that account across this motivo.

### Requirement 7: An orchestration method exposes cuenta + motivo + fundamento for item #11
**Given** a provider and optional pre-selected motivo  
**When** orchestration is invoked  
**Then** return suggested account, motivo, and rationale together in a single result so item #11 can pass them to the asiento confirmation.

#### Scenario: Combined result for given proveedor and explicit motivo
When a motivo is pre-selected by the user, return only the suggested account + fundamento for that motivo (no motivo suggestion needed).

#### Scenario: Combined result with motivo suggestion when none pre-selected
When no motivo is pre-selected, suggest both motivo and account, then return all three (motivo + account + fundamento) in the result.
