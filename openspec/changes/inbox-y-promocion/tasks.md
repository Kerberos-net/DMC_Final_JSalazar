# Tasks: Inbox y promoción (BACKLOG #7)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~2700–3100 (WU1 Python producer ~370; WU2 `Inbox.Core` ~630; WU3 `Inbox.Infrastructure` ~650; WU4 API wiring+contract tests ~230; WU5 Angular workspace bootstrap+Inbox screen ~800+; WU6 ADR doc fix ~40) |
| 400-line budget risk | High — WU2, WU3 and WU5 each individually exceed the 400-line budget; WU5 alone bootstraps a whole SPA workspace |
| Chained PRs recommended | Yes |
| Suggested split | WU1 → WU2 → WU3 → WU4 → WU5 → WU6 (six PRs, strictly sequential; WU5 may itself need sub-splitting once scaffolded) |
| Delivery strategy | ask-on-risk — this forecast flags risk, so chained delivery is a stop-and-ask, not a silent decision |
| Chain strategy | pending — orchestrator to ask user |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

**Two unresolved design deltas gate WU2 and WU6** — do not silently pick a side (CLAUDE.md rule 1):
1. Design D5 computes **5** indicators (`EsReferenciaExterna` stays DDL default `0`), contradicting
   the proposal's/spec's "6 indicator flags" and ADR 0005's current prose. Design flags this as an
   open question, not decided. WU2's indicator task and WU6's ADR fix are blocked on confirming D5
   before implementing — implement per design's structural reasoning (`DatosExtraidos` has no
   reference-nota columns) unless the user overrides.
2. Design D4 drops `confianza` from `evidencia[]` (only `fuente`, uniform per event), narrowing
   proposal Q2. WU1's payload builder and WU4's contract-test golden are blocked on the same
   confirmation.

**WU5 (Angular) is itself flagged in design.md as likely needing its own further split** — no
`package.json`/SPA folder/frontend CI job exists yet; bootstrapping the workspace plus the screen in
one PR is far past budget on its own.

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | Python producer: `payload_inbox.py`, `inbox_event_repo.py`, `cli_inbox.py`, pyproject script | PR 1 | `pytest tests/unit/test_payload_inbox.py tests/unit/test_inbox_event_repo.py tests/unit/test_cli_inbox.py -q` | `pytest -m integracion` real `fact_worker` login, ephemeral DB | Delete `payload_inbox.py`, `inbox_event_repo.py`, `cli_inbox.py`, their tests; revert `pyproject.toml` |
| 2 | `SmartNet.Inbox.Core` — records, `PoliticaDePromocion`, `CalculoDeIndicadores`, `ConstruccionDeFactura`, ports, `PurityScanTests` | PR 2 | `dotnet test SmartNet.Inbox.Core.Tests` | None — zero DB/HTTP/clock by construction | Delete `SmartNet/inbox/SmartNet.Inbox.Core*` |
| 3 | `SmartNet.Inbox.Infrastructure` — parser, 3 `Sql*Repository`, `PromocionBackgroundService`, integration tests | PR 3 | `dotnet test SmartNet.Inbox.Infrastructure.Tests` | `TestDatabaseFixture`, real `usr_api` login, ephemeral `fact_test_<id>` | Delete `SmartNet/inbox/SmartNet.Inbox.Infrastructure*` |
| 4 | `BandejaEndpoints.cs`, `Program.cs` wiring, `.sln`/`ci.yml`, contract-test golden | PR 4 | `dotnet test` (`ContractTests` filter) + `pytest tests/unit/test_payload_inbox_contract.py` | `WebApplicationFactory`, real cookie auth | Remove hosted-service registration + endpoint file; revert `.sln`/`ci.yml` diffs |
| 5 | Angular workspace bootstrap + Inbox route/component/service | PR 5 (may split further once scaffolded) | `ng test` (Inbox suite) | `ng e2e`/manual `GET /api/bandeja` smoke, N/A if no e2e harness chosen yet | Delete `SmartNet/spa/`; revert any root `package.json`/CI frontend job |
| 6 | ADR 0005 prose correction (`Tipo`, indicator count) | PR 6 | N/A — doc-only | N/A | Revert the ADR diff |

---

## Phase 1 (WU1): Python producer

- [x] 1.1 RED: `SmartNet/worker/tests/unit/test_payload_inbox.py` — payload shape per design's JSON
      example (`version`, `estadoProcesamiento`, `documento`, `comprobante`, `evidencia[]` with
      `fuente` only per D4 (confirmed), `afectacionMixta`, `camposNoExtraidos`,
      `advertenciasAsociacion`); pure function, no DB.
