# Verification Report — outbox-mensajeria (BACKLOG #14)

**Mode**: Full artifacts (proposal, specs outbox-emision + outbox-consumo, design.md, tasks.md, ADR 0020, RELEASE-NOTES.md)
**Verdict**: **PASS**

## Task Completeness

51/51 tasks checked across 6 phases (tasks.md). No unchecked tasks. Deviations documented inline (D8 gate reading, Phase-2→3 deferral, 2 real production bugs in batch 5) — all resolved, none block.

## Independent Test Execution (run by verifier, not trusted from apply report)

| Suite | Result | Real schema? | Matches apply-progress claim |
|---|---|---|---|
| `SmartNet.Facturacion.Core.Tests` | 88/88 passed | No (fake) | Yes |
| `SmartNet.Facturacion.Infrastructure.Tests` | 46/46 passed | Yes (SQL Server) | Yes |
| `SmartNet.Inbox.Core.Tests` | 49/49 passed | No | Yes |
| `SmartNet.Inbox.Infrastructure.Tests` | 41/41 passed | Yes | Yes |
| `SmartNet.Api.Tests` | 143/143 passed | Yes (integration) | Yes |
| `SmartNet.Db.Runner.Tests --filter PermissionMatrixTests` | 27/27 passed | Yes | Yes |
| worker `pytest tests/unit -q` | 210/210 passed | No | Yes |
| worker `pytest tests/integration -q` | 19 passed, 1 skipped | Yes | Yes |

Zero drift from batch-6's reported counts.

## Spec Compliance Matrix (outbox-emision + outbox-consumo)

1. **5-event catalog, 4 emission points** — VERIFIED in source:
   - `ASIENTO_CORREGIDO` at `ValidarInternoAsync` reconfirm (`ServicioDeFacturas.cs:134-136`, `esReconfirmacion` gate)
   - `ASIENTO_ANULADO` in `AnularAsync` (`ServicioDeAsientos.cs:134-136`, explicit `asientoId`)
   - `FACTURA_CORREGIDA` in `PatchAsync` (`:249-253`, `entradas.Count>0 && Estado==Validada`) and `ConfirmarAfectacionAsync` (`:454-458`, `AfectacionMixta changed && Estado==Validada`)
   - `FACTURA_VALIDADA`/`DOCUMENTACION_ACTUALIZADA` retrofitted onto `PayloadOutbox` envelope
   - All via `IUnidadDeTrabajo.EmitirOutboxAsync`, same transaction, `SeqOutbox` sequence — confirmed by `SqlUnidadDeTrabajo.cs:263-267` (`NEXT VALUE FOR fact.SeqOutbox`).

2. **`MarcarFacturaValidadaAsync` (D10) + NoTransicionable→409 on DESCARTADA** — VERIFIED: `ServicioDeFacturas.cs:126-131` calls the port member before payload build; `NoTransicionable` returns `Conflicto(CasoConflicto.FacturaDescartada, ...)` before `CommitAsync`, so the `await using var uow` rolls back (no asiento CONFIRMADO, no event). State-CAS SQL (`WHERE Estado='PENDIENTE_VALIDACION'`, literal `'VALIDADA'`) confirmed against real schema by `SmartNet.Facturacion.Infrastructure.Tests` (46/46 green).

3. **Per-tx double-emission guard (D8)** — VERIFIED: `SqlUnidadDeTrabajo.cs:45,247-253` — `HashSet<(string Tipo, long FacturaId)> _emitidosEnEstaTx`, throws `InvalidOperationException` on repeat within the same transaction (fail-loud → rollback).

4. **Fan-out to `OutboxEventIntegracion` / DRIVE-SHEETS map (D3)** — VERIFIED: `SqlUnidadDeTrabajo.cs:237-245` — `DestinosAplicables`: `FACTURA_VALIDADA`/`FACTURA_CORREGIDA`/`ASIENTO_CORREGIDO`/`ASIENTO_ANULADO` → `[DRIVE, SHEETS]`; `DOCUMENTACION_ACTUALIZADA` → `[DRIVE]` only — matches ADR 0004's table exactly.

5. **Python consumer** — VERIFIED all 5 new modules present (`reclamo.py`, `outbox_repo.py`, `guarda_obsolescencia.py`, `despacho_outbox.py`, `cli_outbox.py` + tests). `READPAST` isolated to `outbox_repo.py` per design D6. `OBSOLETO` guard confirmed never routed through TRANSITORIO/DIFERIBLE/PERMANENTE (comments reference #17's future classification only — no implementation, correctly out of scope). 5-min lease and 1-min cadence match design D4, verified green in the integration suite.

6. **Bidirectional contract tests (ADR 0019 N2)** — VERIFIED green in `pytest tests/integration` (19 passed incl. permission-matrix rows) and `PermissionMatrixTests` (.NET side, 27/27, confirmed untouched by this item).

7. **2 production bugs fixed in batch 5** — VERIFIED:
   - Migration `019_permiso_secuencia_seqoutbox.sql` exists, `GRANT UPDATE ON OBJECT::fact.SeqOutbox TO fact_api`. `rollback/019_down.sql` exists with matching `REVOKE UPDATE`. `checksums.txt` entry independently recomputed via `sha256sum` and matches exactly (`d8880c71...`). ADR 0016 honored: new migration file, 008 untouched.
   - `SET NOCOUNT ON` fix pinned by a passing unit test and exercised for real by the green `READPAST` concurrency/bidirectional integration tests.

8. **Real test execution, not trusted claims** — DONE: all 8 suites above executed independently in this verify pass.

9. **No dbo.\* / no #15/#16/#17 scope creep** — VERIFIED: `grep -rn "dbo\."` over migration 019 + rollback returns nothing. `git log -1 -- PermissionMatrixTests.cs` = `72b8bd5` (BACKLOG #13, predates this item) and `git diff --stat` against HEAD is empty. No TRANSITORIO/DIFERIBLE/PERMANENTE logic implemented (comments only, forward references to #17).

## Design Coherence

D1–D10 all reflected in code as documented. ADR 0020 revision 2 correctly documents decision 5 (state-CAS) matching design.md D10 1:1. RELEASE-NOTES.md correctly documents both visible behavior changes (`POST /descartar` 409, `POST /validar` 409+rollback) with before/after/who's-affected framing plus regression evidence table.

## Issues

**CRITICAL**: None.

**WARNING**: None.

**SUGGESTION**:
- RDD/`gentle-ai sdd-attempt` interaction was skipped throughout (disabled at repo level) — consistent with project convention, not a defect.
- Open Questions 6 and 7 (design.md) remain explicitly deferred/forward-only by design — correctly out of #14's scope.

## Final Verdict: **PASS**

All 51/51 tasks complete and code-verified. All 5 catalog events emit correctly from all 4 confirmed emission points with self-sufficient payloads. D8 double-emission guard, D10 state-CAS + 409 rollback, and D3 fan-out map are all present and match design/ADR exactly. Both production bugs are genuinely fixed and regression-pinned via ADR 0016-compliant migration 019 with correct checksum. 8 independent test suites re-run by the verifier reproduce the exact green counts reported by sdd-apply, with zero drift. No dbo.* or #15/#16/#17 scope leakage detected. Ready for `sdd-archive`.
