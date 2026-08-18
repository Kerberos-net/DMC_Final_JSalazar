# Exploration: Tipos de cambio (BACKLOG #4)

## Current State

`fact.TipoCambio` DDL already exists (item #1, `SmartNet/db/schema/007_publicacion.sql`):
`Fecha DATE, Origen VARCHAR(10) CHECK IN ('SBS','MANUAL'), Compra/Venta DECIMAL(12,6),
FechaConsulta DATETIME2(3), CargadoPorUsuarioId BIGINT NULL FK->Usuario, CargadoEn DATETIME2(3)`,
PK `(Fecha, Origen)` — deliberately composite so an SBS row and a MANUAL row for the same date
can coexist without silent overwrite. GRANTs in `SmartNet/db/schema/008_usuarios_y_permisos.sql`
already give both `fact_api` (.NET) and `fact_worker` (Python) `SELECT, INSERT, UPDATE` (no
DELETE). `PermissionMatrixTests.cs` and `SchemaShapeTests.cs` already assert this shape.
`fact.AsientoContable.TipoCambioVenta` and `fact.Factura.TipoCambioAplicado` (both
`DECIMAL(12,6) NULL`) already exist for the frozen-at-confirm rate.

No application code exists for #4 yet: no `SmartNet.TiposCambio.*` projects, and
`SmartNet/worker/` (the planned Python location per `SmartNet/README.md`) does not exist on disk
at all — this is the first item that would create it, unless deferred to #5.

## Governing rules (ADR 0018 + REGLAS.md §6 — not ⚠ but directly on point)

1. Tipo de cambio **venta**, not compra (pasivo, not activo).
2. Rate frozen at the invoice's **fecha de emisión**, congealed into the asiento at confirm time.
3. **No row for the date → invoice does not open for edition, API returns 409.** This is the
   backlog's "bloqueo". SBS publishes nightly; Friday covers Sat/Sun/Mon, so only "SBS didn't
   publish" triggers it — manual load (`Origen='MANUAL'`) is the sanctioned escape hatch.
4. Nota de crédito `07` con referencia interna inherits `F.TipoCambio`, does not recompute
   (item #8/#10 concern, but #4 must expose lookup so #8 can read a factura's frozen rate
   trivially).
5. Points 1 and 4 are explicitly pending accountant ratification (REGLAS.md §12) — doesn't block
   #4's storage/lookup mechanics, but should be footnoted.

## Affected Areas

- `SmartNet/tipos-de-cambio/SmartNet.TiposCambio.Core` (new) — pure domain, ADR 0019, mirrors
  `SmartNet.Catalogos.Core`/`SmartNet.Auth.Core` split.
- `SmartNet/tipos-de-cambio/SmartNet.TiposCambio.Infrastructure` (new) — SQL adapter:
  manual-load command + rate lookup query, mirrors `SmartNet.Catalogos.Infrastructure` pattern
  (`SqlCuentaContableRepository.cs`, `NoWriteToDboStructuralTests.cs`,
  `PermissionSufficiencyTests.cs`).
- `SmartNet/worker/` (new, first-ever) — Python SBS scraper writing `Origen='SBS'` rows; no
  Python tooling/convention precedent exists anywhere in the repo yet.
- `fact.EstadoIntegracion` row `Nombre='SBS'` — already in schema, should be written by the
  scraper on every attempt (success/fail).
- Explicitly out of scope: `AsientoContable.TipoCambioVenta` consumption/freezing logic — that's
  item #8.

## Approaches

1. **Full scope now — .NET manual-load + Python SBS scraper, bootstrapping `SmartNet/worker/`.**
   - Pros: item #4 delivers the backlog line end-to-end; no downstream item retrofits the worker
     skeleton.
   - Cons: mixes proven .NET Core/Infrastructure pattern work with bootstrapping an entirely new
     Python project (no `pyproject.toml`, no test-runner precedent) in one change — large diff,
     harder single-PR review.
   - Effort: High.

2. **.NET-only this item; defer SBS scraper/worker scaffold to a follow-up or to #5.**
   - Pros: stays inside the proven pattern, small reviewable change, doesn't force Python tooling
     decisions now.
   - Cons: BACKLOG.md's line explicitly includes "scraping SBS" — splitting it out changes what
     "#4 done" means and needs an explicit call; #8 would need SBS rows seeded manually for
     testing.
   - Effort: Medium.

3. **Full scope, but minimal worker scaffold — a script that scrapes once and writes rows, no
   scheduler/production hardening (scheduling deferred to #5, which needs a polling loop anyway
   for Gmail).**
   - Pros: satisfies the backlog wording without over-building Python infra prematurely; avoids
     building shared Python scheduling infra twice.
   - Cons: still requires deciding Python tooling/conventions for the first time.
   - Effort: Medium-High.

## Recommendation

Approach 3, but only after explicitly raising the open question below to the user before writing
`proposal.md` — BACKLOG.md's dependency graph (#4 and #5 both depend only on #1) doesn't resolve
who scaffolds `SmartNet/worker/` first, and CLAUDE.md itself requires unforeseen design decisions
to be raised before implementing. If the user prefers a smaller #4, Approach 2 is the safer
fallback to offer as an alternative.

## Risks

- **Blocking question**: who scaffolds `SmartNet/worker/` first — item #4 or item #5? Both
  depend only on #1; BACKLOG.md doesn't order them relative to each other.
- **Genuine spec gap**: neither ADR 0018 nor REGLAS.md §6 nor TECH-DESIGN.md states which row
  (`SBS` or `MANUAL`) the lookup query should prefer when both exist for the same date — this
  must be decided explicitly (not defaulted silently) since #8 will freeze whatever #4's lookup
  returns into confirmed asientos.
- Mechanics of "registra la discrepancia" (TECH-DESIGN.md L304-305) are unspecified beyond "both
  rows persist" — likely derivable by querying both rows rather than needing a dedicated audit
  table, but should be confirmed in the proposal.
- Scraping failure/retry policy is not numerically specified (ADR 0002 only says "tolerante a
  fallo, con reintentos"); recommend writing to `fact.EstadoIntegracion` (`Nombre='SBS'`) on
  every attempt and treating scrape failure as non-blocking except via the existing
  409-if-no-row-for-date rule.
- First Python code in the whole repo — no `pyproject.toml`, CI step, or test framework
  precedent exists; this is a bigger unknown than the .NET side.
- REGLAS.md §12 flags rules 1 and 5 (venta rate, NC inheritance) as not yet ratified by an
  accountant — doesn't block #4, but the proposal should note it so #4's completion isn't
  mistaken for "the exchange-rate rule is final."

## Ready for Proposal

Yes, contingent on resolving one blocking question first: **who scaffolds the Python worker
(`SmartNet/worker/`) — item #4 or item #5?**