- [x] 1.2 Confirm RED: `pytest tests/unit/test_payload_inbox.py -q` fails on collection.
- [x] 1.3 GREEN: create `SmartNet/worker/src/smartnet_worker/payload_inbox.py` — pure payload
      builder over already-committed `Procesamiento`/`DatosExtraidos` data passed in.
- [x] 1.4 Confirm GREEN: `pytest tests/unit/test_payload_inbox.py -q` passes.
- [x] 1.5 RED: `tests/unit/test_inbox_event_repo.py` — `listar_no_notificados(cursor)` (rows lacking
      an `InboxEvent`), `insertar_evento(cursor, procesamiento_id, payload)` as one atomic
      `INSERT … SELECT … WHERE NOT EXISTS` (design D3) — exact SQL text, `fact_worker`-scoped.
- [x] 1.6 Confirm RED: fails on collection (`ModuleNotFoundError`).
- [x] 1.7 GREEN: create `SmartNet/worker/src/smartnet_worker/inbox_event_repo.py`.
- [x] 1.8 Confirm GREEN: `pytest tests/unit/test_inbox_event_repo.py -q` passes.
- [x] 1.9 RED: `tests/unit/test_cli_inbox.py` — one cycle: read un-notified → build payload → INSERT
      → commit, per-row isolation (one failure doesn't abort the batch); Tipo is always
      `PROCESAMIENTO_FINALIZADO`, never a second literal.
- [x] 1.10 Confirm RED, then GREEN: create `SmartNet/worker/src/smartnet_worker/cli_inbox.py`.
- [x] 1.11 Modify `SmartNet/worker/pyproject.toml` — register `smartnet-inbox` console script.
- [x] 1.12 Integration test (marker `integracion`): re-running the scan does not duplicate events;
      insert runs under `usr_worker` and denies `fact.Factura` writes (data-partition boundary).

## Phase 2 (WU2): `SmartNet.Inbox.Core` — pure domain

- [x] 2.1 Scaffold `SmartNet/inbox/SmartNet.Inbox.Core` (classlib, zero `PackageReference`) and
      `SmartNet/inbox/SmartNet.Inbox.Core.Tests` (xUnit + Cecil + NetArchTest), mirroring
      `SmartNet.Auth.Core.Tests`.
- [x] 2.2 RED then GREEN: copy `PurityScanTests`, retargeted at `SmartNet.Inbox.Core`; confirm green
      against the empty project.
- [x] 2.3 RED: `EventoInbox`, `ComprobanteExtraido`, `EvidenciaCampo` record-shape tests per design's
      Interfaces/Contracts and the confirmed payload shape.
- [x] 2.4 GREEN: the three records.
- [x] 2.5 RED: `PoliticaDePromocion.Decidir` — sufficiency = the four `NOT NULL` `Factura` columns
      (`TipoComprobante`, `TotalOrig`, `Moneda`, `FechaEmision`) present + `Procesamiento.Estado='COMPLETADO'`
      (design D1); `Numero`/`RucProveedor` absence does NOT block; REGLAS.md §1-4 values are never
      weighed (spec scenario "Structural check does not weigh REGLAS.md business rules").
- [x] 2.6 Confirm RED, then GREEN: `PoliticaDePromocion.Decidir`.
- [x] 2.7 RED: `CalculoDeIndicadores.Calcular` — indicator count per confirmed D5 decision (5,
      `EsReferenciaExterna` fixed `0`, unless user overrides to 6); 3-state `AfectacionMixta`.
- [x] 2.8 Confirm RED, then GREEN: `CalculoDeIndicadores.Calcular`.
- [x] 2.9 RED then GREEN: `ConstruccionDeFactura.Construir` — builds `FacturaPromovida` from
      `EventoInbox` + `proveedorCodigo` + `IndicadoresFactura`; `FechaEnDomingo` derives from
      `FechaEmision` only, never a clock.
- [x] 2.10 GREEN: define ports `IEventoInboxRepository`, `IPromocionRepository`,
      `IBandejaRepository` exactly per design's Interfaces/Contracts.
- [x] 2.11 Re-run `PurityScanTests` against the complete Core assembly — confirm still green before
      Phase 3 builds against these ports.

## Phase 3 (WU3): `SmartNet.Inbox.Infrastructure`

- [ ] 3.1 Scaffold `SmartNet/inbox/SmartNet.Inbox.Infrastructure` (+ `.Tests`, referencing
      `SmartNet.Inbox.Core` + `Microsoft.Data.SqlClient`).
- [ ] 3.2 RED then GREEN: `PayloadInboxParser` — deserializes `InboxEvent.Payload` JSON into
      `EventoInbox`; JSON parsing lives only here, never in Core (design D9).
- [ ] 3.3 RED: `SqlEventoInboxRepository` — reads `EstadoConsumo='PENDIENTE'`, updates to
      `PROMOVIDO`/`DESCARTADO`; never reads `Procesamiento` (data-partition boundary).
- [ ] 3.4 Confirm RED, then GREEN: `SqlEventoInboxRepository`.
- [ ] 3.5 RED: `SqlPromocionRepository` — one `SqlTransaction`: INSERT `Factura`
      (`PENDIENTE_VALIDACION`) + `FacturaExtraccion` rows + indicators; on `UQ_Factura_Procesamiento`
      violation (SQL 2601/2627), catch and resolve existing `FacturaId`, mark `PROMOVIDO` (design D2)
      — never `SELECT`-before-`INSERT`.
- [ ] 3.6 Confirm RED, then GREEN: `SqlPromocionRepository`, including the idempotent-catch path.
- [ ] 3.7 RED then GREEN: `SqlBandejaRepository` — backs `GET /api/bandeja?estado=&orden=` (reuse
      ADR 0008 contract, design D6): filter by `EstadoConsumo`, sort by fecha.
- [ ] 3.8 Create `PromocionBackgroundService` — `BackgroundService` + `PeriodicTimer(1 min)` with
      injected `TimeProvider` (design D7); writes no `fact.EstadoIntegracion` row (design D8).
- [ ] 3.9 Integration tests (`TestDatabaseFixture`): double promotion of the same event → exactly 1
      `Factura`; insufficient payload → 0 `Factura` rows + `DESCARTADO` + `MotivoDescarte`; `usr_api`
      denied on `Procesamiento`; `usr_worker` denied on `Factura`.
- [ ] 3.10 `NoWriteToDboStructuralTests`/`PermissionSufficiencyTests` — confirm no adapter touches
      `dbo.*` or a worker-private table.

## Phase 4 (WU4): API wiring + contract tests

- [ ] 4.1 Create `SmartNet/api/SmartNet.Api/BandejaEndpoints.cs` — thin `GET /api/bandeja`,
      `.RequireAuthorization()`, delegates to `IBandejaRepository`.
- [ ] 4.2 Modify `SmartNet/api/SmartNet.Api/Program.cs` — register the three repos +
      `AddHostedService<PromocionBackgroundService>()`.
- [ ] 4.3 Modify `SmartNet.sln` (add `inbox` solution folder + 4 new projects) and `ci.yml` (Core →
      `verificaciones-estaticas`, Infrastructure → `pruebas-de-base-de-datos`).
- [ ] 4.4 Shared golden JSON fixture (contract test, ADR 0019 L2) — one payload example, confirmed
      D4/D5 shape.
- [ ] 4.5 Python contract test: `payload_inbox.py`'s builder output matches the golden JSON shape.
- [ ] 4.6 .NET contract test: `PayloadInboxParser` parses the golden JSON into the expected
      `EventoInbox`.
- [ ] 4.7 E2E (`WebApplicationFactory`): `GET /api/bandeja` returns promoted + discarded rows,
      filtered/sorted; 401 without cookie.
- [ ] 4.8 Run full solution test suite; confirm zero orphaned `fact_test_*` databases after the run.

## Phase 5 (WU5): Angular Inbox screen

- [ ] 5.1 Bootstrap the Angular workspace under `SmartNet/spa/` (signals, no state library) — decide
      and record scope with the user first per design's Open Question (may need its own further PR
      split once real file counts are known).
- [ ] 5.2 Add a frontend CI job (lint + `ng test`) to `.github/workflows/ci.yml`.
- [ ] 5.3 `InboxService` (signals) — `GET /api/bandeja?estado=&orden=`.
- [ ] 5.4 `InboxListComponent` — renders outcome (`PROMOVIDO`/`DESCARTADO`/`PENDIENTE`), linked
      `Factura` summary, indicator chips (count per confirmed D5); discard shows `MotivoDescarte`;
      no approve/edit/re-trigger controls rendered.
- [ ] 5.5 Filter control by `EstadoConsumo`.
- [ ] 5.6 Sort control by fecha (asc/desc).
- [ ] 5.7 Component tests covering each spec scenario (promoted summary, discarded reason, filter,
      sort, no manual-action controls, signal-driven state).

## Phase 6 (WU6): Docs

- [ ] 6.1 Modify `docs/adr/0005-*.md` — correct `Tipo` prose to the single as-built CHECK value;
      correct indicator count to the confirmed decision (5 per D5, or 6 if the user overrides).
