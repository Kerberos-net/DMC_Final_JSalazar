# Tasks: Sugerencia de cuenta (BACKLOG #9)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~420-480 (2 new csproj, 4 record files, 1 cascade class, 1 orchestrator, ~6 test files, sln edit) |
| 400-line budget risk | Medium |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 → PR 2 |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending (user decision required) |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: Medium

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | Scaffolding + contracts + pure `CascadaDeSugerencia` (`SugerirCuenta`/`SugerirMotivo`) fully TDD-covered | PR 1 | `dotnet test SmartNet/sugerencia/SmartNet.Sugerencia.Core.Tests --filter FullyQualifiedName~CascadaDeSugerencia` | N/A — pure unit tests, no DB/HTTP needed | Revert PR 1: removes `sugerencia/` folder + sln entries, no dependents yet |
| 2 | `ServicioDeSugerencia` orchestrator + structural/purity guards + e2e spec verification | PR 2 | `dotnet test SmartNet/sugerencia/SmartNet.Sugerencia.Core.Tests` | N/A — fake in-memory ports, no live DB/HTTP | Revert PR 2 only: removes `ServicioDeSugerencia.cs` + its tests; cascade (PR 1) unaffected |

## Phase 1: Scaffolding (PR 1)

- [x] 1.1 Create `SmartNet/sugerencia/SmartNet.Sugerencia.Core/SmartNet.Sugerencia.Core.csproj` (net10.0, zero PackageReference, ProjectReference → `Catalogos.Core`)
- [x] 1.2 Create `SmartNet/sugerencia/SmartNet.Sugerencia.Core.Tests/SmartNet.Sugerencia.Core.Tests.csproj` (xUnit, Mono.Cecil, NetArchTest — mirror `SmartNet.Contable.Core.Tests.csproj`)
- [x] 1.3 Add `sugerencia` solution folder + both new projects to `SmartNet/SmartNet.sln`
- [x] 1.4 Create result records: `EscalonSugerencia.cs`, `SugerenciaDeCuenta.cs`, `SugerenciaDeMotivo.cs`, `SugerenciaParaFactura.cs` in `SmartNet.Sugerencia.Core` per design's Interfaces/Contracts

## Phase 2: `CascadaDeSugerencia.SugerirCuenta` — TDD cascade tiers

- [x] 2.1 RED: test tier 1 wins when `(proveedor, motivo)` history exists (spec scenario "Tier 1 resolves") in `CascadaDeSugerenciaTests.cs`
- [x] 2.2 GREEN: implement tier-1 filter (vigentes ∩ `Veces > 0`) + comparator `Veces` DESC → `UltimoUso` DESC → `CuentaCodigo` ASC in `CascadaDeSugerencia.SugerirCuenta`
- [x] 2.3 RED: test falls to tier 2 when no provider-specific rows exist (spec "Falls to tier 2")
- [x] 2.4 GREEN: implement tier-2 fallback over `usoGlobalDelMotivo`
- [x] 2.5 RED: test falls to tier 3, ordinal-minimum candidate, no tie (spec "Falls to tier 3 without a tie")
- [x] 2.6 GREEN: implement tier-3 = ordinal min of `vigentes`, `Veces = VecesDelAmbito = 0`
- [x] 2.7 RED: test tier 3 is deterministic under unsorted candidate input (spec "tier 3 with a tie ... regardless of input row order")
- [x] 2.8 GREEN: re-derive minimum internally instead of trusting `[0]` (Design Decision 4)
- [x] 2.9 RED: test tier-1 `Veces` tie resolved by `UltimoUso` DESC (spec "Tie in Veces at tier 1")
- [x] 2.10 RED: test tier-2 `Veces`+`UltimoUso` tie resolved by `CuentaCodigo` ASC (spec "Tie ... at tier 2")
- [x] 2.11 GREEN: confirm/adjust comparator covers both tie levels (2.9/2.10 should now pass)
- [x] 2.12 RED: test a top-ranked historical row excluded when its `CuentaCodigo` is no longer in `candidatasVigentes` (spec "historically-used account no longer in the live candidates")
- [x] 2.13 GREEN: apply vigencia filter before ranking in both tier 1 and tier 2
- [x] 2.14 RED: test brand-new provider falls straight to tier 2 (global history exists) — spec "First-ever invoice ... motivo has prior global history"
- [x] 2.15 RED: test brand-new provider + brand-new motivo falls to tier 3 — spec "motivo also has no history anywhere"
- [x] 2.16 GREEN: verify 2.14/2.15 pass with existing tier logic; patch gaps if any
- [x] 2.17 RED: test empty `candidatasVigentes` returns `null`
- [x] 2.18 GREEN: implement empty-vigentes short-circuit

