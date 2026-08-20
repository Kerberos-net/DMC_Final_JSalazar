# Proposal: Tipos de cambio (BACKLOG #4)

## Intent

`fact.TipoCambio` exists since item #1 but no application code can read or write it. Facturas
cannot be valued without a venta rate for their fecha de emisión (ADR 0018, REGLAS.md §6), so
this item builds the domain/infrastructure capability to load rates and expose the vigente rate
for a date — the prerequisite both #8 (asiento freezing) and #11 (API 409 gate) depend on.

**No new DDL.** Table, composite PK `(Fecha, Origen)`, CHECK, and `fact_api`/`fact_worker` grants
already shipped in `007_publicacion.sql` / `008_usuarios_y_permisos.sql`.

## Decisions already resolved (not open questions)

- **SBS > MANUAL priority**: when rows exist for both origins on the same date, the lookup
  returns SBS; MANUAL is the fallback when SBS did not publish. Reason: SBS is the authoritative
  nightly source; MANUAL exists only as the escape hatch REGLAS.md §6 anticipates.
- **Python scope is minimal**: `SmartNet/worker/` is created here (first Python in the repo) with
  a single-run SBS scraper that writes `Origen='SBS'` rows. No scheduler/polling/retry
  orchestration — deferred to #5 (Gmail ingestion), which needs a polling loop anyway and will
  reuse whatever tooling convention this item establishes.

## Scope

### In Scope
- `SmartNet.TiposCambio.Core` (.NET, no PackageReference) — domain rule for SBS>MANUAL selection,
  a typed "no rate for date" result (no null/generic exception).
- `SmartNet.TiposCambio.Infrastructure` (Microsoft.Data.SqlClient) — repository: insert MANUAL
  row, lookup vigente rate by date, mirrors `SmartNet.Catalogos.Infrastructure` pattern
  (`NoWriteToDboStructuralTests.cs`, `PermissionSufficiencyTests.cs`).
- `SmartNet/worker/` — minimal Python SBS scraper, single execution, writes `Origen='SBS'` rows,
  logs attempt to `fact.EstadoIntegracion` (Nombre='SBS').
- "Bloqueo si no hay dato": this item exposes the absence as an explicit typed domain result
  (Option/nullable-with-intent) the repository/Core surface — it does NOT build the HTTP 409
  endpoint.

### Out of Scope
- `AsientoContable.TipoCambioVenta` / `Factura.TipoCambioAplicado` freeze-at-confirm logic (#8).
- Scheduler/orchestration/retry policy for recurring scraping (deferred to #5).
- HTTP endpoint returning 409 (#11) — this item only makes "no data" observable, not HTTP-shaped.
- Accountant ratification of REGLAS.md §12 points 1 (tasa venta) and 5 (NC inherits factura's
  rate) — still pending; #4's completion does not imply these rules are final.

## Capabilities

### New Capabilities
- `tipos-de-cambio`: manual rate loading, SBS-priority lookup of vigente rate by date, typed
  absence signaling. Covers `SmartNet.TiposCambio.Core`/`Infrastructure` and `SmartNet/worker/`.

### Modified Capabilities
None.

## Approach

Mirror the item #2/#3 hexagonal split (Core pure, Infrastructure holds SQL). Core owns the
SBS>MANUAL selection rule per ADR 0019 (no DB in domain logic) and the "sin dato" result type.
Infrastructure implements insert (MANUAL) and select (both origins) against the existing table.
Python worker is a standalone script under `SmartNet/worker/`, scrapes SBS once, upserts via
`fact_worker` credentials, records outcome in `fact.EstadoIntegracion`.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `SmartNet/tipos-de-cambio/SmartNet.TiposCambio.Core` | New | Domain rule + typed absence result |
| `SmartNet/tipos-de-cambio/SmartNet.TiposCambio.Infrastructure` | New | SQL repository (manual load + lookup) |
| `SmartNet/worker/` | New | First Python code; single-run SBS scraper |
| `SmartNet.sln` / CI | Modified | Wire new projects, first Python CI step |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Python has no repo precedent (tooling, tests, CI) | Med | Keep worker minimal; document convention for #5 to reuse |
| REGLAS.md §12 pts 1/5 not accountant-ratified | Low | Footnoted; not blocking storage/lookup mechanics |
| Discrepancy handling (SBS vs MANUAL) under-specified | Low | Both rows persist and remain queryable; no dedicated audit table this item |

## Rollback Plan

Revert the two new .NET projects and `SmartNet/worker/`; no DDL/grants to roll back since none
are added. `SmartNet.sln`/CI changes revert with the same commit.

## Dependencies

- Item #1 (schema/permissions) — already closed.

## Success Criteria

- [ ] MANUAL rate can be loaded via `SmartNet.TiposCambio.Infrastructure`.
- [ ] Lookup returns SBS when both origins exist for a date; MANUAL as fallback.
- [ ] Absence of a rate for a date is a typed, observable result — not null/generic exception.
- [ ] Python scraper writes `Origen='SBS'` rows and logs to `fact.EstadoIntegracion` on every run.

## Proposal question round

Two blocking decisions from exploration are already resolved by the user (see above). Remaining
open points, with proposed assumptions — confirm, correct, or request a second round:

1. When SBS and MANUAL disagree for the same date, is it enough that both rows simply persist and
   are queryable (assumed: yes, no dedicated discrepancy flag/audit table this item), or does the
   business want an explicit "hay discrepancia" signal in this item?
2. Should a failed SBS scrape write something observable beyond `fact.EstadoIntegracion` (assumed:
   that's sufficient — no alerting/notification in this item)?
3. Does the lookup result need to expose *which* origin was used (SBS vs MANUAL) to callers, or
   only the numeric rate (assumed: origin is exposed, since #8/audit may need it later)?
