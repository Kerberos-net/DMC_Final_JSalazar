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

- [ ] 1.1 Scaffold `SmartNet/catalogos/SmartNet.Catalogos.Core` (classlib, `net10.0`, zero
      `PackageReference`) and `SmartNet/catalogos/SmartNet.Catalogos.Core.Tests` (xUnit + `Mono.Cecil`
      0.11.6 + `NetArchTest.Rules` 1.3.2, mirroring `SmartNet.Auth.Core.Tests`).
- [ ] 1.2 RED: purity/architecture-scan test — copy `PurityScanTests` from `SmartNet.Auth.Core.Tests`
      literally, retargeted at `SmartNet.Catalogos.Core` (5 facts: 3 NetArchTest not-have-dependency
      assertions against `System.Data.SqlClient`/`Microsoft.Data.SqlClient`/`Microsoft.AspNetCore`,
      plus the Cecil assembly-reference scan, plus the IL `DateTime.Now`/`UtcNow` call-site scan).
- [ ] 1.3 Confirm 1.2 passes trivially against the empty project (nothing to violate yet) —
      same empty-baseline discipline as item #2's task 2.3.
- [ ] 1.4 RED: `CuentaContable` record shape test — construction/equality, `EsHojaImputable =>
      Nivel is null`.
- [ ] 1.5 GREEN: `CuentaContable` record (design.md's exact shape: `Cuenta`, `Descripcion`, `Nivel`,
      `CtaReflejaCodigo`, `CtaPuenteCodigo`).
- [ ] 1.6 RED: `ParsearPrefijos` tests — comma-split, trim, discard empty tokens, ordinal dedup;
      `null`/`""` input → empty list.
- [ ] 1.7 GREEN: `ResolucionDePrefijos.ParsearPrefijos`.
- [ ] 1.8 RED: `ResolverCandidatas` unit tests (in-memory plan, no DB) — leaf vs hierarchy-node
      exclusion, multi-prefix union without duplicates, overlapping prefixes, deterministic ordinal
      ascending order, `null`/no-match prefix → empty result, a `cuenta` value with `NULL` `Nivel`
      handling.
- [ ] 1.9 GREEN: `ResolucionDePrefijos.ResolverCandidatas` — `StartsWith` ordinal matching over the
      full flat chart, filtering leaves internally (design.md Decision 1).
- [ ] 1.10 RED: `EsCandidata` tests — true for a leaf matching a declared prefix, false for a
      hierarchy node or non-matching leaf.
- [ ] 1.11 GREEN: `ResolucionDePrefijos.EsCandidata`.
- [ ] 1.12 GATE CHECK: confirm task 0.2/0.3 closed with matching counts (or an explicit
      user-answered discrepancy) before writing golden tests below.
- [ ] 1.13 RED: golden tests against `SmartNet/db/fixtures/data/CuentaContable.csv` as a linked
      test-project resource (pure, no DB) — REGLAS.md §3's 5 worked examples: motivo 22→1, 48→6,
      6→20, 70→34, 8→22 candidates, using the exact prefix strings and counts confirmed in Phase 0.
- [ ] 1.14 GREEN/confirm 1.13 against the implementation from 1.7/1.9 — no further production code
      expected; record if any gap surfaces.
- [ ] 1.15 GREEN: define the 8 repository port interfaces exactly per design.md's Interfaces/
      Contracts table (`ICuentaContableRepository`, `IMotivoRepository`, `IProveedorRepository`,
      `IOrigenRepository`, `IDocumentoIdentidadRepository`, `IProveedorAtributoRepository`,
      `IMotivoAtributoRepository`, `ISugerenciaCuentaRepository`) — compile-time contracts, no
      meaningful RED for an interface declaration; compression acknowledged explicitly, same class
      as item #2's task 2.16.
- [ ] 1.16 Re-run 1.2's purity scan against the complete `SmartNet.Catalogos.Core` (all of
      1.4–1.15) — confirm still GREEN before Phase 2 starts building against these ports.

## Phase 2: `SmartNet.Catalogos.Infrastructure` — external catalog adapters (read-only)

- [ ] 2.1 Scaffold `SmartNet/catalogos/SmartNet.Catalogos.Infrastructure` (classlib, referencing
      `SmartNet.Catalogos.Core` + `Microsoft.Data.SqlClient` 7.0.2) and
      `SmartNet/catalogos/SmartNet.Catalogos.Infrastructure.Tests` (+ `ProjectReference` to
      `SmartNet.Db.TestBootstrap`).
- [ ] 2.2 Write a local seed helper (in the test project, using the already-public
      `ExecuteNonQueryAsync`, per design.md Decision 3) that populates `CuentaContable`, `Proveedor`,
      `Origen`, `DocumentoIdentidad` in `dbo.*` — `TestDatabaseFixture.CreateExternalDboCatalogsAsync`
      leaves these 4 empty and only seeds `dbo.Motivo`.
- [ ] 2.3 RED: `SqlCuentaContableRepository` tests — `ListarPlanCompletoAsync` maps every column
      including `Nivel`/`CtaReflejaCodigo`/`CtaPuenteCodigo`; `ObtenerAsync(cuenta)` returns the row
      for an existing code and an empty/absent result for a missing one, no exception.
- [ ] 2.4 GREEN: `SqlCuentaContableRepository`.
- [ ] 2.5 RED: `SqlMotivoRepository` tests — `ObtenerAsync(codigo)`, `ListarAsync`, empty-collection
      behavior on zero rows.
