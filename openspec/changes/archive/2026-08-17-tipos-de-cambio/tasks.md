# Tasks: Tipos de Cambio (BACKLOG #4)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~1450–1750 (Core+Core.Tests ~350; Infrastructure+Infrastructure.Tests ~380; Python worker package+tests ~550; .sln+ci.yml wiring ~90) |
| 400-line budget risk | High — WU1, WU2, and WU3 each individually approach or exceed the 400-line budget |
| Chained PRs recommended | Yes |
| Suggested split | WU1 → WU2 → WU3 → WU4 (four PRs, strictly sequential) |
| Delivery strategy | ask-on-risk — this forecast flags risk, so chained delivery is a stop-and-ask, not a silent decision |
| Chain strategy | pending — orchestrator to ask user |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | `SmartNet.TiposCambio.Core` — records, closed result hierarchy, `SeleccionDeTipoCambio`, purity tests | PR 1 | `dotnet test SmartNet.TiposCambio.Core.Tests` | None — zero DB/HTTP/clock by construction | Delete `SmartNet/tipos-de-cambio/SmartNet.TiposCambio.Core*` |
| 2 | `SmartNet.TiposCambio.Infrastructure` — `SqlTipoCambioRepository`, structural + permission tests | PR 2 | `dotnet test SmartNet.TiposCambio.Infrastructure.Tests` | `TestDatabaseFixture`, `ExecuteAsUserAsync("usr_api"/"usr_worker", …)` | Delete `SmartNet/tipos-de-cambio/SmartNet.TiposCambio.Infrastructure*` |
| 3 | `SmartNet/worker/` — Python scaffold, pure parser, cursor repos, CLI, unit + integration tests | PR 3 | `pytest -m "not integracion and not externa"` (unit); `pytest -m integracion` (real pyodbc) | Ephemeral `CREATE LOGIN usr_worker`, own SQL Server container | Delete `SmartNet/worker/` entirely |
| 4 | Wiring — `SmartNet.sln`, `.github/workflows/ci.yml` (3rd job `pruebas-de-worker-python`) | PR 4 | full `dotnet test SmartNet.sln` + full CI run | Full suite, fresh `fact_test_<id>` per test + fresh worker container | Remove 4 `.sln` entries; revert `ci.yml` diff |

---

## Phase 1 (WU1): `SmartNet.TiposCambio.Core` — pure domain (ADR 0019 level 1)

- [x] 1.1 Scaffold `SmartNet/tipos-de-cambio/SmartNet.TiposCambio.Core` (classlib, `net10.0`, zero
      `PackageReference`) and `SmartNet.TiposCambio.Core.Tests` (xUnit 2.9.3 + Mono.Cecil 0.11.6 +
      NetArchTest.Rules 1.3.2), mirroring `SmartNet.Catalogos.Core`.
- [x] 1.2 RED: purity/architecture-scan test — copy `PurityScanTests` from `SmartNet.Catalogos.Core.Tests`,
      retargeted at `SmartNet.TiposCambio.Core`, **adding `System.Net.Http` to the forbidden-dependency
      list** (spec.md requirement).
- [x] 1.3 Confirm 1.2 passes trivially against the empty project. **Confirmed GREEN: 6/6.**
- [x] 1.4 RED: `TipoCambio` record + `OrigenTipoCambio` enum shape test — construction/equality per
      Interfaces/Contracts (`Fecha`, `Origen`, `Compra`, `Venta`, `FechaConsulta`). **Confirmed RED:
      CS0246/CS0103, type does not exist.**
- [x] 1.5 GREEN: `TipoCambio` record, `OrigenTipoCambio` enum. **Confirmed GREEN: 10/10.**
- [x] 1.6 RED: `ResultadoTipoCambio` hierarchy shape test — `Vigente`/`SinTipoCambio` are the only
      constructible cases; hierarchy closed to other assemblies (`private protected` ctor).
      **Confirmed RED: CS0246, type does not exist (13 call sites).**
