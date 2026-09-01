# Archive Report: asociacion-pdf-clave-desde-xml

**Change**: asociacion-pdf-clave-desde-xml  
**Archived to**: `openspec/changes/archive/2026-09-01-asociacion-pdf-clave-desde-xml/`  
**Archived on**: 2026-09-01  
**Status**: COMPLETE  

## Executive Summary

Change successfully archived and closed. Worker-only PDF↔XML association by filename containment (Option D + alphanumeric serie fix) plus PDF-only InboxEvent re-emit on `DocumentoAsociadoId` NULL→non-null transition. Unit gate green (285 passed, +19); specs merged into main; ADR 0017 amended to Revision 3; 4 acknowledged handoffs recorded (integration tests blocked by pre-existing conftest bug, BACKLOG #26 owner-managed). No CRITICAL issues. Lineage #19→#24→#25→#26.

## Artifact Traceability (Engram observation IDs)

| Artifact | Obs ID | Status |
|----------|--------|--------|
| Proposal | #292 | Complete — real invoice pain point, owner-approved Option D |
| Spec (deltas) | #293 | Complete — 4 requirements (2 ADDED extraccion, 1 MODIFIED + 1 ADDED inbox), 16 scenarios |
| Design | #294 | Complete — 9 decision points (D1-D9), pure Python, no schema/NET changes |
| Tasks | #295 | Complete — 23/27 done; 4 open are handoffs (W1, W3, W4 noted in verify) |
| Verify Report | #299 | Pass with warnings (0 CRITICAL, 5 WARNING, 2 SUGGESTION) |
| Apply Progress | (embedded in tasks #295) | Partial — code/unit tests GREEN, integration WRITTEN but NOT RUN |

## Final State per Authority Hierarchy

**Source rank 1 (launch prompt final-state facts)**: 23/27 tasks complete; 4 open are handoffs (integration blocked, BACKLOG deferred, commit hygiene). Verify verdict: pass-with-warnings (0 CRITICAL). Unit gate green 285 passed (+19 vs 266 baseline).

**Source rank 2 (tasks artifact #295)**: APPLY COMPLETE (batch 1, 2026-09-01). Unit gate 285 passed. Ruff clean on changed files. Integration tests written, not run (no SQL Server).

**Source rank 3 (verify-report #299, intermediate snapshot)**: Confirms spec compliance 16/16 scenarios, 4/4 requirements at unit level. ADR 0017 amendment correct (Revision 3). "adrs - v2/" untouched.

**Contradiction resolution**: Spec text near-miss scenario originally listed `01230` as non-match alongside `12300`. Design D2 normalization (`normalizar_numero` strips leading zeros) makes `01230` → `1230` a genuine match. Implementation is correct per the design and CLAUDE.md rule 1 (deliberate reconciliation, not silent). **W2 correction applied to merged spec at archive time** (dropped `01230` reference, kept `12300`).

## Specs Merged into Main

### extraccion-y-asociacion

**Delta**: 2 ADDED requirements, 10 scenarios (new capability `asociacion-por-nombre-archivo`)  
**Main spec updated**: YES — appended 2 ADDED requirements before `clasificacion-errores-permanente`  
**W2 correction applied**: YES — near-miss scenario now lists only `12300` as the non-match (removed `01230` per design D2)  
**Status**: MERGED ✓

### inbox-event-publishing

**Delta**: 1 MODIFIED + 1 ADDED requirement, 6 scenarios  
**Main spec updated**: YES — expanded "Idempotent publishing" requirement + added "PDF-only NULL→non-null re-emit" requirement  
**Status**: MERGED ✓

Both specs now carry the definitive behavior. No stale references remain.

## Archive Contents Verified

- ✓ explore.md (exploration/options analysis)
- ✓ proposal.md (intent, scope, approach)
- ✓ design.md (technical decisions D1-D9, data flow, file changes, threat analysis)
- ✓ tasks.md (6 phases, review workload forecast, 27 tasks — 23 checked, 4 handoffs noted)
- ✓ specs/extraccion-y-asociacion/spec.md (delta, W2-corrected)
- ✓ specs/inbox-event-publishing/spec.md (delta)
- ✓ apply-progress.md (TDD evidence, files changed, deviations, deferred items)
- ✓ verify-report.md (PASS WITH WARNINGS, 0 CRITICAL, 16/16 scenarios pass at unit level, 5 WARNINGs, 2 SUGGESTIONs)
- ✓ archive-report.md (this file)

All tasks in tasks.md are properly marked: [x] for done, [~] for handoffs. No stale unchecked implementation tasks remain.

## Handoffs Recorded (final state, not incomplete)

### H1 — Task 4.7 / 6.2: Integration tests blocked by pre-existing conftest bug

**Artifact status**: 2 integration tests written and collectable (`test_segunda_pasada_containment_toca_solo_procesamiento_y_documentorecibido` + `test_reemision_pdf_only_candidate_query_y_no_repeticion`)  
**Blocker**: tests/integration/conftest.py `_RUNNER_PROJECT` path incorrect (`SmartNet/db/runner/` instead of `SmartNet/SmartNetApi/db/runner/`). All 27 worker integration tests skip.  
**Owner action**: Fix conftest path, run `pytest -m integracion` with disposable SQL Server + dotnet.  
**Evidence of readiness**: `pytest --collect-only` confirms both new tests are importable and discoverable.

### H2 — Task 5.2: BACKLOG.md #26 deferred to owner

**Artifact status**: `BACKLOG.md` NOT edited (owner-managed). Draft for item #26 (Factura uniqueness guard by comprobante identity) recorded in apply-progress.md.  
**Owner action**: Review draft in apply-progress.md; decide on #26 scope and priority; edit BACKLOG.md directly.  
**Not a defect**: BACKLOG.md ownership is deliberate per project conventions.

### H3 — Commit hygiene (W4)

**Working tree status**: The branch `item-19-campos-contables-editables` carries unrelated uncommitted work from change #25 (pdf-asociado-en-documento-factura):
- .NET: SqlPromocionRepository.cs, PromocionBackgroundService.cs, PoliticaDeDocumentoAsociado.cs, IPromocionRepository.cs + tests
- SPA: visor-documento changes
- Specs: openspec/specs/factura-promotion, openspec/specs/pantalla-detalle-validacion

**Owner action**: When committing, stage ONLY:
- `SmartNet/SmartNetWorker/**` (6 Python files + test files)
- `adrs/0017-frontera-del-motor-de-extraccion.md`
- `openspec/changes/asociacion-pdf-clave-desde-xml/**` (now archived)

Do NOT commit the #25 work to this PR.

## Key Corrections Documented

### W2 — Spec-text near-miss scenario (CLAUDE.md rule 1)

**Issue**: Delta spec listed `01230` and `12300` as non-matches in the near-miss scenario.  
**Root cause**: Ambiguity between design intent (D2 normalization rules) and spec text wording.  
**Design D2 clarification**: `normalizar_numero("01230") == "1230"` (strips leading zeros, same rule for `00001230`). Therefore `01230` IS a genuine match, only `12300` → `12300` ≠ `1230` is a true near-miss.  
**Implementation**: Correct per design. Test `test_numero_con_ceros_a_la_izquierda_si_matchea` validates `00001230` and `01230` both match; test `test_token_casi_igual_no_matchea` validates `12300` does NOT match.  
**Resolution**: Merged spec scenario text now reads "...PDF filename whose only numeric token is `12300`" (removed `01230` reference). No code change. Deliberate reconciliation per CLAUDE.md rule 1 (ADR changes are documented, never silent).

### Design D5 — PDF-only re-emit (spec correction folded in at archive)

**Issue**: Original inbox-event-publishing delta was written type-agnostically but design D5 restricts re-emit to PDF only (XML re-emit would create a second fact.Factura via .NET promotion path).  
**Evidence**: Design D5 cites shipped #25 `EsDocumentoAsociado` predicate pinned false for XML, and no uniqueness guard on `fact.Factura` identity.  
**Resolution**: Merged spec requirement already incorporates the PDF-only filter ("When a **PDF** Procesamiento row's `DocumentoAsociadoId` transitions..."). No additional correction needed; delta text was already amended per the final design intent.

## Test Results at Close

| Gate | Command | Result | Status |
|------|---------|--------|--------|
| Unit | `pytest -m "not integracion and not externa and not ocr"` | 285 passed, 28 deselected | ✓ GREEN |
| Unit baseline | (prior to this change) | 266 passed | Established |
| New tests | (this change only) | +19 tests | ✓ All green |
| Ruff (13 files) | `ruff check src/smartnet_worker/... tests/unit/... tests/integration/...` | All checks passed | ✓ PASS |
| Ruff (whole tree) | `ruff check src tests` | 11 E501 pre-existing, unrelated, byte-identical to baseline, 0 in changed files | ✓ CLEAN |
| Integration | `pytest -m integracion` | 27 tests SKIP (2 new written, collectable; blocker: conftest path) | ⏸ DEFERRED (H1) |
| Design proof | Spec scenario coverage | 16/16 scenarios have unit-level runtime tests | ✓ 100% |

## Code Quality Assurances

- **Pure function (ADR 0019)**: `comprobante.py` stays pure — only imports `collections`, `collections.abc`, `dataclasses`, `re`. No pyodbc, pathlib, or IO.
- **Exact-path regression guard**: `comprobante.asociar()` body byte-unchanged (git diff verified). New second pass is separate `asociar_por_nombre_archivo()` function.
- **Idempotency**: Re-emit uses payload-aware `NOT EXISTS` on JSON_VALUE (lax mode), repeated in both candidate query and INSERT WHERE (anti-TOCTOU).
- **Data partition boundary (ADR 0003)**: Re-emit queries touch only fact.Procesamiento (read), fact.DocumentoRecibido (read), fact.InboxEvent (insert) under fact_worker role. No .NET-owned table touched by Python. Verified in unit tests + repo-layer assertions.
- **Schema unchanged**: Zero .sql files, zero schema changes. Existing `fact.InboxEvent` allows multiple rows per ProcesamientoId (PK is identity, no UQ on ProcesamientoId).

## Risks and Mitigations

| Risk | Likelihood | Mitigation | Status |
|------|------------|-----------|--------|
| Mis-association to wrong XML | Low | RUC (11 digits) + número + serie as tokens is strong fingerprint; bilateral 1:1 exclusivity refuses ambiguity; 8 unit tests cover 2-XML and 2-PDF refusal | ✓ MITIGATED |
| Re-emit creates duplicate `InboxEvent` rows | Med | `NOT EXISTS` guard on payload JSON + existing #25 `UQ_DocumentoFactura_DocumentoRecibidoId` (2601 catch) make promotion idempotent | ✓ MITIGATED |
| Stale orphan suppresses valid association | Med | Accepted by owner — fails safe (stays orphan for manual review); ADR 0017 accepts this cost | ✓ ACCEPTED |
| `_extraer_serie_numero` widening over-matches | Low | Constrained to SUNAT-shaped alphanumeric + negative lookahead; 3 unit tests (F96X, F001, prose/garbage) | ✓ GUARDED |
| Integration tests don't run | High | Pre-existing conftest bug (not this change). Tests written, collectable, ready for owner. H1 handoff recorded. | ✓ RECORDED |

## Dependencies and Lineage

- **Lineage**: #19 (campos-contables-editables) → #24 (extraccion-y-asociacion-v2) → #25 (pdf-asociado-en-documento-factura, shipped) → #26 (this change, archived)
- **Depends on**: #25 (shipped) — consumes new PDF↔XML associations; confirmed non-conflicting. #5 (filename sanitization, shipped) — containment check relies on normalized filename.
- **Generates**: BACKLOG item #26 (Factura uniqueness guard) — deferred to owner per H2.
- **No schema migration**: Additive and self-healing. Existing orphan pairs associate on next run; rollback = revert commit (FKs indistinguishable).

## Success Criteria (from proposal)

- [x] The observed pair (`20127765279` / `f96x` / `00001230`) associates in the association pass
- [x] `comprobante` second pass refuses on 2-XML and 2-PDF ambiguity
- [x] `_extraer_serie_numero` parses `F96X` and still rejects non-SUNAT garbage
- [x] A NULL→non-null `DocumentoAsociadoId` transition produces a new `InboxEvent` candidate; #25 then merges idempotently
- [x] ADR 0017 amendment and BACKLOG #26 (draft) recorded; no .NET files touched
- [x] Diff within the 800-line budget; single PR (size:exception approved)

## Commit Guidance (W4 hygiene)

**Branch**: `item-19-campos-contables-editables` (pre-existing)  
**Commit message** (conventional):

```
feat(worker): PDF↔XML association by filename containment + alphanumeric serie (BACKLOG #26, phase 1/3)

- New pure comprobante.asociar_por_nombre_archivo() second pass over residue
  of exact 4-tuple association: XML key authority, bilateral 1:1 exclusivity,
  no type required from filename.
- Widened _extraer_serie_numero regex to SUNAT alphanumeric series (F96X).
- PDF-only InboxEvent re-emit on DocumentoAsociadoId NULL→non-null transition
  (integrates with shipped #25 merge path).
- ADR 0017 amended: §Asociación PDF↔XML, Recuperación para two forms
  (own key; containment against XML authority); Estado → Revisión 3.
- 285 unit tests passed (+19 new). Integration tests written, blocked by
  pre-existing conftest path bug (owner follow-up).

Closes: BACKLOG #26 (implementation; BACKLOG.md and #26 scope drafted for owner).
Dependency: Shipped #25 (pdf-asociado-en-documento-factura).
Lineage: #19 → #24 → #25 → #26.
```

**Files to stage**:
- SmartNet/SmartNetWorker/src/smartnet_worker/pdf_texto.py
- SmartNet/SmartNetWorker/src/smartnet_worker/comprobante.py
- SmartNet/SmartNetWorker/src/smartnet_worker/procesamiento_repo.py
- SmartNet/SmartNetWorker/src/smartnet_worker/cli_procesamiento.py
- SmartNet/SmartNetWorker/src/smartnet_worker/inbox_event_repo.py
- SmartNet/SmartNetWorker/src/smartnet_worker/cli_inbox.py
- SmartNet/SmartNetWorker/tests/unit/test_pdf_texto.py
- SmartNet/SmartNetWorker/tests/unit/test_comprobante.py
- SmartNet/SmartNetWorker/tests/unit/test_procesamiento_repo.py
- SmartNet/SmartNetWorker/tests/unit/test_cli_procesamiento.py
- SmartNet/SmartNetWorker/tests/unit/test_inbox_event_repo.py
- SmartNet/SmartNetWorker/tests/unit/test_cli_inbox.py
- SmartNet/SmartNetWorker/tests/integration/test_pyodbc_integracion.py
- adrs/0017-frontera-del-motor-de-extraccion.md
- openspec/changes/asociacion-pdf-clave-desde-xml/** (now archived)

**NOT to commit** (pre-existing unrelated #25 work):
- SmartNet/SmartNetApi/... (any .NET changes)
- SmartNet/SmartNetWeb/... (SPA changes)

## Archive Closure

This change is **COMPLETE and ARCHIVED**. The SDD cycle is closed:
1. ✓ Proposal — intent and scope approved by owner
2. ✓ Spec — 4 requirements, 16 scenarios defined
3. ✓ Design — 9 architecture decisions documented
4. ✓ Tasks — 27 tasks identified; 23 complete, 4 acknowledged handoffs
5. ✓ Apply — code and unit tests green; integration tests written (blocked H1)
6. ✓ Verify — PASS WITH WARNINGS; 0 CRITICAL, 5 WARNINGS (all documented/handoffs), 2 SUGGESTIONs (owner follow-up)
7. ✓ Archive — specs merged, ADR amended, handoffs recorded

Next step: Owner commits the staged files and executes the handoffs (H1, H2, H3).

---

**Archive created**: 2026-09-01 — Authorized by sdd-archive executor  
**Change closed**: All artifacts retained in openspec/changes/archive/2026-09-01-asociacion-pdf-clave-desde-xml/
