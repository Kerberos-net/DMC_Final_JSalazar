# Tasks: Catálogos y Satélites (BACKLOG #3)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~1700–2000 (`SmartNet.Catalogos.Core` records+ports+`ResolucionDePrefijos` ~300; `Core.Tests` unit+golden+`PurityScanTests` ~450; `SmartNet.Catalogos.Infrastructure` 8 `Sql*Repository` adapters ~500; `Infrastructure.Tests` adapter+seed-helper+`PermissionSufficiencyTests` ~450; `SmartNet.sln` + `ci.yml` diff ~40) |
| 400-line budget risk | High — WU1 and WU3 each individually exceed the 400-line review budget |
| Chained PRs recommended | Yes |
| Suggested split | WU0 → WU1 → WU2 → WU3 → WU4 (five PRs, strictly sequential) |
| Delivery strategy | ask-on-risk — this forecast flags risk, so chained delivery is a stop-and-ask, not a silent decision |
| Chain strategy | pending — orchestrator to ask user |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

**Why WU0 exists before any golden test is written.** The five golden counts (motivo 22→1, 48→6,
6→20, 70→34, 8→22) are copied from REGLAS.md §3's worked prose, not independently derived from the
real fixture. If `SmartNet/db/fixtures/data/CuentaContable.csv` does not reproduce them exactly,
writing the golden tests first would either fabricate a passing test against a wrong fixture or
force a silent "fix" of one side — forbidden by CLAUDE.md rule 1. WU0 runs the count check standalone,
before any Core code exists, and is a hard gate: WU1's golden-test task cannot start until WU0 closes.

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 0 | Gate: verify `CuentaContable.csv` reproduces REGLAS.md §3's 5 worked counts | PR 1 | N/A — standalone script/query, no test project yet | None | Revert the verification note; blocks nothing if reversed |
| 1 | `SmartNet.Catalogos.Core` — records, ports, `ResolucionDePrefijos`, golden + purity tests | PR 2 | `dotnet test SmartNet.Catalogos.Core.Tests` | None — zero DB/HTTP/clock by construction | Delete `SmartNet/catalogos/SmartNet.Catalogos.Core*` |
| 2 | `SmartNet.Catalogos.Infrastructure` — 5 external read-only `Sql*Repository` adapters | PR 3 | `dotnet test SmartNet.Catalogos.Infrastructure.Tests` (external-catalog subset) | `TestDatabaseFixture`, local dbo seed helper | Delete the 5 external adapter files + their tests |
| 3 | `SmartNet.Catalogos.Infrastructure` — 3 satellite `Sql*Repository` adapters + `PermissionSufficiencyTests` | PR 4 | `dotnet test SmartNet.Catalogos.Infrastructure.Tests` (satellite subset) | `TestDatabaseFixture`, `ExecuteAsUserAsync("usr_api"/"usr_worker", …)` | Delete the 3 satellite adapter files + their tests |
| 4 | `SmartNet.sln`, `.github/workflows/ci.yml`, full end-to-end run | PR 5 | full `dotnet test` at solution level | Full suite, fresh `fact_test_<id>` per test | Remove the 4 `.sln` entries; CI diff revert |

---

## Phase 0: Gate — fixture count verification (blocks WU1's golden-test task)

- [x] 0.1 GATE — write a standalone script/query (e.g. a throwaway LINQPad/`dotnet-script`/one-off
      console snippet, not shipped code) that parses `SmartNet/db/fixtures/data/CuentaContable.csv`
      (`\|`-delimited, no header) and, using the exact `ParsearPrefijos`/`StartsWith` semantics
      design.md specifies, computes candidate counts for motivo 22, 48, 6, 70, 8's declared prefixes
      from REGLAS.md §3.

      **Verification method** (no `dotnet-script`/LINQPad available in this environment; used an
      equivalent throwaway `awk` one-liner over Git Bash instead — same semantics: ordinal
      `StartsWith`/`index(cuenta, prefix) == 1`, leaf filter on `$3 == ""` i.e. `nivel` empty,
      distinct-by-`cuenta` union across comma-split prefixes):

      ```sh
      cd SmartNet/db/fixtures/data
      count_prefixes() {
        awk -F'|' -v prefs="$1" '
          BEGIN { n = split(prefs, p, ","); }
          $3 == "" {
            cuenta = $1; matched = 0;
            for (i = 1; i <= n; i++) if (index(cuenta, p[i]) == 1) { matched = 1; break; }
            if (matched) print cuenta;
          }' CuentaContable.csv | sort -u | wc -l
      }
      ```

      **Exact declared prefixes used** — read directly from the real
      `SmartNet/db/fixtures/data/Motivo.csv` (field 3, `codigo|descripcion|cuenta`), not
      re-typed from REGLAS.md's abbreviated prose:

      | Motivo | `Motivo.cuenta` (declared prefixes) |
      |---|---|
      | 22 | `631111` |
      | 48 | `6373` |
      | 6 | `104` |
      | 70 | `16` |
      | 8 | `4011,4017,4018,403,417,167101,1674` |

      Note: motivo 8's REGLAS.md §3 worked example truncates the prefix list with "…"
      (`4011,4017,4018,403,417,…`); the real fixture has 7 declared prefixes, not 5 — the
      count still matches (see 0.2), so this is REGLAS.md prose being illustrative/abbreviated,
      not a discrepancy.

