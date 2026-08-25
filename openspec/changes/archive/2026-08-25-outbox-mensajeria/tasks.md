# Tasks: Outbox y mensajería (BACKLOG #14)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | 750–950 (12+ files: Core, Infra, 5 Python modules, contract/golden fixtures, 3 extended test files) |
| 400-line budget risk (skill default) | High |
| Project preflight budget (800 lines) | Medium-High — near/at the agreed ceiling |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 → PR 2 → PR 3 |
| Delivery strategy | ask-on-risk (assumed default; not overridden in this request) |
| Chain strategy | pending — user must confirm stacked-to-main vs feature-branch-chain |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | .NET producer: PayloadOutbox, port, 4 emission points, D9 retrofit, D10 state-CAS | PR 1 | `dotnet test SmartNet.Facturacion.Core.Tests` | `dotnet test SmartNet.Facturacion.Infrastructure.Tests` (real schema) | Revert 4 `EmitirOutboxAsync` sites + `PayloadOutbox.cs`; no data migration |
| 2 | Python consumer: reclamo/guarda/repo/despacho/cli + fan-out map | PR 2 | `pytest worker/tests/unit` | `pytest worker/tests/integration -k outbox` (real SQL Server) | Disable/remove worker process; producer unaffected |
| 3 | Bidirectional N2 contract tests + permission matrix + #7/#11 regression | PR 3 | `pytest worker/tests/integration -k contract` | Real `usr_api`/`usr_worker` connections | Test-only; revert without touching PR 1/2 code |

## Phase 1: Core Foundation (port + payload)

