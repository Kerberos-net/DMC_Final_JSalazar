# Archive Report: Outbox y mensajería (BACKLOG #14)

**Date**: 2026-08-25
**Change**: outbox-mensajeria
**Mode**: hybrid (OpenSpec + Engram)
**Final Status**: ARCHIVED — Ready for delivery

## Verification Gate (Task Completion + Review Authority)

Per sdd-archive skill §Final-State Authority:

- **Verify Report** (Engram #189): PASS — 51/51 tasks complete, no critical issues
- **Apply Progress** (Engram #187): Batch 6/6 complete — all phases done
- **Review Gate**: RDD disabled at repo level (per every batch's instructions); no `gentle-ai review` interaction occurred. This is consistent with project convention.
- **Tasks Artifact** (OpenSpec + Engram #186): 51/51 tasks marked [x] across all 6 phases. No unchecked implementation tasks.

All gates passed. Archive proceeds.

## Artifact Retrieval & Merging

### Source Artifacts (Engram)

| Artifact | ID | Timestamp | Status |
|----------|-----|-----------|--------|
| sdd/outbox-mensajeria/explore | #182 | 2026-08-24 15:51:06 | Read, complete |
| sdd/outbox-mensajeria/proposal | #183 | 2026-08-24 15:53:24 (revised through 3 amendment rounds) | Read, complete |
| sdd/outbox-mensajeria/spec | #184 | 2026-08-24 16:21:01 (revised) | Read, complete |
| sdd/outbox-mensajeria/design | #185 | 2026-08-24 16:26:32 (revised through 3 passes) | Read, complete |
| sdd/outbox-mensajeria/tasks | #186 | 2026-08-24 19:34:40 | Read, 51/51 checked |
| sdd/outbox-mensajeria/apply-progress | #187 | 2026-08-24 20:18:11 (6 batches) | Read, final batch 6/6 |
| sdd/outbox-mensajeria/verify-report | #189 | 2026-08-25 06:30:01 | Read, PASS verdict |

### Source Artifacts (OpenSpec)

- `openspec/changes/outbox-mensajeria/proposal.md` ✅
- `openspec/changes/outbox-mensajeria/design.md` ✅
- `openspec/changes/outbox-mensajeria/tasks.md` ✅
- `openspec/changes/outbox-mensajeria/verify-report.md` ✅
- `openspec/changes/outbox-mensajeria/RELEASE-NOTES.md` ✅
- `openspec/changes/outbox-mensajeria/specs/outbox-emision/spec.md` ✅
- `openspec/changes/outbox-mensajeria/specs/outbox-consumo/spec.md` ✅

### Main Specs Merge

Two NEW specifications (no prior versions existed):

| Spec | Domain | Action | Status |
|------|--------|--------|--------|
| outbox-emision/spec.md | outbox-emision | Copy delta to main specs | ✅ Created at `openspec/specs/outbox-emision/spec.md` |
| outbox-consumo/spec.md | outbox-consumo | Copy delta to main specs | ✅ Created at `openspec/specs/outbox-consumo/spec.md` |

**Merge notes**: Both specs are full specifications, not deltas. They define new capabilities with no prior requirements to merge against. Copied directly as-is.

## Archive Folder Movement

**Source**: `openspec/changes/outbox-mensajeria/`
**Destination**: `openspec/changes/archive/2026-08-25-outbox-mensajeria/`

**Correction note (orchestrator, post-archive)**: the first archive pass left the source folder in place (never moved) and wrote truncated/summarized stand-ins for `design.md`, `proposal.md`, `tasks.md`, `verify-report.md`, and `RELEASE-NOTES.md` in the destination instead of the real content — the same "move wasn't real" defect already seen and fixed on item #13. Caught before commit: the full content was copied from the source into the archive folder, this report was written as a file (it previously existed only in Engram), and the duplicate source folder was deleted.

**Contents** (final, verified):
- proposal.md ✅ (full, 230 lines)
- design.md ✅ (full, 438 lines — 10 architecture decisions D1–D10)
- tasks.md ✅ (full, 140 lines)
- verify-report.md ✅ (full, 68 lines)
- RELEASE-NOTES.md ✅ (full, 74 lines)
- archive-report.md ✅ (this file)
- specs/outbox-emision/spec.md ✅
- specs/outbox-consumo/spec.md ✅

## Final State Authority Summary

The archive report records FINAL state at change close, not stale intermediate snapshots. Authority ranking per skill §Final-State Authority:

1. **Verify Report** (highest rank) — #189, 2026-08-25 06:30:01
   - Verdict: **PASS** (0 critical, 0 warnings)
   - Task completeness: 51/51 checked, no unchecked implementation tasks
   - 8 independent test suites re-run with zero drift from apply-progress counts
   - All 5 catalog events verified emitting from 4 confirmed emission points with self-sufficient payloads
   - D8 double-emission guard, D10 state-CAS + 409 rollback, D3 fan-out map all present and matching design/ADR exactly
   - 2 production bugs (SeqOutbox permission grant, SET NOCOUNT ON) found and fixed, both regression-pinned via ADR 0016 migration 019
   - Bidirectional N2 contract tests green (19 passed integration, 27/27 permission matrix .NET side untouched)
   - No dbo.* or scope creep to #15/#16/#17

2. **Apply Progress** (intermediate snapshot, lower rank) — #187, final batch 6/6
   - All 6 phases complete: Phase 1-5 from prior batches, Phase 6 (regression + release note) this batch
   - Regression re-run of closed #7/#11 suites: zero new failures, only expected D9 sequence assertion shape deltas
   - RELEASE-NOTES.md documents both visible behavior changes on existing endpoints (`POST /descartar` 409, `POST /validar` 409+rollback)

3. **Explicit final-state facts** confirmed against the above:
   - 5 events of catalog emitted in 4 points with self-sufficient payload ✅
   - Gap of item #11 closed (`MarcarFacturaValidadaAsync`, D10, `NoTransicionable`→409) ✅
   - Guard by-transaction against double-emission (D8) ✅
   - Fan-out to `OutboxEventIntegracion` (D3, DRIVE/SHEETS map confirmed by user) ✅
   - Consumer Python complete (READPAST+interface, obsolescence guard, lease 5min, cadence 1min) ✅
   - Contract tests ADR 0019 N2 bidirectional ✅
   - 2 real production bugs found and corrected (migration 019 permissions, SET NOCOUNT ON) ✅
   - 2 visible behavior changes documented in release notes ✅
   - ~1,700+ lines under `size:exception`, accepted by project owner across 3 separate rounds ✅

All facts from final-state authority sources agree: **change is complete, verified PASS, and ready for archive.**

## Known Deviations (All Resolved)

Per apply-progress and verify-report:

| Deviation | Phase | Resolution | Status |
|-----------|-------|-----------|--------|
| `IUnidadDeTrabajo` breaking interface (`MarcarFacturaValidadaAsync`) | 1 | `SqlUnidadDeTrabajo` immediately gained implementation to keep solution compiling | Resolved, tested |
| `ConfirmarAfectacionAsync` VALIDADA gate reading (D8) | 2 | Spec requirement and test fixture clarified; gate necessary per ADR 0004 | Resolved, tested |
| Deferred D8 guard test (per-tx emission guard) | 2→3 | Moved to batch 3 with dedicated real-schema integration test | Resolved, tested |
| D3 Tipo→Integracion applicability map inferred from ADR 0004 prose | 3 | Owner confirmed map at batch 4 kickoff; matches ADR 0004 exactly | Resolved, verified |
| Real bug #1: missing `GRANT UPDATE ON fact.SeqOutbox` to `fact_api` | 5 | New migration `019_permiso_secuencia_seqoutbox.sql` + rollback; ADR 0016 honored | Resolved, regression-pinned |
| Real bug #2: missing `SET NOCOUNT ON` in Python `outbox_repo.reclamar` | 5 | One-line addition + new unit test | Resolved, regression-pinned |
| Archive "move" wasn't real; archived docs truncated/fabricated | archive | Orchestrator caught before commit: real content copied over stubs, this report written as a file, duplicate source folder deleted | Resolved |

**Zero deviations remain open.**

## Deliverable State Snapshot

### Producer (.NET)

- 4 `EmitirOutboxAsync` call sites wired (D1, D8):
  - `ServicioDeFacturas.ValidarInternoAsync` — `ASIENTO_CORREGIDO` on reconfirm
  - `ServicioDeAsientos.AnularAsync` — `ASIENTO_ANULADO`
  - `ServicioDeFacturas.PatchAsync` — `FACTURA_CORREGIDA` on correction
  - `ServicioDeFacturas.ConfirmarAfectacionAsync` — `FACTURA_CORREGIDA` on `AfectacionMixta` change
- All 5 catalog events carry self-sufficient full-snapshot payloads via `PayloadOutbox` (D2)
- `ValidarInternoAsync` writes `fact.Factura.Estado = 'VALIDADA'` via `MarcarFacturaValidadaAsync` (D10), with `NoTransicionable` → 409 + rollback on `DESCARTADA` factura (OQ5)
- Per-transaction double-emission guard on `(Tipo, FacturaId)` (D8)
- Fan-out to `OutboxEventIntegracion` rows per applicability map (D3)

### Consumer (Python)

- `ReclamoDeLote` Protocol + `reclamo.py` — batch claim with `READPAST` (isolated to `outbox_repo.py` only, D6)
- 5-minute lease constant `ARRENDAMIENTO` (D4, OQ3)
- Pure `guarda_obsolescencia.evaluar()` — no I/O, no raises (D5)
- Destination-agnostic `despacho_outbox.py` dispatcher (empty handler registry in #14)
- CLI `smartnet-outbox` on own 1-minute cadence (D7)

### Contract & Permission

- Bidirectional N2 boundary tests: .NET writes/Python reads and vice versa against real schema
- Permission matrix confirmed:
  - `usr_api`: can INSERT/SELECT `OutboxEvent`, can INSERT `OutboxEventIntegracion` (needs SeqOutbox UPDATE grant, fixed by migration 019)
  - `usr_worker`: can SELECT/UPDATE both tables (never writes `fact.Factura`), never inserts parent rows
- `READPAST` concurrency test: two clients claim simultaneously, disjoint sets
- Lease expiry test: row invisible at +4min, reclaimable at +6min

### Visible Behavior Changes (Release Notes)

1. **`POST /descartar` on VALIDADA factura → 409**: `DescartarAsync`'s guard (`:292`) goes live once `Estado` becomes real (D10)
2. **`POST /validar` on DESCARTADA factura → 409 + rollback**: `MarcarFacturaValidadaAsync` returns `NoTransicionable`, aborts before commit (OQ5)

Both changes documented in `RELEASE-NOTES.md`. Both forward-only (D10 not backfilled). Neither breaks a consumer (none existed before #14).

### Test Evidence (Independent Re-run by Verifier)

| Suite | Count | Result |
|-------|-------|--------|
| `SmartNet.Facturacion.Core.Tests` | 88 | ✅ PASS |
| `SmartNet.Facturacion.Infrastructure.Tests` (real SQL Server) | 46 | ✅ PASS |
| `SmartNet.Inbox.Core.Tests` | 49 | ✅ PASS |
| `SmartNet.Inbox.Infrastructure.Tests` (real schema) | 41 | ✅ PASS |
| `SmartNet.Api.Tests` (integration) | 143 | ✅ PASS |
| `SmartNet.Db.Runner.Tests --filter PermissionMatrixTests` | 27 | ✅ PASS (unchanged from #13) |
| `worker pytest tests/unit` | 210 | ✅ PASS |
| `worker pytest tests/integration` | 19 passed, 1 skipped | ✅ PASS |

**Zero drift** from batch-6's reported counts. **Zero regressions** vs. closed #7/#11.

## SDD Cycle Complete

- **Explore** → schema/permission state mapped, producer gap and consumer gap both found
- **Proposal** → approved by owner, amended 3 times (payload retrofit, 4th emission point, #11 state-transition gap)
- **Spec** → two new specs written, no delta merge needed (capabilities didn't exist before)
- **Design** → 10 architecture decisions (D1–D10), 5 open questions resolved by owner (OQ1–OQ5), 2 deferred forward-only (OQ6–OQ7)
- **Tasks** → 51 implementation tasks across 6 phases
- **Apply** → 6 batches, all complete, 2 real production bugs found and fixed with regression pins
- **Verify** → PASS, 8 test suites re-run independently, zero regressions, all spec requirements met
- **Archive** → change folder content copied to archive with ISO date prefix, main specs updated, this report persisted both to Engram and as a file

**Next Change**: Item #15 (Publicación a Drive) or #16 (Publicación a Sheets) — both depend on #14 and can now begin SDD cycles.

## Traceability

- Exploration: Engram #182
- Proposal: Engram #183
- Spec: Engram #184
- Design: Engram #185
- Tasks: Engram #186
- Apply Progress (batch 6, final): Engram #187
- Verify Report: Engram #189
- Archive Report: Engram #190 (this file)

Archive folder: `openspec/changes/archive/2026-08-25-outbox-mensajeria/`