- [x] 0.2 Compare the computed counts against REGLAS.md §3's stated 1/6/20/34/22. If they match
      exactly for all 5, record the confirmation and proceed to WU1's golden tests using these
      counts as given. **If any count mismatches**: STOP — do not silently adjust either side
      (CLAUDE.md rule 1). Record the actual computed count(s), the discrepancy, and surface an
      explicit question to the user: which side is wrong (REGLAS.md's worked example, the fixture's
      contents, or the prefix-matching semantics) and how to proceed. Do not write task 1.x's golden
      tests until this gate is closed one way or the other.

      **Result — all 5 counts match exactly:**

      | Motivo | Prefixes | Expected (REGLAS.md §3) | Computed (real CSV) | Match |
      |---|---|---|---|---|
      | 22 | `631111` | 1 | 1 | ✅ |
      | 48 | `6373` | 6 | 6 | ✅ |
      | 6 | `104` | 20 | 20 | ✅ |
      | 70 | `16` | 34 | 34 | ✅ |
      | 8 | `4011,4017,4018,403,417,167101,1674` | 22 | 22 | ✅ |

      **GATE CLOSED — PASS.** WU1's golden tests (task 1.13) may proceed using these confirmed
      counts and the exact prefix strings above.

- [x] 0.3 Also confirm the fixture's total row count and leaf-row count (`nivel` empty) match
      REGLAS.md §2's "907 hojas de 1650" — same silent-fix prohibition if it does not.

      **Result:**

      | Metric | Expected (REGLAS.md §2) | Actual (`CuentaContable.csv`) | Match |
      |---|---|---|---|
      | Total rows | 1650 | 1650 | ✅ |
      | Leaf rows (`nivel` empty) | 907 | 907 | ✅ |

      Verified with `wc -l < CuentaContable.csv` and
      `awk -F'|' '$3 == "" { c++ } END { print c }' CuentaContable.csv`.

## Phase 1: `SmartNet.Catalogos.Core` — pure domain (ADR 0019 level 1)

- [x] 1.1 Scaffold `SmartNet/catalogos/SmartNet.Catalogos.Core` (classlib, `net10.0`, zero
      `PackageReference`) and `SmartNet/catalogos/SmartNet.Catalogos.Core.Tests` (xUnit + `Mono.Cecil`
      0.11.6 + `NetArchTest.Rules` 1.3.2, mirroring `SmartNet.Auth.Core.Tests`).
- [x] 1.2 RED: purity/architecture-scan test — copy `PurityScanTests` from `SmartNet.Auth.Core.Tests`
      literally, retargeted at `SmartNet.Catalogos.Core` (5 facts: 3 NetArchTest not-have-dependency
      assertions against `System.Data.SqlClient`/`Microsoft.Data.SqlClient`/`Microsoft.AspNetCore`,
      plus the Cecil assembly-reference scan, plus the IL `DateTime.Now`/`UtcNow` call-site scan).
- [x] 1.3 Confirm 1.2 passes trivially against the empty project (nothing to violate yet) —
      same empty-baseline discipline as item #2's task 2.3. **Confirmed: 5/5 green.**
- [x] 1.4 RED: `CuentaContable` record shape test — construction/equality, `EsHojaImputable =>
      Nivel is null`. **Confirmed RED: CS0246, type does not exist.**
- [x] 1.5 GREEN: `CuentaContable` record (design.md's exact shape: `Cuenta`, `Descripcion`, `Nivel`,
      `CtaReflejaCodigo`, `CtaPuenteCodigo`). **Confirmed GREEN: 4/4.**