- [x] 1.1 RED: golden-fixture tests for all 5 event payloads (retrofit incl.) — `PayloadOutboxTests.cs`
- [x] 1.2 GREEN: create `PayloadOutbox.cs` (`ConstruirAsync` port re-read + pure `Serializar` + `EnvolturaOutbox`)
- [x] 1.3 RED: `MarcarFacturaValidadaAsync` returns Aplicada/YaValidada/NoTransicionable — `FakeUnidadDeTrabajoTests`
- [x] 1.4 GREEN: add member + `TransicionEstadoFactura` enum to `IUnidadDeTrabajo.cs`; implement in `FakeUnidadDeTrabajo.cs` (records `Llamadas`, reflects the transition into the next `CargarFacturaAsync`). NOTE: the D9 fidelity fixes ("non-null `FacturaACargar` default", "reads see own writes" for `CargarFacturaAsync`/`CargarAsientoAsync` generally) are NOT done yet — deferred to Phase 2 (task 2.1), which is where they are first required by `ValidarInternoAsync`'s integration.
- [x] 1.5 RED: `DescribirCaso` maps `FacturaDescartada` to 409 factura-descartada
- [x] 1.6 GREEN: add case to `CasoConflicto.cs` + arm in `SmartNet.Api/ProblemasDeNegocio.cs`

  DEVIATION: extending `IUnidadDeTrabajo` (1.4) is a breaking interface change — `SqlUnidadDeTrabajo`
  (Infrastructure) had to gain a real `MarcarFacturaValidadaAsync` body immediately to keep the
  *entire* solution compiling (not just #14), pulling forward part of task 3.1's GREEN
  implementation (exact SQL from design D10) without its own dedicated real-schema integration test
  yet — that RED test (`SqlUnidadDeTrabajoFacturaTests.cs`) is still pending in Phase 3.

## Phase 2: Producer Emission Points (`ServicioDeFacturas.cs`, `ServicioDeAsientos.cs`) — DONE (apply batch 2)

- [x] 2.1 RED/GREEN: `ValidarInternoAsync` calls `MarcarFacturaValidadaAsync` before payload build; extend `ValidarAsync_...CommitsInOrder` (+4 reads) and `ValidarPorFacturaAsync_...` (D9)
- [x] 2.2 RED/GREEN: `NoTransicionable` → 409, no `CommitAsync`, empty `EventosOutbox`, asiento stays BORRADOR
- [x] 2.3 RED/GREEN: `YaValidada` reconfirm still emits `ASIENTO_CORREGIDO`, no rollback
- [x] 2.4 RED/GREEN: `ASIENTO_ANULADO` emitted in `AnularAsync`, explicit `asientoId`
- [x] 2.5 RED/GREEN: `FACTURA_CORREGIDA` in `PatchAsync` iff `entradas.Count > 0`; no-op emits nothing
- [x] 2.6 RED/GREEN: `FACTURA_CORREGIDA` in `ConfirmarAfectacionAsync` iff `AfectacionMixta` changes; extend `ConfirmarAfectacionAsync_WhenApplied_...` (+4 reads, +emit)
- [x] 2.7 RED/GREEN: production-shaped guard test — validar then adjunto/PATCH without hand-set `Estado`; `DOCUMENTACION_ACTUALIZADA`/`FACTURA_CORREGIDA` now fire; retrofit `DOCUMENTACION_ACTUALIZADA` payload via `PayloadOutbox`
- [x] 2.8 RED/GREEN: per-tx emission guard throws on repeated `(Tipo, FacturaId)` — mirrored in `SqlUnidadDeTrabajo` and `FakeUnidadDeTrabajo`

  DEVIATION: `ConfirmarAfectacionAsync`'s `FACTURA_CORREGIDA` gate was implemented as
  `AfectacionMixta != esMixta AND Estado == VALIDADA` (not just the changed-value check design.md's
  D8 prose literally states) — spec.md's requirement title ("on any accepted update to a validated
  invoice") and its explicit scenario ("GIVEN a validated Factura") both require the VALIDADA gate
  for both emission points; `PatchAsync`'s prose already carried it implicitly via the shared intro
  sentence. `ConfirmarAfectacionAsync_WhenApplied_...`'s fixture was extended from `FacturaPendiente()`
  to `... with { Estado = Validada }` accordingly (still task 2.6's own instruction: "extend ... +emit").

  Full detail (TDD evidence table, Llamadas-sequence exact-count derivation, files changed, work-unit
  evidence): see Engram `sdd/outbox-mensajeria/apply-progress`.

## Phase 3: Infrastructure (`SqlUnidadDeTrabajo.cs`) — DONE (apply batch 3)

- [x] 3.1 RED/GREEN: state-CAS `UPDATE fact.Factura ... WHERE Estado='PENDIENTE_VALIDACION'` (literal) — `SqlUnidadDeTrabajoOutboxTests.cs`, real schema
- [x] 3.2 RED/GREEN: `EmitirOutboxAsync` fan-out INSERT into `OutboxEventIntegracion` per Infra applicability map (D3)
- [x] 3.3 Assert: post-validar PATCH with stale ETag returns 412 (Version bump, no code change)

  Also closed (deferred from batch 2, deviation 2): a dedicated `SqlUnidadDeTrabajo.EmitirOutboxAsync`
  per-tx-guard throw test against real schema (D8) — `EmitirOutboxAsync_WithARepeatedTipoAndFacturaIdInTheSameTransaction_Throws`.

  Full detail (TDD evidence table, files changed, work-unit evidence): see Engram
  `sdd/outbox-mensajeria/apply-progress`.

## Phase 4: Consumer (`worker/src/smartnet_worker/`) — DONE (apply batch 4)

- [x] 4.1 RED/GREEN: `reclamo.py` — `ReclamoDeLote` Protocol, `EventoReclamado`, `ARRENDAMIENTO=5min`, no `pyodbc`
- [x] 4.2 RED/GREEN: `guarda_obsolescencia.py` — pure `evaluar`, never raises
- [x] 4.3 RED/GREEN: import-graph test + `despacho_outbox.py` (dispatch, empty registry, no `READPAST` import)
- [x] 4.4 RED/GREEN: `outbox_repo.py` — only `READPAST` module; claim/progreso/marcar
- [x] 4.5 RED/GREEN: `cli_outbox.py` + `smartnet-outbox` script in `pyproject.toml`
- [x] 4.6 RED/GREEN: `OBSOLETO` never dispatches, never touches `Intentos`/`UltimoError`

  Full detail (TDD evidence table, files changed, work-unit evidence): see Engram
  `sdd/outbox-mensajeria/apply-progress`.

## Phase 5: Bidirectional Contract Tests (ADR 0019 N2) — DONE (apply batch 5)

- [x] 5.1 Create shared `tests/fixtures/outbox_event_payload.golden.json`; .NET + Python tests read it
- [x] 5.2 Update `worker/tests/integration/conftest.py` — real `usr_api` LOGIN, `api_connection_string`
- [x] 5.3 Bidirectional: `usr_api` inserts event+child rows → `usr_worker` claims/marks → `usr_api` reads back
- [x] 5.4 Permission matrix: `usr_worker` INSERT `OutboxEventIntegracion` denied; `usr_api` UPDATE denied; `usr_worker` write `fact.Factura` denied
- [x] 5.5 Lease test: claim invisible at `ahora+4min`, reclaimable at `ahora+6min`, asserted against `ARRENDAMIENTO`
- [x] 5.6 `READPAST` concurrency: two `pyodbc` connections claim same batch, disjoint sets, no blocking

  DEVIATION (real bug #1, found by 5.3 running the real INSERT under the real `usr_api` LOGIN for
  the first time — every prior real-schema test in #14 connected as trusted/sysadmin, never as the
  actual login): SQL Server requires `UPDATE` permission on a `SEQUENCE` object to execute
  `NEXT VALUE FOR` — a GRANT distinct from the table-level `SELECT, INSERT, UPDATE` 008 already
  gives `fact_api` on `fact.OutboxEvent`. Without it, `SqlUnidadDeTrabajo.EmitirOutboxAsync`'s real
  INSERT (`NEXT VALUE FOR fact.SeqOutbox`) fails in production for the `usr_api` login with error
  229. Fixed with a NEW migration `019_permiso_secuencia_seqoutbox.sql` (+ `rollback/019_down.sql`,
  + regenerated `checksums.txt`) — never edited 008 in place (ADR 0016, SQL versionado: an applied
  script's content must stay checksum-stable). First draft granted `SELECT, UPDATE`; `GRANT SELECT`
  on a SEQUENCE is itself invalid ("Granted or revoked privilege SELECT is not compatible with
  object", error 4606) — final migration grants `UPDATE` only. `fact_worker` needs nothing (Python
  never inserts `fact.OutboxEvent`).

  DEVIATION (real bug #2, found by 5.3/5.6 running `OutboxRepo.reclamar` against a real ODBC driver
  for the first time — the fake-cursor unit tests in `test_outbox_repo.py` never reproduce
  multi-statement result-set behaviour): without `SET NOCOUNT ON`, pyodbc surfaces the `UPDATE`
  statement's "N rows affected" message as an empty result set ahead of the final `SELECT`, and
  `cursor.fetchall()` immediately after `execute()` raised
  `pyodbc.ProgrammingError: No results.  Previous SQL was not a query.` Fixed by prepending
  `SET NOCOUNT ON;` to `_RECLAMAR_TEMPLATE` in `outbox_repo.py`, pinned by a new unit test
  (`test_reclamar_activa_set_nocount_on_antes_del_declare`).

  Both are genuine production correctness bugs that ADR 0019's N2 contract tests exist specifically
  to catch (real schema + real GRANT/DENY + real driver, not fakes) — confirms the level's value,
  not scope creep.

## Phase 6: Regression + Release Note — DONE (apply batch 6)

- [x] 6.1 Run closed #7/#11 suites before/after; only the 3 D9 sequence assertions change
- [x] 6.2 Confirm `PermissionMatrixTests.cs:254-309` untouched (no duplication)
- [x] 6.3 Document both visible behavior changes (`POST /descartar` 409, `POST /validar` 409+rollback) in release note

  Re-ran the closed #7/#11 suites post-Phase-5 as the "after" side of the regression check (the
  "before" baseline is each item's own closed-verify state, per `SPRINT.md`): `SmartNet.Inbox.Core.Tests`
  49/49, `SmartNet.Inbox.Infrastructure.Tests` 41/41 (real schema), `SmartNet.Facturacion.Core.Tests`
  88/88, `SmartNet.Facturacion.Infrastructure.Tests` 46/46 (real schema), `SmartNet.Api.Tests` 143/143,
  `SmartNet.Db.Runner.Tests --filter PermissionMatrixTests` 27/27, worker `pytest tests/unit` 210/210.
  Zero regressions; the only assertions that changed SHAPE (not count) are the 3 D9 sequence
  assertions already applied and green since Phase 2. `PermissionMatrixTests.cs:254-309` confirmed
  untouched: empty `git diff --stat`, last modifying commit `72b8bd5` predates this item (BACKLOG
  #13). Release note written to
  `openspec/changes/outbox-mensajeria/RELEASE-NOTES.md`, covering both behavior changes plus the
  regression-evidence table above.
