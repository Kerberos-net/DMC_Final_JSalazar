# Archive Report: item-13-bandeja-incidencias (BACKLOG #13)

**Archived**: 2026-08-24
**Change**: item-13-bandeja-incidencias (Bandeja e incidencias — BACKLOG #13)
**Status**: CLOSED ✓

## Cycle Summary

The SDD cycle for item #13 is complete: proposal → spec → design → tasks → apply → verify → archive.

- **Proposal** (Engram #168): Widening `GET /api/bandeja` in place to the full ADR 0008 contract with filters, pagination, `origen` discriminator, error history panel, and reprocesar UI with confirmation.
- **Spec** (Engram #170): Three domain specs — new `bandeja` (full spec), delta `api-incidencias-integraciones` (clarifies `{id}` = `ProcesamientoId`), delta `inbox-screen` (adds filter inputs, panel de errores, reprocesar confirmation+pending-window).
- **Design** (Engram #171): Eight architecture decisions (D1-D8) covering permission grant (018), SQL batch strategy, `origen` derivation, pagination, 5-min re-enable window, confirmation dialog, proveedor identity+fallback, and error panel placement.
- **Tasks** (Engram #173): 51/51 tasks marked complete across 6 phases: DB permisos + ADR 0003 amendment, Core pure logic, Infrastructure batch query, Api param binding, SPA filters/panel/reprocesar, and full verification.
- **Apply Progress** (Engram #175/#176): Implementation complete across 3 apply batches (one interrupted by a session/API error mid-batch, verified and recovered by the orchestrator via direct dotnet build/test before continuing), all code changes merged, all tests green.
- **Verify Report** (Engram #178): 0 CRITICAL, 0 WARNING, 2 SUGGESTIONS (non-blocking). 51/51 tasks complete. Full spec compliance verified. Design decisions D1-D8 all implemented as specified. ADR 0019 (accounting core purity) maintained.

## Final State — Authority Hierarchy

### 1. Native Review Authority (if applicable)
Not in scope for this project.

### 2. Persisted Tasks Artifact
File: `openspec/changes/archive/2026-08-24-item-13-bandeja-incidencias/tasks.md` (51/51 tasks [x] checked)
- All implementation, integration, API, and SPA tasks complete
- All verification tasks complete
- Working tree matches Affected Areas tables exactly

### 3. Explicit Final-State Facts (orchestrator-verified)
- 51/51 tasks complete
- dotnet test green: SmartNet.Inbox.Core.Tests 49/49, SmartNet.Inbox.Infrastructure.Tests 41/41, SmartNet.Api.Tests 132/132, SmartNet.Db.Runner.Tests 134/134 (standalone, no contention)
- SPA: `npx ng test --watch=false` → 162/162 green; `npx ng build` → clean
- ADR 0003 amended to revision 6 (reclassifies fact.ProcesamientoError as asymmetric-read)
- Migration 018 applied and verified
- No regressions in SmartNet.Contable.Core / PurityScanTests

### 4. Verify Report Snapshot (Engram #178)
51/51 checked, all tests green, spec compliance matrix PASS, all 8 design decisions implemented as specified, 2 SUGGESTIONS non-blocking.

**Conclusion**: Final state is COMPLETE and CLOSED.

## Spec Merges — Authority

### bandeja (NEW)
- Created: `openspec/specs/bandeja/spec.md` (full spec, not a delta)
- Status: Merged ✓

### api-incidencias-integraciones (MODIFIED)
- Updated: `openspec/specs/api-incidencias-integraciones/spec.md`
- Changed: `{id}` in `reprocesar` MUST be interpreted as `ProcesamientoId`, new scenario "Reprocesar uses ProcesamientoId, not InboxEventId or FacturaId"
- Status: Merged ✓

### inbox-screen (MODIFIED + ADDED)
- Updated: `openspec/specs/inbox-screen/spec.md`
- MODIFIED: "Read-only in this item" amended to "Read-only except the reprocesar action"
- ADDED: filter inputs desde/hasta/proveedor, panel de errores rendering, reprocesar confirmation+pending-window
- Status: Merged ✓

## Archive Location

**Moved**: `openspec/changes/item-13-bandeja-incidencias/` → `openspec/changes/archive/2026-08-24-item-13-bandeja-incidencias/`

Archived artifacts: `exploration.md`, `proposal.md`, `design.md`, `tasks.md`, `verify-report.md`, `archive-report.md` (this file), `specs/bandeja/spec.md`, `specs/api-incidencias-integraciones/spec.md`, `specs/inbox-screen/spec.md`.

## Observation IDs (Traceability)

- Proposal: #168 `sdd/item-13-bandeja-incidencias/proposal`
- Spec: #170 `sdd/item-13-bandeja-incidencias/spec`
- Design: #171 `sdd/item-13-bandeja-incidencias/design`
- Tasks: #173 `sdd/item-13-bandeja-incidencias/tasks`
- Verify Report: #178 `sdd/item-13-bandeja-incidencias/verify-report`
- Delivery-strategy decision: #174 `architecture/decisi-n-tem-13-estrategia-de-entrega`
- ADR 0003 ratification decision: #172

## Key Decisions Archived

1. **D1 Permission Grant** (`018_permiso_lectura_procesamiento_error.sql`): REVOKE prior DENY, GRANT SELECT on `fact.ProcesamientoError` to `fact_api`, re-DENY INSERT/UPDATE/DELETE. Ratified by owner.
2. **ADR 0003 Amendment** (revision 6): Reclassified `fact.ProcesamientoError` from class 1 (Privada) to asymmetric-read (Python writes, both read), citing `fact.Configuracion` precedent.
3. **D2 origen Discriminator**: Flat C# record with nullable fields + string discriminator; TS narrows via discriminated union over same JSON wire.
4. **D3 Error Projection**: Second result set in same SqlCommand batch (not LEFT JOIN) keyed by `@pagina` table variable.
5. **D4 Pagination**: OFFSET/FETCH + InboxEventId tiebreak + `COUNT(*) OVER()`, fallback COUNT only when page empty and `pagina>1`.
6. **D5 5-Minute Re-enable**: Server computes `reprocesarDisponibleEn`; client never derives business rules from its own clock.
7. **D6 Confirmation Dialog**: Native `<dialog>` element, not CDK/Material or `window.confirm`.
8. **D7 Proveedor Identity**: Match on FacturaCodigo/RucProveedor (promoted), fallback to `JSON_VALUE(Payload)` for non-promoted.
9. **D8 Error Panel Placement**: Dumb `panel-errores`, embedded in `<details>` by `inbox-list`.

## Scope Discipline Confirmed

- 6th indicator `EsReferenciaExterna` — stays DDL default (deferred to #10)
- #18 (Ajuste visual SPA) — untouched
- Separate `GET /api/incidencias` endpoint — rejected
- Multi-role authorization — rejected (ADR 0007)

## Test Evidence (Final)

- Database: `Db.Runner.Tests` 134/134 ✓ (standalone; parallel-run contention is orthogonal to #13)
- Core: `Inbox.Core.Tests` 49/49 ✓ (includes `PurityScanTests`)
- Infrastructure: `Inbox.Infrastructure.Tests` 41/41 ✓ (runs as `usr_api`, proves D1 grant)
- Api: `Api.Tests` 132/132 ✓
- Contable Core: 41/41 ✓ (untouched)
- SPA: `npx ng test --watch=false` → 162/162 ✓; `npx ng build` → clean ✓

## Notable Deviations (All Resolved)

1. **Task 4.4 default-view test split** between API and Infra layers (a pre-existing `PromocionBackgroundService` crashes the API test host with a stub PENDIENTE row) — documented, both halves covered.
2. **`checksums.txt` recovery** — migration 018's hash was initially missing, caught by the project's own checksum-verification tests, fixed additively.
3. **Apply batch 2 session interruption** — one `sdd-apply` batch crashed mid-run on a session/API error (403), unrelated to code content. The orchestrator verified the working tree directly with `dotnet build`/`dotnet test` before trusting the partial work, confirmed Phase 3 (Infrastructure) was complete and green, and continued from there.

## ADR Amendments

**ADR 0003** (revision 6): Reclassified `fact.ProcesamientoError` from class 1 (Privada) to asymmetric-read (Python writes, both read), precedent `fact.Configuracion`.

## Delivery Strategy

`sdd-tasks` forecast ~520-650 changed lines (High risk, over the 400-line budget) and suggested a 4-PR chain (DB/permisos+ADR → Core+Infrastructure → Api → SPA). Per `delivery_strategy=ask-on-risk`, the orchestrator asked the product owner, who explicitly chose **a single PR with `size:exception`** instead — the entire change was implemented and delivered as one unit, not split into 4 PRs. `chain_strategy` does not apply.

## Next Item

No follow-up SDD needed. Item #13 is complete and closed.

---

**Mode**: hybrid (Engram persistence + OpenSpec filesystem)
**Completeness**: All phases concluded, no open issues, 2 suggestions documented as non-blocking.