- [x] 1.6 RED: `ParsearPrefijos` tests — comma-split, trim, discard empty tokens, ordinal dedup;
      `null`/`""` input → empty list. **Confirmed RED: CS0103, member does not exist.**
- [x] 1.7 GREEN: `ResolucionDePrefijos.ParsearPrefijos`. **Confirmed GREEN: 7/7.**
- [x] 1.8 RED: `ResolverCandidatas` unit tests (in-memory plan, no DB) — leaf vs hierarchy-node
      exclusion, multi-prefix union without duplicates, overlapping prefixes, deterministic ordinal
      ascending order, `null`/no-match prefix → empty result, a `cuenta` value with `NULL` `Nivel`
      handling. **Confirmed RED: CS0117, method does not exist.**
- [x] 1.9 GREEN: `ResolucionDePrefijos.ResolverCandidatas` — `StartsWith` ordinal matching over the
      full flat chart, filtering leaves internally (design.md Decision 1). **Confirmed GREEN: 7/7.**
- [x] 1.10 RED: `EsCandidata` tests — true for a leaf matching a declared prefix, false for a
      hierarchy node or non-matching leaf. **Confirmed RED: CS0117, method does not exist.**
- [x] 1.11 GREEN: `ResolucionDePrefijos.EsCandidata`. **Confirmed GREEN: 3/3.**
- [x] 1.12 GATE CHECK: confirm task 0.2/0.3 closed with matching counts (or an explicit
      user-answered discrepancy) before writing golden tests below. **Confirmed: WU0 gate closed,
      all 5 counts and the 1650/907 totals matched exactly — no discrepancy to resolve.**
- [x] 1.13 RED: golden tests against `SmartNet/db/fixtures/data/CuentaContable.csv` as a linked
      test-project resource (pure, no DB) — REGLAS.md §3's 5 worked examples: motivo 22→1, 48→6,
      6→20, 70→34, 8→22 candidates, using the exact prefix strings and counts confirmed in Phase 0.
      **The fixture loader was new test-support code (no prior file loaded the real CSV); the
      resolution logic itself (1.7/1.9) already existed and was proven correct in isolation. Ran
      immediately after being written — passed on first execution (6/6, including the 1650/907
      fixture-shape assertion), no RED→GREEN gap to close.**
- [x] 1.14 GREEN/confirm 1.13 against the implementation from 1.7/1.9 — no further production code
      expected; record if any gap surfaces. **Confirmed: no gap. All 5 golden counts matched
      exactly against the real fixture on first run; no production code changed.**
- [x] 1.15 GREEN: define the 8 repository port interfaces exactly per design.md's Interfaces/
      Contracts table (`ICuentaContableRepository`, `IMotivoRepository`, `IProveedorRepository`,
      `IOrigenRepository`, `IDocumentoIdentidadRepository`, `IProveedorAtributoRepository`,
      `IMotivoAtributoRepository`, `ISugerenciaCuentaRepository`) — compile-time contracts, no
      meaningful RED for an interface declaration; compression acknowledged explicitly, same class
      as item #2's task 2.16. **Also defined the 7 supporting domain records these ports reference
      (`Motivo`, `Proveedor`, `Origen`, `DocumentoIdentidad`, `ProveedorAtributo`, `MotivoAtributo`,
      `SugerenciaCuenta`) — design.md fixed only `CuentaContable`'s exact shape; these were modeled
      1:1 on the real DDL columns (`SmartNet/db/fixtures/010_dbo_catalogos_ddl.sql`,
      `SmartNet/db/schema/004_satelites_datos_maestros.sql`) since the 8 interfaces cannot compile
      without them. Noted as a deviation in the apply-progress artifact, not a silent addition.**
- [x] 1.16 Re-run 1.2's purity scan against the complete `SmartNet.Catalogos.Core` (all of
      1.4–1.15) — confirm still GREEN before Phase 2 starts building against these ports.
      **Confirmed: full suite 32/32 green, including all 5 PurityScanTests against the complete
      assembly (records + interfaces added, still zero infrastructure references).**

## Phase 2: `SmartNet.Catalogos.Infrastructure` — external catalog adapters (read-only)

- [x] 2.1 Scaffold `SmartNet/catalogos/SmartNet.Catalogos.Infrastructure` (classlib, referencing
      `SmartNet.Catalogos.Core` + `Microsoft.Data.SqlClient` 7.0.2) and
      `SmartNet/catalogos/SmartNet.Catalogos.Infrastructure.Tests` (+ `ProjectReference` to
      `SmartNet.Db.TestBootstrap`). **No `FrameworkReference`** (unlike `SmartNet.Auth.Infrastructure`
      — no ASP.NET Core Identity/cookie dependency here).