- [ ] 2.6 GREEN: `SqlMotivoRepository`.
- [ ] 2.7 RED: `SqlProveedorRepository` tests — `ObtenerPorCodigoAsync`; `BuscarPorRucAsync` returns
      a list (not a single row) since `rucpro` is non-unique, seeded with two providers sharing one
      RUC to exercise `IX_Proveedor_Ruc`.
- [ ] 2.8 GREEN: `SqlProveedorRepository`.
- [ ] 2.9 RED: `SqlOrigenRepository`/`SqlDocumentoIdentidadRepository` tests — `ListarAsync` returns
      the seeded 13/6 rows, empty-collection behavior on zero rows.
- [ ] 2.10 GREEN: `SqlOrigenRepository`, `SqlDocumentoIdentidadRepository`.
- [ ] 2.11 RED: structural test — none of the 5 external-catalog interfaces/adapters declares or
      issues `INSERT`/`UPDATE`/`DELETE` against any `dbo.*` table (spec's "No SQL adapter writes to
      a dbo.* table" scenario) — reflection over interface members plus a literal scan of each
      adapter's SQL command text.
- [ ] 2.12 GREEN/confirm 2.11 — passes by construction against 2.4–2.10; record confirmation.

## Phase 3: `SmartNet.Catalogos.Infrastructure` — satellite adapters (read/write) + permissions

- [ ] 3.1 RED: `SqlProveedorAtributoRepository` tests — `ObtenerAsync` returns empty/absent for an
      unseeded code, no exception; `GuardarAsync` upserts a new `EsRelacionada` value for a
      `ProveedorCodigo` not yet present.
- [ ] 3.2 GREEN: `SqlProveedorAtributoRepository`.
- [ ] 3.3 RED: `SqlMotivoAtributoRepository` tests — `ObtenerAsync`, `ListarAsync`, `GuardarAsync`
      upsert; confirm `activo`/`origen '02'` filtering stays out of the adapter's SQL (design.md:
      filtered in Core, not SQL) — adapter returns raw rows only.
- [ ] 3.4 GREEN: `SqlMotivoAtributoRepository`.
- [ ] 3.5 RED: `SqlSugerenciaCuentaRepository` read tests —
      `ListarPorProveedorYMotivoAsync`/`ListarPorMotivoAsync`/`ListarPorProveedorAsync` return raw
      rows, no ranking/sorting by frequency.
- [ ] 3.6 GREEN: the three list methods on `SqlSugerenciaCuentaRepository`.
- [ ] 3.7 RED: `RegistrarUsoAsync` tests — inserts a new row the first time for a
      (`ProveedorCodigo`,`Motivo`,`CuentaCodigo`) combination; increments `Veces` and updates
      `UltimoUso` on the second call for the same combination, leaving other rows untouched; the
      instant is passed as a parameter, never computed with `SYSUTCDATETIME()` inside the adapter.
- [ ] 3.8 GREEN: `RegistrarUsoAsync` — single `UPDATE … IF @@ROWCOUNT = 0 INSERT …` statement per
      design.md.
- [ ] 3.9 RED: structural test — `ISugerenciaCuentaRepository` has no method that ranks, sorts, or
      selects one preferred candidate (spec's "no single best suggestion" scenario) — reflection over
      interface members.
- [ ] 3.10 GREEN/confirm 3.9 — passes by construction; record confirmation.
- [ ] 3.11 RED: `PermissionSufficiencyTests` — replay every SQL statement from the 3 satellite
      adapters (2.x's 5 external adapters are read-only `SELECT`s, already covered by existing
      `fact_api`/`fact_worker` catalog grants from item #1) through
      `ExecuteAsUserAsync("usr_api", …)`, confirming each succeeds under `usr_api`'s real grants;
      confirm `usr_worker` remains denied write access to the 3 satellites.
- [ ] 3.12 Confirm 3.11 is GREEN with no schema changes. If a statement fails under real grants,
      record the gap explicitly — item #1's `fact_api` grants on the 3 satellites are the only
      source of truth; do not relax the test to route around a real gap.

## Phase 4: Solution wiring, CI, and full integration

- [ ] 4.1 Modify `SmartNet/SmartNet.sln` — add a `catalogos` solution folder and the 4 new projects
      (`SmartNet.Catalogos.Core`, `.Core.Tests`, `.Infrastructure`, `.Infrastructure.Tests`).
- [ ] 4.2 Modify `.github/workflows/ci.yml` — wire `SmartNet.Catalogos.Core.Tests` into the
      `verificaciones-estaticas` (no-DB) job and `SmartNet.Catalogos.Infrastructure.Tests` into the
      `pruebas-de-base-de-datos` job, mirroring item #2's WU7 wiring for `Auth.Core.Tests`/
      `Auth.Infrastructure.Tests`.
- [ ] 4.3 Run the full solution test suite (`dotnet test SmartNet.sln`) — confirm no regression in
      the existing 6 projects' test counts (104 `Db.Runner` extended to 127, 33 `Auth.Core`, 41
      `Auth.Infrastructure`, 22 `Api`, 17 `Admin`) alongside the new `Catalogos.Core.Tests`/
      `Catalogos.Infrastructure.Tests` counts.
- [ ] 4.4 Confirm `master`/`BDSmartNet` have zero orphaned `fact_test_*` databases after the full
      run, per the standing rule from item #1's Fase 3 incident — every test uses
      `TestDatabaseFixture`, never a direct connection.
