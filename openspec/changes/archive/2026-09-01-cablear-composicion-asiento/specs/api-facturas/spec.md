# Delta for api-facturas

Change: `cablear-composicion-asiento` (BACKLOG #24). `abrir` now seeds an engine-composed
asiento (owner decision 1 — Option 3 hybrid); `validar` runs REGLAS §7 against the persisted
seed + audited user deltas, never a re-composition; a new `recomponer` command regenerates
the seed. Scope: factura / boleta only. NC composition out of scope.

## MODIFIED Requirements

### Requirement: `POST /api/facturas/{id}/abrir` creates the draft asiento if absent

Per ADR 0006, `abrir` MUST create the `BORRADOR` asiento when none exists for the factura,
and MUST NOT write `AuditoriaCorreccion` (not in the `Accion` enum).

The created asiento MUST be **engine-composed** (`ComposicionDeAsiento.Componer`), not a bare
header: its `BasePEN`/`IgvPEN`/`NetoPEN` MUST carry the REGLAS §5/§6 projection and its líneas
MUST be the PRINCIPAL + DESTINO blocks. The default cargo línea MUST use the account returned
by `ServicioDeSugerencia` for the `(proveedor, motivo)` pair (prefix cascade, REGLAS §3); when
no suggestion resolves, the cargo línea MUST be a "sin cuenta" placeholder (Global-2 of §7
then blocks `validar` until the user picks an account). `abrir` MUST remain idempotent — when
a non-`ANULADO` asiento already exists it is a no-op and does NOT re-seed or overwrite user
edits. A foreign-currency factura with no vigente `fact.TipoCambio` still returns `409`.
(Previously: `abrir` inserted a header-only `BORRADOR` asiento with `BasePEN`/`IgvPEN`/
`NetoPEN` and líneas all NULL/absent; `ServicioDeSugerencia` was not wired.)

#### Scenario: Opening a factura with no asiento — [modified]
- **Given** a factura with no non-`ANULADO` asiento and a resolvable suggested account
- **When** `POST /api/facturas/{id}/abrir` is called
- **Then** a `BORRADOR` asiento is created with engine-composed `BasePEN`/`IgvPEN`/`NetoPEN`
  and PRINCIPAL + DESTINO líneas, the default cargo línea carrying the suggested account, and
  no `AuditoriaCorreccion` row is written
- (test: E2E abrir→load / integration)

#### Scenario: Opening when no account suggestion resolves — [new]
- **Given** a factura whose `(proveedor, motivo)` yields no `ServicioDeSugerencia` account
- **When** `POST /api/facturas/{id}/abrir` is called
- **Then** the asiento is seeded with a "sin cuenta" placeholder cargo línea; a later
  `validar` is blocked by REGLAS §7 Global-2 until an account is assigned
- (test: E2E / integration)

#### Scenario: Opening a factura with an existing BORRADOR asiento is a no-op — [new]
- **Given** a factura that already has a `BORRADOR` asiento with user-edited líneas
- **When** `POST /api/facturas/{id}/abrir` is called again
- **Then** the existing asiento and its líneas are left untouched (idempotent — no re-seed)
- (test: E2E / integration)

#### Scenario: Opening a factura with no tipo de cambio (foreign currency) — [unchanged]
- **Given** the factura is in foreign currency and `fact.TipoCambio` has no row for the fecha
  de emisión
- **When** `POST /api/facturas/{id}/abrir` is called
- **Then** the response is `409 Conflict` naming the missing tipo de cambio as the blocker
- (test: E2E / integration)

## ADDED Requirements

### Requirement: `POST /api/facturas/{id}/recomponer` regenerates the engine seed

The endpoint MUST regenerate the `BORRADOR` asiento from `ComposicionDeAsiento.Componer`,
**replacing** its líneas (PRINCIPAL + DESTINO) and re-deriving the header projection, using
the factura's current values and the currently suggested cargo account. It MUST be rejected
when the asiento is `CONFIRMADO` (`409`/`422`). The regeneration MUST be audited (an
`AuditoriaCorreccion` row recording the líneas replacement). Manual line edits (#12) made
before `recomponer` are intentionally discarded by this action.

#### Scenario: Recomponer replaces líneas on a BORRADOR asiento — [new]
- **Given** a `BORRADOR` asiento whose líneas were manually split and no longer balance the base
- **When** `POST /api/facturas/{id}/recomponer` is called
- **Then** the líneas are regenerated as a fresh PRINCIPAL + DESTINO seed, the header
  projection is re-derived, and an audit row records the replacement
- (test: E2E / integration)

#### Scenario: Recomponer on a CONFIRMADO asiento is rejected — [new]
- **Given** a factura whose asiento is `CONFIRMADO`
- **When** `POST /api/facturas/{id}/recomponer` is called
- **Then** the request is rejected and no líneas change
- (test: E2E / integration)

### Requirement: `validar` evaluates REGLAS §7 against the persisted seeded asiento

`validar` MUST run `InvariantesDeConfirmacion` against the **persisted** asiento — the engine
seed plus any audited user deltas (manual líneas #12, scalar base/IGV projection #19) — and
MUST NOT re-compose the asiento at confirm time (owner decision 1). Because `abrir` now always
seeds PRINCIPAL + DESTINO líneas, the §7 PRINCIPAL / DESTINO / Global-1 checks evaluate real
data; a freshly seeded asiento with a single valid cargo account passes for real and reaches
`CONFIRMADO` + correlativo (downstream behavior unchanged).

#### Scenario: Freshly seeded asiento with no manual edits validates for real — [new]
- **Given** a `BORRADOR` asiento seeded by `abrir` with a valid single cargo account, no user edits
- **When** `POST /api/facturas/{id}/validar` is called
- **Then** §7 PRINCIPAL (`sumaCargos == BasePEN` gravada / `NetoPEN` otherwise), DESTINO
  (reflejo/puente), and Global-1 (balanced) all pass on real líneas; the asiento becomes
  `CONFIRMADO` with a correlativo
- (test: E2E abrir→validar / integration)

#### Scenario: REGLAS §10.1 / §10.2 / §10.3 pass end-to-end through abrir→validar — [new]
- **Given** a factura matching REGLAS §10.1 (gravada soles con destino), §10.2 (boleta IGV al
  costo), or §10.3 (dólares con redondeo derivado)
- **When** `abrir` then `validar` run with no intervening edits
- **Then** the resulting CONFIRMADO asiento matches the numeric example line-for-line
- (test: E2E golden / integration — reuses REGLAS §10 fixtures)

#### Scenario: §10.4 (percepción) is not covered — [new / deferred gap]
- **Given** a factura with percepción
- **When** the abrir→validar pipeline runs
- **Then** §10.4 is explicitly out of scope this cycle — no `PercepcionOrig` column; noted as
  a known gap (owner decision 2)

#### Scenario: Manual split that balances still validates — [new]
- **Given** a seeded asiento whose cargo was manually split into several accounts that still
  sum to `BasePEN`
- **When** `validar` is called
- **Then** §7 passes and the asiento is confirmed
- (test: E2E / integration)

#### Scenario: Vacuous-pass regression — empty asiento can no longer confirm — [new]
- **Given** an `AsientoContable` with zero líneas (legacy dev/demo state)
- **When** `validar` is called
- **Then** it is rejected on the missing-PRINCIPAL invariant — the previously vacuous pass is
  gone (dev/demo only, no migration; `recomponer` regenerates a valid seed)
- (test: E2E / integration — moved fixture)

### Requirement: A base/IGV edit that unbalances the seeded split blocks `validar` with a descuadre message

When a `PATCH` recomputes the scalar `BasePEN`/`IgvPEN`/`NetoPEN` projection (existing #19
behavior — líneas untouched) and the persisted cargo líneas no longer sum to the new `BasePEN`
(gravada) / `NetoPEN` (otherwise), `validar` MUST be blocked by the REGLAS §7 PRINCIPAL
invariant, returning `422` `application/problem+json` whose `detail` names the failing
invariant and the conflicting amounts. The user resolves it by re-aligning the líneas
(manual edit #12) or calling `recomponer`. This is intended behavior, not a bug (owner
decision 4).

#### Scenario: Base edit unbalances the split → validar blocked — [new]
- **Given** a seeded `BORRADOR` asiento, then a `PATCH` changes `baseImponible` so the cargo
  líneas no longer sum to the new `BasePEN`
- **When** `POST /api/facturas/{id}/validar` is called
- **Then** the response is `422` naming the §7 "cargos `6x`/`1x` igualan base imponible"
  invariant and the mismatched amounts; the asiento stays `BORRADOR`
- (test: E2E / integration)

#### Scenario: Reconciling via recomponer clears the block — [new]
- **Given** the blocked state above
- **When** the user calls `recomponer` (or re-aligns the líneas) then `validar`
- **Then** §7 passes and the asiento is confirmed
- (test: E2E / integration)