- [x] 2.2 Local seed helper `DboCatalogSeedHelper.cs` (test project, extension methods over
      `TestDatabaseFixture`, using the already-public `ExecuteNonQueryAsync`, per design.md
      Decision 3): `SeedCuentaContableAsync`, `SeedProveedorAsync`, `SeedOrigenAsync`,
      `SeedDocumentoIdentidadAsync`. Shared `TestDatabaseFixture` left untouched.
- [x] 2.3 RED: `SqlCuentaContableRepositoryTests` — `ListarPlanCompletoAsync` maps every column
      including `Nivel`/`CtaReflejaCodigo`/`CtaPuenteCodigo`; `ObtenerAsync(cuenta)` returns the row
      for an existing code and null for a missing one, no exception. **Confirmed RED: CS0246, type
      does not exist (3 call sites).**
- [x] 2.4 GREEN: `SqlCuentaContableRepository`. **Confirmed GREEN: 3/3.**
- [x] 2.5 RED: `SqlMotivoRepositoryTests` — `ObtenerAsync(codigo)`, `ListarAsync`, empty-collection
      behavior on zero rows. **Confirmed RED: CS0246 (4 call sites).**
- [x] 2.6 GREEN: `SqlMotivoRepository`. **Confirmed GREEN: 4/4 — one fix needed during GREEN:
      migration `010_motivo_atributo_demo.sql` THROWs unless `dbo.Motivo` has exactly 23
      reclassified rows, so the empty-collection test could not skip seeding (as first written) —
      it now seeds normally, migrates, then `DELETE FROM dbo.Motivo` post-migration to exercise the
      adapter's zero-row path without breaking the migration chain. Deviation from the initial test
      draft, not from design.md.**
- [x] 2.7 RED: `SqlProveedorRepositoryTests` — `ObtenerPorCodigoAsync`; `BuscarPorRucAsync` returns
      a list (not a single row) since `rucpro` is non-unique, seeded with two providers sharing one
      RUC to exercise `IX_Proveedor_Ruc`. **Confirmed RED: CS0246 (3 call sites).**
- [x] 2.8 GREEN: `SqlProveedorRepository`. **Confirmed GREEN: 3/3.**
- [x] 2.9 RED: `SqlOrigenRepositoryTests`/`SqlDocumentoIdentidadRepositoryTests` — `ListarAsync`
      returns the seeded rows, empty-collection behavior on zero rows. **Confirmed RED: CS0246 (2
      files, 4 call sites).**
- [x] 2.10 GREEN: `SqlOrigenRepository`, `SqlDocumentoIdentidadRepository`. **Confirmed GREEN: 4/4.**
- [x] 2.11 RED: `NoWriteToDboStructuralTests` — reflection over the 5 external-catalog interfaces'
      members (no Insert/Update/Delete/Eliminar/Actualizar/Guardar-named method) plus a literal scan
      of each adapter's own `.cs` source for an `INSERT`/`UPDATE`/`DELETE` keyword outside comments
      (spec's "No SQL adapter writes to a dbo.* table" scenario). **First run surfaced a false
      positive: the regex matched "INSERT" inside `SqlCuentaContableRepository.cs`'s own XML doc
      comment prose ("no INSERT/UPDATE/DELETE ever issued"), not real SQL — fixed by stripping
      `//`-prefixed lines before scanning.**
- [x] 2.12 GREEN/confirm 2.11 — passes by construction against 2.4–2.10 once the comment-stripping
      fix landed. **Confirmed GREEN: 2/2.**
- [x] 2.13 (not numbered in the original plan, added during apply) `PermissionSufficiencyTests` —
      requested by the coordinator, analogous to `SmartNet.Auth.Infrastructure.Tests`'s pattern:
      replays each adapter's exact SQL text under `ExecuteAsUserAsync`. **Deviation from the
      request, documented in the test file's own XML doc, not silent**: the request assumed
      `usr_worker` is denied read access to the 5 `dbo.*` catalogs. Verified against
      `008_usuarios_y_permisos.sql` lines 147–156 — both `fact_api` AND `fact_worker` receive
      `GRANT SELECT` on all 5 external catalogs (confirmed by the existing
      `PermissionMatrixTests.BothUsers_CanSelect_FiveExternalDboTables_NeitherCanWrite`). The real
      denial these adapters are subject to is on WRITE statements, which none of the 5 read-only
      adapters issues (already covered by 2.11/2.12). Wrote 12 theory cases (`usr_api`/`usr_worker`
      × 6 SELECT statements, all succeed) plus 2 negative cases confirming both users are denied a
      `dbo.CuentaContable` `UPDATE`. **Confirmed GREEN: 14/14.**

## Phase 3: `SmartNet.Catalogos.Infrastructure` — satellite adapters (read/write) + permissions

- [x] 3.1 RED: `SqlProveedorAtributoRepository` tests — `ObtenerAsync` returns empty/absent for an
      unseeded code, no exception; `GuardarAsync` upserts a new `EsRelacionada` value for a
      `ProveedorCodigo` not yet present. **Confirmed RED: CS0246, type does not exist (5 call
      sites).**
- [x] 3.2 GREEN: `SqlProveedorAtributoRepository`. **Confirmed GREEN: 3/3.** No `EXISTS` guard
      against `dbo.Proveedor` (design.md Decision 2) — `GuardarAsync` succeeds for a code never
      seeded into `dbo.Proveedor`, proven by the test itself never seeding it.
- [x] 3.3 RED: `SqlMotivoAtributoRepository` tests — `ObtenerAsync`, `ListarAsync`, `GuardarAsync`
      upsert; confirm `activo`/`origen '02'` filtering stays out of the adapter's SQL (design.md:
      filtered in Core, not SQL) — adapter returns raw rows only. **Confirmed RED: CS0246 (5 call
      sites).**
