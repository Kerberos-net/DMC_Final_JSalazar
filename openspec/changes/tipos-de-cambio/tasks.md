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

- [ ] 3.1 Scaffold `SmartNet/worker/pyproject.toml` (PEP 621, `requires-python = ">=3.13"`, deps
      `requests`, `beautifulsoup4`, `pyodbc`; dev deps `pytest`, `ruff`; `[tool.pytest.ini_options]`
      markers `integracion`, `externa`) + src layout `src/smartnet_worker/`.
- [ ] 3.2 RED (pytest): `parse_tipo_cambio` unit test against a saved real SBS HTML fixture
      (`tests/fixtures/sbs_tipo_cambio.html`) — returns `TipoCambioSbs` with exact `Decimal` compra/venta;
      malformed/mutilated HTML raises `ParseoSbsError`.
- [ ] 3.3 GREEN: `sbs.py` — `parse_tipo_cambio(html: str) -> TipoCambioSbs`, pure, `beautifulsoup4` on
      `html.parser`, `Decimal(str(...))` never `float`.
- [ ] 3.4 RED (pytest): `insertar_sbs` unit test with a fake cursor recording statement/params — no
      `dbo.` in the emitted SQL; `IntegrityError` on the fake cursor is caught and returns `False`.
- [ ] 3.5 GREEN: `tipo_cambio_repo.py` — `insertar_sbs(cursor, tc) -> bool`, hardcoded `Origen='SBS'`
      (design.md Decision 4, symmetric with .NET's `CargarManualAsync`), catches `IntegrityError`.
- [ ] 3.6 RED (pytest): `registrar_exito`/`registrar_fallo` unit tests with a fake cursor — `UPDATE …
      WHERE Nombre='SBS'` issued; raises if fake cursor reports `rowcount != 1`; `instante` passed as a
      parameter, never `datetime.now()`.
- [ ] 3.7 GREEN: `estado_integracion.py` — `registrar_exito(cursor, instante)`,
      `registrar_fallo(cursor, instante, error)` with `UPDATE`+rowcount guard, `UltimoError` truncated
      to 2000 chars, `FallosSeguidos` incremented on failure (design.md Decision 6).
- [ ] 3.8 GREEN: `cli_tipo_cambio.py` — sole IO entry point; `requests.get` with explicit timeout,
      reads `SMARTNET_WORKER_ODBC_CONNECTION` (no committed default), orchestrates
      parse→insertar_sbs→registrar_exito on success, registrar_fallo on any failure inside its own
      transaction after rollback (no test — thin orchestration wired to already-tested pure units,
      compression acknowledged).
- [ ] 3.9 RED (pytest, marker `integracion`): real `pyodbc` test against an ephemeral
      `CREATE LOGIN usr_worker` — successful run inserts the SBS row for today; duplicate insert for
      the same date returns `False`; `UPDATE` of `EstadoIntegracion` affects exactly 1 row; the
      scraper never issues a `dbo.*` statement (assert via connection-level query log or grant denial).
- [ ] 3.10 GREEN/confirm 3.9 against 3.5/3.7/3.8 wired together; no production code expected to
      change — record if a gap surfaces.
- [ ] 3.11 `SmartNet/worker/README.md` — install steps (`pip install -e .[dev]`), required env var,
      `pytest` marker usage (`-m "not integracion and not externa"` for local unit-only runs), the
      convention item #5 will reuse.
- [ ] 3.12 `ruff check` clean pass over `src/` and `tests/`.

## Phase 4 (WU4): Solution wiring, CI, full integration

- [ ] 4.1 Modify `SmartNet/SmartNet.sln` — add a `tipos-de-cambio` solution folder and the 4 new
      .NET projects, mirroring the `catalogos` folder's GUID/nesting pattern.
- [ ] 4.2 Modify `.github/workflows/ci.yml` — wire `SmartNet.TiposCambio.Core.Tests` and the Python
      unit tests (`pytest -m "not integracion and not externa"`) into `verificaciones-estaticas`;
      wire `SmartNet.TiposCambio.Infrastructure.Tests` into `pruebas-de-base-de-datos`.
- [ ] 4.3 Add new CI job `pruebas-de-worker-python` — its own SQL Server container, ephemeral
      `CREATE LOGIN usr_worker WITH PASSWORD`, runs `pytest -m integracion` (design.md Decision 7);
      confirm `-m externa` stays deselected by default.
- [ ] 4.4 Run the full solution test suite (`dotnet test SmartNet.sln`, sequential per project) —
      confirm no regression in existing project test counts alongside the new
      `TiposCambio.Core.Tests`/`TiposCambio.Infrastructure.Tests` counts.
- [ ] 4.5 Confirm zero orphaned `fact_test_*` databases and zero orphaned ephemeral `usr_worker`
      logins after the full run (standing rule from item #1's Fase 3 incident).