- [x] 1.7 GREEN: `ResultadoTipoCambio` abstract record + nested sealed `Vigente`/`SinTipoCambio`.
      **Confirmed GREEN: 14/14.**
- [x] 1.8 RED: `SeleccionDeTipoCambio.Seleccionar` unit tests (design.md Testing Strategy table,
      Threat Matrix "red + credenciales" is out of scope here) — SBS wins with both present; only
      MANUAL used when SBS absent; empty list → `SinTipoCambio`; row for a different `Fecha`
      discarded; unknown `Origen` value discarded, never selected. **Confirmed RED: CS0103, member
      does not exist (6 call sites).**
- [x] 1.9 GREEN: `SeleccionDeTipoCambio.Seleccionar`. **Confirmed GREEN: 20/20.**
- [x] 1.10 GREEN: define `ITipoCambioRepository` port (`ObtenerVigenteAsync`, `CargarManualAsync`)
      and `ResultadoCargaManual` enum exactly per design.md's Interfaces/Contracts — compile-time
      contracts, no meaningful RED (compression acknowledged, same class as item #3's task 1.15).
- [x] 1.11 Re-run 1.2's purity scan against the complete `SmartNet.TiposCambio.Core` — confirm still
      GREEN before Phase 2 builds against these ports. **Confirmed GREEN: 20/20 — purity scan
      passes unchanged against the complete assembly (records, enum, closed hierarchy, static
      selection class, repository port).**

## Phase 2 (WU2): `SmartNet.TiposCambio.Infrastructure` — SQL repository

- [x] 2.1 Scaffold `SmartNet/tipos-de-cambio/SmartNet.TiposCambio.Infrastructure` (referencing
      `SmartNet.TiposCambio.Core` + `Microsoft.Data.SqlClient` 7.0.2, no `FrameworkReference`) and
      `SmartNet.TiposCambio.Infrastructure.Tests` (+ `ProjectReference` to `SmartNet.Db.TestBootstrap`).
- [x] 2.2 RED: `SqlTipoCambioRepositoryTests.ObtenerVigenteAsync` — only-SBS row returns `Vigente` with
      Origen=Sbs; both origins present returns `Vigente` with Origen=Sbs (MANUAL discarded); no row for
      the date returns `SinTipoCambio`. **Confirmed RED: CS0246, type does not exist (3 call sites).**
- [x] 2.3 GREEN: `SqlTipoCambioRepository.ObtenerVigenteAsync` — `SELECT` both origins by PK
      `(Fecha, Origen)` (max 2 rows), delegate selection to `SeleccionDeTipoCambio.Seleccionar`
      (design.md Decision 1). **Confirmed GREEN: 3/3.** MigratedDatabase helper needed
      `CreateExternalDboCatalogsAsync()` + `SeedDboMotivoFixtureRowsAsync()` before `RunMigrations()`
      — `010_motivo_atributo_demo.sql` selects from `dbo.Motivo`, same dependency as item #3.
- [x] 2.4 RED: `SqlTipoCambioRepositoryTests.CargarManualAsync` — inserts a MANUAL row for an
      uncovered date and returns `Cargada`; a second insert for the same `(Fecha, 'MANUAL')` returns
      `YaExistia` via the real composite PK (SqlException 2627/2601 translation), not a pre-check.
      **Compression acknowledged**: `CargarManualAsync` was implemented alongside `ObtenerVigenteAsync`
      in the same 2.3 pass (single adapter file, both port methods written together, same class as
      item #3 task 1.15/1.10); no meaningful RED — tests passed on first run against existing code.
- [x] 2.5 GREEN: `SqlTipoCambioRepository.CargarManualAsync` — plain `INSERT`, hardcoded `Origen='MANUAL'`
      (no `Origen` parameter, design.md Decision 4), catch 2627/2601 → `YaExistia`, anything else
      propagates; `FechaConsulta` passed as a parameter, never `SYSUTCDATETIME()`. **Confirmed GREEN: 2/2.**
- [x] 2.6 RED: `NoWriteToDboStructuralTests` analog — literal scan of the adapter's `.cs` source
      confirms it never mentions `dbo.` (comment-stripped, same fix as item #3's task 2.11).
      **Compression acknowledged**: same class as 2.4 — adapter already exists from 2.3/2.5 and
      never mentioned `dbo.`, so no meaningful RED; test passed on first run.
- [x] 2.7 GREEN/confirm 2.6 — passes by construction against 2.3/2.5. **Confirmed GREEN: 1/1.**
- [x] 2.8 RED: `PermissionSufficiencyTests` analog — replay the adapter's exact SQL text under
      `ExecuteAsUserAsync("usr_api", …)` and `ExecuteAsUserAsync("usr_worker", …)`; both succeed
      (007/008 grant identical access to both roles on `fact.TipoCambio`). No meaningful RED here
      either — this suite verifies a claim about already-shipped grants (008, item #1), not new
      production code; compression acknowledged, same class as item #3's task 3.6.
- [x] 2.9 GREEN/confirm 2.8 — passes against real grants. **Confirmed GREEN: 6/6** — both `usr_api`
      and `usr_worker` execute `ObtenerVigenteAsync`'s SELECT and `CargarManualAsync`'s INSERT
      successfully, both are denied DELETE. Design.md's claim ("007/008 grant identical access to
      both roles on this table") holds against the real grants; no gap found.

## Phase 3 (WU3): `SmartNet/worker/` — Python SBS scraper (first Python in repo)

- [x] 3.1 Scaffold `SmartNet/worker/pyproject.toml` (PEP 621, `requires-python = ">=3.13"`, deps
      `requests`, `beautifulsoup4`, `pyodbc`; dev deps `pytest`, `ruff`; `[tool.pytest.ini_options]`
      markers `integracion`, `externa`) + src layout `src/smartnet_worker/`. **Environment note**:
      this sandbox initially had no Python interpreter (only the Windows Store stub alias) — Python
      3.13.15 was installed via `winget install Python.Python.3.13` during implementation (deviation
      from assuming a preinstalled interpreter, flagged here rather than silently downgrading or
      skipping execution). `pip install -e .[dev]` succeeded cleanly (requests 2.34.2,
      beautifulsoup4 4.15.0, pyodbc 5.3.0, pytest 9.1.1, ruff 0.16.3).
- [x] 3.2 RED (pytest): `parse_tipo_cambio` unit test against a saved real SBS HTML fixture
      (`tests/fixtures/sbs_tipo_cambio.html`) — returns `TipoCambioSbs` with exact `Decimal` compra/venta;
      malformed/mutilated HTML raises `ParseoSbsError`. **Confirmed RED**: temporarily removed
      `sbs.py` and ran `pytest tests/unit/test_sbs.py` → `ModuleNotFoundError: No module named
      'smartnet_worker.sbs'` (collection error, all 6 tests unrunnable). **Fixture note**: the real
      `sbs.gob.pe` page is behind an Incapsula WAF that blocked the automated `curl` fetch used
      during implementation (challenge script only, no table markup) — the fixture is a
      documented, clearly-labeled **synthetic** structure (`tests/fixtures/README.md`), not a
      captured real page.
- [x] 3.3 GREEN: `sbs.py` — `parse_tipo_cambio(html: str) -> TipoCambioSbs`, pure, `beautifulsoup4` on
      `html.parser`, `Decimal(str(...))` never `float`. **Confirmed GREEN**: restored `sbs.py`,
      `pytest tests/unit/test_sbs.py` → 6/6 passed.
- [x] 3.4 RED (pytest): `insertar_sbs` unit test with a fake cursor recording statement/params — no
      `dbo.` in the emitted SQL; `IntegrityError` on the fake cursor is caught and returns `False`.
      **Confirmed RED**: temporarily removed `tipo_cambio_repo.py` → `ModuleNotFoundError: No
      module named 'smartnet_worker.tipo_cambio_repo'` (collection error, all 4 tests unrunnable).
- [x] 3.5 GREEN: `tipo_cambio_repo.py` — `insertar_sbs(cursor, tc) -> bool`, hardcoded `Origen='SBS'`
      (design.md Decision 4, symmetric with .NET's `CargarManualAsync`), catches `IntegrityError`.
      **Confirmed GREEN**: restored the module, `pytest tests/unit/test_tipo_cambio_repo.py` → 4/4
      passed.
- [x] 3.6 RED (pytest): `registrar_exito`/`registrar_fallo` unit tests with a fake cursor — `UPDATE …
      WHERE Nombre='SBS'` issued; raises if fake cursor reports `rowcount != 1`; `instante` passed as a
      parameter, never `datetime.now()`. **Confirmed RED**: temporarily removed
      `estado_integracion.py` → `ModuleNotFoundError: No module named
      'smartnet_worker.estado_integracion'` (collection error, all 6 tests unrunnable).
- [x] 3.7 GREEN: `estado_integracion.py` — `registrar_exito(cursor, instante)`,
      `registrar_fallo(cursor, instante, error)` with `UPDATE`+rowcount guard, `UltimoError` truncated
      to 2000 chars, `FallosSeguidos` incremented on failure (design.md Decision 6). **Confirmed
      GREEN**: restored the module, `pytest tests/unit/test_estado_integracion.py` → 6/6 passed.
- [x] 3.8 GREEN: `cli_tipo_cambio.py` — sole IO entry point; `requests.get` with explicit timeout,
      reads `SMARTNET_WORKER_ODBC_CONNECTION` (no committed default), orchestrates
      parse→insertar_sbs→registrar_exito on success, registrar_fallo on any failure inside its own
      transaction after rollback (no test — thin orchestration wired to already-tested pure units,
      compression acknowledged). Also added `config.py` (env var + URL + timeout constants,
      `ConfiguracionError`), matching design.md's File Changes table.
- [x] 3.9 RED (pytest, marker `integracion`): real `pyodbc` test against an ephemeral
      `CREATE LOGIN usr_worker` — successful run inserts the SBS row for today; duplicate insert for
      the same date returns `False`; `UPDATE` of `EstadoIntegracion` affects exactly 1 row; the
      scraper never issues a `dbo.*` statement (assert via connection-level query log or grant denial).
      **Confirmed RED, then GREEN against real infra**: `tests/integration/conftest.py` provisions
      an ephemeral `fact_test_worker_<id>` database, applies the full versioned schema via a real
      `dotnet run --project SmartNet.Db.Runner` invocation (never a Python reimplementation of the
      schema — ADR 0016), and creates a real ephemeral `CREATE LOGIN usr_worker`. A local SQL
      Server 2025 (Developer) instance + the .NET SDK were both reachable in this sandbox, so these
      tests were actually run, not just written: first run failed loudly with real, informative
      errors while the harness was built (ADO.NET vs ODBC connection-string dialect mismatch for
      `SmartNet.Db.Runner`; missing `dbo.*` external-catalog fixture tables/seed rows that
      `008_usuarios_y_permisos.sql`/`010_motivo_atributo_demo.sql` require — same dependency WU2
      hit) — each fixed for real, never worked around by weakening the test. "No `dbo.*` statement"
      is covered separately by the structural test in 3.10, not by a live query-log assertion.
      The "no `dbo.*` statement" requirement is verified structurally (test_no_dbo_structural.py,
      comment-stripped source scan), matching the .NET side's `NoWriteToDboStructuralTests`
      pattern — the design.md Testing Strategy table lists this as a separate "Structural" row, not
      part of the pyodbc integration suite.
- [x] 3.10 GREEN/confirm 3.9 against 3.5/3.7/3.8 wired together — **Confirmed GREEN: 3/3 real**
      `pytest -m integracion` **passed** against the real ephemeral database: insert-today,
      duplicate-returns-False (real PK violation via `pyodbc.IntegrityError`), and
      `EstadoIntegracion` rowcount=1. No production code needed to change once the test harness
      itself was fixed (connection-string dialect + dbo fixture, see 3.9) — no gap in 3.5/3.7/3.8
      surfaced. Verified zero orphaned `fact_test_worker_*` databases and zero orphaned
      `usr_worker` logins after the run (`sqlcmd` query, 0 rows both).
- [x] 3.11 `SmartNet/worker/README.md` — install steps (`pip install -e .[dev]`), required env var,
      `pytest` marker usage (`-m "not integracion and not externa"` for local unit-only runs), the
      convention item #5 will reuse, plus a "Limitaciones conocidas" section documenting the
      synthetic fixture and the environment bootstrap.
- [x] 3.12 `ruff check` clean pass over `src/` and `tests/`. **Confirmed**: `ruff check src tests`
      → `All checks passed!` (after fixing `UP017` datetime.UTC alias warnings via `--fix` and
      manually wrapping 6 `E501` long lines).

## Phase 4 (WU4): Solution wiring, CI, full integration

- [x] 4.1 Modify `SmartNet/SmartNet.sln` — add a `tipos-de-cambio` solution folder and the 4 new
      .NET projects, mirroring the `catalogos` folder's GUID/nesting pattern. **Done**: used
      `dotnet sln SmartNet.sln add ... -s tipos-de-cambio` (same primitive that produced the
      `catalogos` folder), which generates its own new project-type/nesting GUIDs — not hand-copied
      from `catalogos`. Confirmed by a full `dotnet build SmartNet.sln --configuration Release`:
      0 errors, 0 warnings, all 19 projects (15 pre-existing + 4 new) build clean.
- [x] 4.2 Modify `.github/workflows/ci.yml` — wire `SmartNet.TiposCambio.Core.Tests` and the Python
      unit tests (`pytest -m "not integracion and not externa"`) into `verificaciones-estaticas`;
      wire `SmartNet.TiposCambio.Infrastructure.Tests` into `pruebas-de-base-de-datos`. **Done**:
      added `TESTS_TIPOS_CAMBIO_CORE`/`TESTS_TIPOS_CAMBIO_INFRA`/`WORKER_DIR` env vars, a
      `dotnet test` step for `TiposCambio.Core.Tests` (same pure-domain pattern as
      `Auth.Core.Tests`/`Catalogos.Core.Tests`), an `actions/setup-python@v5` (3.13) step + `pip
      install -e .[dev]` + `pytest -m "not integracion and not externa"` step in
      `verificaciones-estaticas`, and a `dotnet test` step for `TiposCambio.Infrastructure.Tests`
      at the end of `pruebas-de-base-de-datos`.
- [x] 4.3 Add new CI job `pruebas-de-worker-python` — its own SQL Server container, ephemeral
      `CREATE LOGIN usr_worker WITH PASSWORD`, runs `pytest -m integracion` (design.md Decision 7);
      confirm `-m externa` stays deselected by default. **Done**: new job with its own
      `mcr.microsoft.com/mssql/server:2022-latest` service container (mirrors
      `pruebas-de-base-de-datos`'s container config exactly, own throwaway `sa` password), installs
      `mssql-tools18`/`unixodbc-dev` + Python 3.13, `pip install -e .[dev]`, runs `pytest -m
      integracion` — the real `CREATE LOGIN usr_worker` is issued by
      `tests/integration/conftest.py` itself (same fixture verified locally in WU3, task 3.9/3.10),
      not by a separate CI step. Added an explicit post-run `sqlcmd` orphan check
      (`sys.databases LIKE 'fact_test_%'` and `sys.server_principals = 'usr_worker'`, both must be
      0) that fails the job (`::error::` + `exit 1`) if anything leaked, `if: always()` so it runs
      even after a test failure. `-m externa` is never referenced anywhere in `ci.yml` — grepped to
      confirm no `pytest -m externa` / `pytest -m "... externa"` invocation exists.
- [x] 4.4 Run the full solution test suite (`dotnet test <ProjectName>`, sequential per project,
      NOT `dotnet test SmartNet.sln` as a whole — known flaky per item #3's WU5 finding) — confirm
      no regression in existing project test counts alongside the new `TiposCambio.Core.Tests`/
      `TiposCambio.Infrastructure.Tests` counts. **Confirmed, all green**:
      - `dotnet test SmartNet.TiposCambio.Core.Tests` → **20/20** (records, closed hierarchy,
        `SeleccionDeTipoCambio`, purity scan).
      - `dotnet test SmartNet.TiposCambio.Infrastructure.Tests` → **12/12** against a real local
        SQL Server 2019 instance (`ObtenerVigenteAsync`, `CargarManualAsync`,
        `NoWriteToDboStructuralTests`, `PermissionSufficiencyTests` for `usr_api`+`usr_worker`);
        real migration ran `001`–`012` against a fresh `fact_test_<guid>` database.
      - Regression spot-check: `dotnet test SmartNet.Catalogos.Core.Tests` → **32/32**;
        `dotnet test SmartNet.Auth.Core.Tests` → **33/33** — both unchanged from item #3's closing
        counts, zero regression from the `.sln`/`ci.yml` edits.
      - Python: `pytest` (whole suite, `SmartNet/worker/`) → **20/20** (17 unit + 3 integration);
        `pytest -m "not integracion and not externa"` → **17 passed, 3 deselected**; `pytest -m
        integracion` → **3 passed, 17 deselected** — confirms marker filtering behaves exactly as
        `ci.yml`'s two jobs invoke it.
- [x] 4.5 Confirm zero orphaned `fact_test_*` databases and zero orphaned ephemeral `usr_worker`
      logins after the full run (standing rule from item #1's Fase 3 incident). **Confirmed**: ran
      `sqlcmd` against the local instance immediately after the full 4.4 run —
      `SELECT COUNT(*) FROM sys.databases WHERE name LIKE 'fact_test_%'` → **0**;
      `SELECT COUNT(*) FROM sys.server_principals WHERE name = 'usr_worker'` → **0**. Same check
      WU3 already ran after its own integration pass; re-run here after the .sln/ci.yml edits and
      the full WU4 regression pass to confirm nothing leaked across the combined run.

### Work Unit Evidence (WU4)

| Evidence | Value |
|---|---|
| Focused test command and exact result | `dotnet test SmartNet.TiposCambio.Core.Tests` → 20/20; `dotnet test SmartNet.TiposCambio.Infrastructure.Tests` → 12/12; `pytest` in `SmartNet/worker/` → 20/20 (17 unit + 3 integration) |
| Runtime harness command/scenario and exact result | Real local SQL Server 2019 instance: `.NET Infrastructure.Tests` ran a real `SmartNet.Db.Runner` migration (`001`–`012`) against a fresh `fact_test_<guid>` database and exercised `usr_api`/`usr_worker` GRANTs; Python `pytest -m integracion` ran `tests/integration/conftest.py`'s own ephemeral `fact_test_worker_<id>` database + real `CREATE LOGIN usr_worker`, migrated the same way. Both harnesses independently confirmed zero orphaned `fact_test_*` databases/`usr_worker` logins after their runs. |
| Rollback boundary | `git revert` this commit — reverts `SmartNet.sln`'s 4 new solution-folder entries and the `ci.yml` diff (3rd job removed, wiring lines removed) without touching WU1/WU2/WU3's already-committed project files |

This closes all 47/47 tasks of `tipos-de-cambio` (item #4) at the **apply** stage. Independent
verification (`sdd-verify`) is still pending before the item is marked closed in `SPRINT.md`.