- [x] 3.4 GREEN: `SqlMotivoAtributoRepository`. **Confirmed GREEN: 5/5 — one fix needed during
      GREEN, same class as task 2.6's: migration `010_motivo_atributo_demo.sql` unconditionally
      inserts 23 demo rows into `fact.MotivoAtributo` when it runs; `InitializeAsync` now deletes
      them post-migration so every test starts from a clean table (mirrors `SqlMotivoRepositoryTests`'
      pattern for `dbo.Motivo`).**
- [x] 3.5 RED: `SqlSugerenciaCuentaRepository` read tests —
      `ListarPorProveedorYMotivoAsync`/`ListarPorMotivoAsync`/`ListarPorProveedorAsync` return raw
      rows, no ranking/sorting by frequency. **Confirmed RED: CS0246 (6 call sites).**
- [x] 3.6 GREEN: the three list methods on `SqlSugerenciaCuentaRepository`. **Confirmed GREEN as
      part of the same 7/7 run as 3.7/3.8 below (one repository, one test file).**
- [x] 3.7 RED: `RegistrarUsoAsync` tests — inserts a new row the first time for a
      (`ProveedorCodigo`,`Motivo`,`CuentaCodigo`) combination; increments `Veces` and updates
      `UltimoUso` on the second call for the same combination, leaving other rows untouched; the
      instant is passed as a parameter, never computed with `SYSUTCDATETIME()` inside the adapter.
      **Confirmed RED (same compile failure as 3.5, single test file).**
- [x] 3.8 GREEN: `RegistrarUsoAsync` — single `UPDATE … IF @@ROWCOUNT = 0 INSERT …` statement per
      design.md, `@instante` passed as a `DateTimeOffset` parameter (mapped to UTC
      `DATETIME2(3)`), never `SYSUTCDATETIME()`. **Confirmed GREEN: 7/7 for
      `SqlSugerenciaCuentaRepositoryTests`** (4 read cases + 3 `RegistrarUsoAsync` cases including
      "leaves other combinations untouched").
