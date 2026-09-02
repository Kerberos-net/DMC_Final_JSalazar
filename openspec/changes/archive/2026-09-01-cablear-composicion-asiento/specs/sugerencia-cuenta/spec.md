# Delta for Sugerencia de cuenta

Change: `cablear-composicion-asiento` (BACKLOG #24). The `sugerencia` module is wired into the
API DI container and consumed at asiento seed time to pick the default cargo account (owner
decision 3). The ranking cascade itself (REGLAS §3, ADR 0011) is unchanged; no suggestion
endpoints and no suggestion SPA UI are added.

## ADDED Requirements

### Requirement: `ServicioDeSugerencia` is registered in the API DI container

`Program.cs` MUST register `ServicioDeSugerencia` and its dependencies
(`ISugerenciaCuentaRepository`, `ResolverCandidatas`) so the asiento composition path can
resolve a cargo account. This is a compose-time consumer only — no new HTTP endpoint is
exposed.

#### Scenario: Suggestion service resolves during asiento seeding — [new]
- GIVEN the API is running and a factura is opened
- WHEN `abrir` / `recomponer` / promotion composes the asiento seed
- THEN `ServicioDeSugerencia` is invoked once for the `(proveedor, motivo)` pair and returns a
  ranked account (or none)
- (test: E2E / integration — DI resolution + one call)

### Requirement: The suggested account seeds the default cargo línea

At seed time the composition MUST ask `ServicioDeSugerencia` for the account for the factura's
`(proveedor, motivo)` pair (3-tier prefix cascade, REGLAS §3). When a suggestion resolves,
that account MUST be frozen onto the default cargo línea (with its `CtaReflejaCodigo` /
`CtaPuenteCodigo`). When no suggestion resolves, the cargo línea MUST be seeded as a "sin
cuenta" placeholder so REGLAS §7 Global-2 blocks `validar` until the user assigns one.

#### Scenario: Suggested account is on the default cargo línea — [new]
- GIVEN a `(proveedor, motivo)` pair for which the cascade resolves account `631111`
- WHEN the asiento is seeded
- THEN the default cargo línea carries `631111` and its frozen reflejo/puente accounts
- (test: E2E / integration)

#### Scenario: No suggestion → placeholder línea → validar blocked — [new]
- GIVEN a `(proveedor, motivo)` pair with no resolvable candidate account
- WHEN the asiento is seeded and later `validar` is called
- THEN the cargo línea is a "sin cuenta" placeholder and `validar` is rejected by §7 Global-2
  until the user assigns an account
- (test: E2E / integration)