## Phase 3: `CascadaDeSugerencia.SugerirMotivo` — TDD

- [x] 3.1 RED: test `SugerirMotivo` returns provider's highest aggregate-`Veces` motivo (spec "Motivo suggestion returns the provider's most-used motivo")
- [x] 3.2 GREEN: implement aggregation by `Motivo` (sum `Veces`, max `UltimoUso`), filter by `motivosOfrecibles`, comparator `Veces` DESC → `UltimoUso` DESC → `Motivo` ASC
- [x] 3.3 RED: test no history for provider returns `null` (no tier 2/3 exists for motivo)
- [x] 3.4 GREEN: confirm null-path already covered; patch if needed

## Phase 4: `Fundamento` rationale — TDD

- [x] 4.1 RED: test tier-1 result's `SugerenciaDeCuenta` exposes `Escalon=1`, winning `Veces`, `VecesDelAmbito` (sum over filtered winning-tier rows) and a non-empty `Fundamento` string (spec "Tier-1 result exposes usage counts")
- [x] 4.2 GREEN: populate `Fundamento`/`VecesDelAmbito` per Design Decision 3 (denominator = filtered winning-tier rows only)
- [x] 4.3 RED: test orchestration result exposes cuenta + motivo + fundamento together (spec "Orchestration returns a combined result") — placed here as a pending integration test, activated in Phase 5 (DEFERRED to PR 2: depends on `ServicioDeSugerencia`, out of scope for PR 1)

## Phase 5: `ServicioDeSugerencia` orchestrator (PR 2) — TDD

- [x] 5.1 Create `ServicioDeSugerencia.cs` constructor accepting the 4 ports (`ISugerenciaCuentaRepository`, `ICuentaContableRepository`, `IMotivoRepository`, `IMotivoAtributoRepository`)
- [x] 5.2 RED: test `SugerirParaFacturaAsync` calls `ResolverCandidatas` + repos with fake in-memory ports and returns combined `SugerenciaParaFactura` (spec "Orchestration returns a combined result") in `ServicioDeSugerenciaTests.cs`
- [x] 5.3 GREEN: implement wiring — compute `motivosOfrecibles` (Activo && OrigenLibro=="02"), call `ResolverCandidatas`, feed `CascadaDeSugerencia`
- [x] 5.4 RED: test `motivoSeleccionado = null` path still returns a motivo suggestion and no cuenta if no motivo resolved
- [x] 5.5 GREEN: handle nullable `motivoSeleccionado` branch
- [x] 5.6 RED: test motivo with zero live candidates returns `Cuenta = null`, `CandidatasVigentes` empty
- [x] 5.7 GREEN: handle empty-candidates branch
- [x] 5.8 RED: test `RegistrarUsoAsync` is never invoked by `ServicioDeSugerencia` (design boundary: writing is item #11's job)
- [x] 5.9 GREEN: confirm no call path reaches `RegistrarUsoAsync`; fix if a stray call exists

## Phase 6: Structural guards

- [x] 6.1 Create `PurityScanTests.cs` in `SmartNet.Sugerencia.Core.Tests` (copy pattern from `Contable.Core.Tests/PurityScanTests.cs`, incl. `DateTime.Now/UtcNow` IL scan) — RED first against a deliberately impure stub, then confirm GREEN against real code
- [x] 6.2 Add reflection/NetArchTest assertion: `CascadaDeSugerencia` exposes no `Task`-returning member
- [x] 6.3 Add `NoRankingStructuralTests.cs`: `Catalogos.Core.dll` contains no ranking-shaped type (extend existing guard from item #3, do not weaken it)

## Phase 7: End-to-end spec verification

- [x] 7.1 Run full `SmartNet.Sugerencia.Core.Tests` suite; map each of the 12 scenarios across the 7 spec requirements to a passing test and confirm no gaps
- [x] 7.2 Confirm ADR 0011 rev. 4 and REGLAS.md §3 remain untouched (no file diff), and `fact.SugerenciaCuenta` schema/grants are unmodified