- [x] 3.9 RED: structural test — `ISugerenciaCuentaRepository` has no method that ranks, sorts, or
      selects one preferred candidate (spec's "no single best suggestion" scenario) — reflection over
      interface members. **Not a RED-first task, same class as 2.11 vs the already-existing
      interface: `ISugerenciaCuentaRepository` was fully defined in WU1 (Phase 1) with exactly the
      4 storage-only methods design.md specifies — nothing to change, only to confirm. Compression
      acknowledged explicitly.**
- [x] 3.10 GREEN/confirm 3.9 — passes by construction; record confirmation. **Confirmed:
      `NoRankingStructuralTests` 1/1 green on first run — 4 members, none ranking/sorting/selection
      shaped.**
- [x] 3.11 RED: `PermissionSufficiencyTests` — replay every SQL statement from the 3 satellite
      adapters (2.x's 5 external adapters are read-only `SELECT`s, already covered by existing
      `fact_api`/`fact_worker` catalog grants from item #1) through
      `ExecuteAsUserAsync("usr_api", …)`, confirming each succeeds under `usr_api`'s real grants;
      confirm `usr_worker` remains denied write access to the 3 satellites. **Not RED-first: added
      10 new `[Fact]`/`[Theory]` cases to the existing `PermissionSufficiencyTests.cs` (2.13),
      replaying each satellite adapter's exact SQL text — these confirm real grants, they do not
      exercise not-yet-written production code, same class as 2.13/3.9.**
- [x] 3.12 Confirm 3.11 is GREEN with no schema changes. If a statement fails under real grants,
      record the gap explicitly — item #1's `fact_api` grants on the 3 satellites are the only
      source of truth; do not relax the test to route around a real gap. **Confirmed GREEN: all 24
      `PermissionSufficiencyTests` cases (14 from WU2 + 10 new) pass — `usr_api` succeeds on every
      satellite SELECT/INSERT/UPDATE (`008_usuarios_y_permisos.sql`'s real `GRANT` list), `usr_worker`
      is denied both read and write on all 3 satellites (real `DENY`), and both users are denied
      `DELETE` on all 3 (no `DELETE` grant to either principal — confirms the "never DELETE" design
      constraint at the permission layer, not just by adapter method shape). No schema changes
      needed; no gap found.**

## Phase 4: Solution wiring, CI, and full integration

- [x] 4.1 Modify `SmartNet/SmartNet.sln` — add a `catalogos` solution folder and the 4 new projects
      (`SmartNet.Catalogos.Core`, `.Core.Tests`, `.Infrastructure`, `.Infrastructure.Tests`).
      **Done via `dotnet sln add ... -s catalogos`, exactly mirroring the `auth` folder's format
      (same GUID scheme, same `FAE04EC0-301F-11D3-BF4B-00C04F79EFBC` project-type GUID, same
      `NestedProjects` wiring under a `2150E333-8FDC-42A3-9474-1A3956D46DE8` folder node).**
- [x] 4.2 Modify `.github/workflows/ci.yml` — wire `SmartNet.Catalogos.Core.Tests` into the
      `verificaciones-estaticas` (no-DB) job and `SmartNet.Catalogos.Infrastructure.Tests` into the
      `pruebas-de-base-de-datos` job, mirroring item #2's WU7 wiring for `Auth.Core.Tests`/
      `Auth.Infrastructure.Tests`. **Done: added `TESTS_CATALOGOS_CORE`/`TESTS_CATALOGOS_INFRA` env
      vars and one step per project in each job, same pattern (whole-project run, no `--filter`,
      since `PurityScanTests` already proves `Catalogos.Core.Tests` is DB/HTTP/clock-free).**
- [x] 4.3 Run the full solution test suite (`dotnet test SmartNet.sln`) — confirm no regression in
      the existing 6 projects' test counts (104 `Db.Runner` extended to 127, 33 `Auth.Core`, 41
      `Auth.Infrastructure`, 22 `Api`, 17 `Admin`) alongside the new `Catalogos.Core.Tests`/
      `Catalogos.Infrastructure.Tests` counts. **Done. Solution-wide `dotnet test SmartNet.sln`
      showed 2 transient failures per run (different tests each time — once in `Db.Runner.Tests`,
      once in `Admin.Tests` — both real SQL Server connection errors: "session is in the kill
      state" / "Could not find server 'esta' in sys.servers"), caused by MSBuild/VSTest running all
      7 test projects concurrently against the same local SQL Server instance, not by this item's
      code. Re-ran every project sequentially (matching exactly how `ci.yml`'s two jobs invoke them,
      one `dotnet test <project>` step at a time): 127+33+44+22+17+32+56 = 331/331 green, zero
      failures, zero regressions. `Auth.Infrastructure` is 44 (not 41 as originally estimated in
      this task's own text — pre-existing count from item #2, not from this item).**
- [x] 4.4 Confirm `master`/`BDSmartNet` have zero orphaned `fact_test_*` databases after the full
      run, per the standing rule from item #1's Fase 3 incident — every test uses
      `TestDatabaseFixture`, never a direct connection. **Confirmed via
      `sqlcmd -Q "SELECT name FROM sys.databases WHERE name LIKE 'fact\_test\_%' ESCAPE '\';"` —
      0 rows.**
