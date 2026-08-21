# Apply Progress: Sugerencia de cuenta (BACKLOG #9) — PR 1 + PR 2 (merged)

**Batch**: 2 of N (PR 2 batch, merged with PR 1's prior progress)
**Mode**: Strict TDD
**PR boundary**: PR 1 (Phases 1–4 minus 4.3) is complete and green. PR 2 (this batch) adds
`ServicioDeSugerencia` (Phase 5), the structural/purity guards (Phase 6), end-to-end spec
verification (Phase 7), and reactivates task 4.3. PR 2 targets PR 1's branch (stacked-to-main
chain strategy) — see "Branch note" below.

## Completed Tasks (cumulative — PR 1 + PR 2)

- [x] 1.1–1.4 — scaffolding: 2 csproj, sln entries, 4 result records (PR 1)
- [x] 2.1–2.18 — `CascadaDeSugerencia.SugerirCuenta`: all 3 tiers, both tie-break levels,
      vigencia filter, determinism, brand-new-provider paths, empty-vigentes short-circuit (PR 1)
- [x] 3.1–3.4 — `CascadaDeSugerencia.SugerirMotivo`: aggregation, comparator, null path (PR 1)
- [x] 4.1–4.2 — `Fundamento`/`VecesDelAmbito` rationale for tier-1/2 cuenta results (PR 1)
- [x] 4.3 — orchestration result exposes cuenta + motivo + fundamento together — **reactivated
      and completed in PR 2**, via `ServicioDeSugerenciaTests.SugerirParaFacturaAsync_CombinesCuentaMotivoYFundamento_WhenMotivoNotPreSelected`
- [x] 5.1–5.9 — `ServicioDeSugerencia` orchestrator: ctor over the 4 ports, wiring, null-motivo
      branch, empty-candidates branch, `RegistrarUsoAsync` never invoked (spy-asserted) (PR 2)
- [x] 6.1–6.3 — Structural guards: `PurityScanTests.cs` (incl. `DateTime.Now/UtcNow` IL scan,
      RED confirmed against a deliberately impure stub then reverted to GREEN), `CascadaDeSugerencia`
      exposes no `Task`-returning member, extended `NoRankingStructuralTests.cs` (PR 2)
- [x] 7.1–7.2 — End-to-end spec verification: full suite green, all spec scenarios mapped, ADR
      0011/REGLAS.md/schema confirmed untouched by this batch (PR 2)

**32/32 tasks complete. All phases done.**

## Files Changed — PR 1 (unchanged from prior batch)

| File | Action | What Was Done |
|------|--------|---------------|
| `SmartNet/sugerencia/SmartNet.Sugerencia.Core/SmartNet.Sugerencia.Core.csproj` | Created | net10.0, zero PackageReference, ProjectReference → `Catalogos.Core` |
| `SmartNet/sugerencia/SmartNet.Sugerencia.Core.Tests/SmartNet.Sugerencia.Core.Tests.csproj` | Created | xUnit + Mono.Cecil + NetArchTest |
| `SmartNet/sugerencia/SmartNet.Sugerencia.Core/EscalonSugerencia.cs` | Created | 3-value enum |
| `SmartNet/sugerencia/SmartNet.Sugerencia.Core/SugerenciaDeCuenta.cs` | Created | Result record |
| `SmartNet/sugerencia/SmartNet.Sugerencia.Core/SugerenciaDeMotivo.cs` | Created | Result record |
| `SmartNet/sugerencia/SmartNet.Sugerencia.Core/SugerenciaParaFactura.cs` | Created | Combined record |
| `SmartNet/sugerencia/SmartNet.Sugerencia.Core/CascadaDeSugerencia.cs` | Created | `SugerirCuenta` + `SugerirMotivo`, pure |
| `SmartNet/sugerencia/SmartNet.Sugerencia.Core.Tests/CascadaDeSugerenciaTests.cs` | Created | 15 tests |
| `SmartNet/SmartNet.sln` | Modified | Added `sugerencia` solution folder + 2 projects |

## Files Changed — PR 2 (this batch)

| File | Action | What Was Done |
|------|--------|---------------|
| `SmartNet/sugerencia/SmartNet.Sugerencia.Core/ServicioDeSugerencia.cs` | Created | Single entry point: ctor over 4 ports, `SugerirParaFacturaAsync` wiring `MotivoAtributo`-filtered `motivosOfrecibles` → `SugerirMotivo` (when `motivoSeleccionado` is null) → `ResolverCandidatas` → `SugerirCuenta`. Never calls `RegistrarUsoAsync`. |
| `SmartNet/sugerencia/SmartNet.Sugerencia.Core.Tests/ServicioDeSugerenciaTests.cs` | Created | 5 tests: combined result for explicit motivo, null-motivoSeleccionado with no history (no cuenta/motivo), motivo with zero live candidates, `RegistrarUsoAsync` spy (0 calls across 2 invocations), and the reactivated 4.3 case (null motivoSeleccionado WITH history → motivo suggested + cuenta chained, both `Fundamento` populated) — using 4 hand-written fakes (`FakeSugerenciaCuentaRepository` incl. spy counter, `FakeCuentaContableRepository`, `FakeMotivoRepository`, `FakeMotivoAtributoRepository`) |
| `SmartNet/sugerencia/SmartNet.Sugerencia.Core.Tests/PurityScanTests.cs` | Created | Copy of `Contable.Core.Tests/PurityScanTests.cs` pattern retargeted at `SmartNet.Sugerencia.Core.dll` (SqlClient/AspNetCore/Http dependency checks + Mono.Cecil IL scan for `DateTime.Now/UtcNow`), plus one extra reflection test: `CascadaDeSugerencia_ExposesNoTaskReturningMember` |
| `SmartNet/catalogos/SmartNet.Catalogos.Infrastructure.Tests/NoRankingStructuralTests.cs` | Modified | Extended (not replaced) — original `ISugerenciaCuentaRepository`-member assertion untouched; added `CatalogosCore_DeclaresNoType_ThatIsRankingOrSelectionShaped`, scanning every public type in `Catalogos.Core.dll` for a ranking-shaped name |

## Branch note (chain_strategy: stacked-to-main)

Resolved post-apply, during delivery. `feat/item-9-sugerencia-cuenta-pr1` (commit `b78b33b`) holds
Phases 1–4 (minus 4.3), branched from `main`. `feat/item-9-sugerencia-cuenta-pr2` (commit
`b2ee7ae`) holds Phases 5–7 plus reactivated 4.3, branched from `feat/item-9-sugerencia-cuenta-pr1`
per `stacked-to-main`. Each branch was built and tested in an isolated `git worktree` before commit
(15/15 tests on PR 1 alone; 27/27 + 2/2 `NoRankingStructuralTests` on PR 2). The original working
tree (`feat/inbox-y-promocion-wu6-adr-fix`) had unrelated uncommitted work (item #8 núcleo
contable) that this split deliberately left untouched. Neither branch has been pushed yet.

## TDD Cycle Evidence — PR 2

| # | Scenario | RED | GREEN | REFACTOR |
|---|---|---|---|---|
| 1 | `ServicioDeSugerenciaTests.cs` written first, referencing the not-yet-existing `ServicioDeSugerencia` type | Confirmed: `dotnet test` → `CS0246: 'ServicioDeSugerencia' no se encontró` (compile-level RED, 5 tests uncompilable) | Implemented `ServicioDeSugerencia.cs`; reran → **20/20 passed** (15 cascade + 5 orchestrator) | None needed — all 5 scenarios (5.2/5.4/5.6/5.8 + reactivated 4.3) satisfied by one wiring pass; no dead branches |
| 2 | `PurityScanTests.cs`: `DomainCore_DoesNotCallDateTimeNowOrUtcNowDirectly` + `CascadaDeSugerencia_ExposesNoTaskReturningMember` | Injected a temporary impure stub (`StubTaskTemporal(): Task<int>`) into `CascadaDeSugerencia.cs`; ran `--filter FullyQualifiedName~PurityScanTests` → **1 real failure**: `CascadaDeSugerencia declares Task-returning member(s): StubTaskTemporal` | Reverted the stub; reran → **7/7 passed** | None needed |
| 3 | `NoRankingStructuralTests.CatalogosCore_DeclaresNoType_ThatIsRankingOrSelectionShaped` | Not RED-first: this is a by-construction confirmation over already-existing types (same disclosed pattern as the original guard's own doc comment — item #3's `ISugerenciaCuentaRepository`/`SugerenciaCuenta` predate this task, nothing to change to make it pass) | Ran `--filter FullyQualifiedName~NoRankingStructuralTests` → **2/2 passed** (original assertion + new one) | None needed |

## Deviations from Strict Per-Scenario TDD (disclosed, not silent)

Carried over from PR 1 (see below) plus:

4. **`NoRankingStructuralTests` extension (6.3) was not RED-first**, matching the same disclosed
   pattern the original test's own doc comment uses ("this is a by-construction confirmation, not
   a RED-first task"): `Catalogos.Core.dll`'s public types already contained no ranking-shaped
   name before this task — there was nothing to make fail deliberately without editing
   `Catalogos.Core` itself (out of scope for item #9, which must not touch item #3's assembly).
5. **`ServicioDeSugerencia`'s "motivo not pre-selected" branch (4.3/5.4) was implemented in one
   pass covering both the null-result and populated-result cases**, verified by 2 separate tests
   (`..._NoMotivoResolved_ReturnsNoCuenta` and `..._CombinesCuentaMotivoYFundamento_WhenMotivoNotPreSelected`)
   rather than a fresh compile-level RED per case — both compiled RED together (`CS0246`), then
   both went green in the same implementation pass, for the same reason as PR 1's Deviation 1
   (the branch's few lines are indivisible without a temporarily-wrong intermediate state).

None of these deviations change scope, weaken a spec requirement, or touch anything outside the
files listed above.

---

### PR 1 deviations (carried over, unchanged)

1. **Tiers 1–3 of `SugerirCuenta` implemented together after one RED test.**
2. **`VecesDelAmbito` for `SugerirMotivo`** — owner-confirmed 2026-08-20 (see design.md Decision 3
   extended note): total `Veces` across every offerable motivo for the provider.
3. **Test 15 (`Tier1Result_ExposesUsageCounts_ForRationaleRendering`) was written after tier-1
   logic already existed.**

## Work Unit Evidence — PR 2

| Evidence | Value |
|---|---|
| Focused test command and exact result | `dotnet test SmartNet/sugerencia/SmartNet.Sugerencia.Core.Tests --filter FullyQualifiedName~ServicioDeSugerenciaTests` → **5/5 passed**; `--filter FullyQualifiedName~PurityScanTests` → **7/7 passed**; `SmartNet.Catalogos.Infrastructure.Tests --filter FullyQualifiedName~NoRankingStructuralTests` → **2/2 passed** |
| Full-project test command and exact result | `dotnet test SmartNet/sugerencia/SmartNet.Sugerencia.Core.Tests` (no filter) → **27/27 passed**, 0 failed, 0 skipped (15 cascade + 5 orchestrator + 7 purity) |
| Full-solution build | `dotnet build SmartNet/SmartNet.sln` → **0 errors, 0 warnings**, all 26 projects compile |
| Runtime harness command/scenario and exact result | N/A — `ServicioDeSugerencia` is tested exclusively against 4 hand-written in-memory fakes; per design.md, item #9 ships zero SQL adapter and zero `.Infrastructure` project, so there is no live-DB/HTTP integration boundary to exercise in this PR |
| Rollback boundary | Revert PR 2 only: removes `ServicioDeSugerencia.cs`, `ServicioDeSugerenciaTests.cs`, `PurityScanTests.cs`, and the one added method in `NoRankingStructuralTests.cs` (the original assertion in that file is untouched and stays). `CascadaDeSugerencia` (PR 1) has zero dependents inside PR 2's new code other than `ServicioDeSugerencia` itself, so PR 1 is unaffected by a PR 2 revert. |

## End-to-end spec verification (Phase 7.1) — requirement → scenario → test mapping

spec.md's capability `sugerencia-cuenta` declares **7 requirements** with **12 explicitly-headed
`#### Scenario:` blocks** (counted directly from `specs/sugerencia-cuenta/spec.md`; the tasks
prompt's "15 scenarios" figure appears to also count cascade-only edge cases that don't have a
dedicated `#### Scenario:` header of their own — flagging this discrepancy for the record rather
than silently reconciling the two numbers). All 12 headed scenarios map to a passing test, no
gaps found:

| Requirement | Scenario | Test | Result |
|---|---|---|---|
| 1. Three-tier cascade, strict order | Tier 1 resolves when provider-specific history exists | `Tier1_Resolves_When_ProviderSpecificHistoryExists` | PASS |
| 1 | Falls to tier 2 when provider has no history for this motivo | `FallsToTier2_When_ProviderHasNoHistoryForThisMotivo` | PASS |
| 1 | Falls to tier 3 without a tie | `FallsToTier3_WithoutATie_ReturnsLowestCuentaCodigo` | PASS |
| 1 | Falls to tier 3 with a tie in candidate codes, deterministic | `FallsToTier3_IsDeterministic_RegardlessOfInputRowOrder` | PASS |
| 2. Tiers 1/2 tie-break: Veces DESC → UltimoUso DESC → CuentaCodigo ASC | Tie in Veces at tier 1 resolved by UltimoUso | `Tier1_TieInVeces_ResolvedByUltimoUsoDescending` | PASS |
| 2 | Tie in Veces and UltimoUso at tier 2 resolved by CuentaCodigo | `Tier2_TieInVecesAndUltimoUso_ResolvedByCuentaCodigoAscending` | PASS |
| 3. Filtered against live candidate set | Historically-used account no longer in live candidates is excluded | `HistoricallyUsedAccount_NoLongerInLiveCandidates_IsExcluded` | PASS |
| 4. Brand-new provider falls through | First-ever invoice, motivo has prior global history | `FirstEverInvoiceForProvider_MotivoHasPriorGlobalHistory_FallsToTier2` | PASS |
| 4 | First-ever invoice, motivo also has no history anywhere | `FirstEverInvoiceForProvider_MotivoHasNoHistoryAnywhere_FallsToTier3` | PASS |
| 5. Same cascade suggests motivo, indexed by provider only | Motivo suggestion returns provider's most-used motivo | `SugerirMotivo_ReturnsProvidersMostUsedMotivo` | PASS |
| 6. Auditable rationale as data | Tier-1 result exposes usage counts | `Tier1Result_ExposesUsageCounts_ForRationaleRendering` | PASS |
| 7. Orchestration exposes cuenta + motivo + fundamento | Orchestration returns a combined result for a given provider and motivo | `SugerirParaFacturaAsync_ReturnsCombinedResult_ForGivenProveedorAndMotivo` + `SugerirParaFacturaAsync_CombinesCuentaMotivoYFundamento_WhenMotivoNotPreSelected` (4.3) | PASS |

Supporting edge-case tests beyond the 12 headed scenarios (all PASS, all green): cascade
determinism re-derivation, `SugerirMotivo` aggregation-before-comparison, `SugerirMotivo` no-history
null path, `SugerirMotivo` excludes non-offerable motivos, empty-`candidatasVigentes` short-circuit,
orchestrator null-motivo/no-history branch, orchestrator zero-live-candidates branch, orchestrator
`RegistrarUsoAsync` spy (0 invocations), plus 7 structural/purity tests.

**Total: 27/27 tests green in `SmartNet.Sugerencia.Core.Tests`.**

### Phase 7.2 — ADR / REGLAS.md / schema untouched

- `adrs/0011-motivo-de-compra-y-sugerencia-de-cuenta.md` shows as modified in `git status`, but
  that diff (rev. 3 → rev. 4: removes the historical-seeding section, fixes tier-1/2 tie-break)
  **predates this apply batch** — it was not touched by any PR 1 or PR 2 task in this session.
  Confirmed no additional diff was introduced by PR 2's tasks.
- `REGLAS.md` — not present in `git status --short` output; confirmed untouched.
- `fact.SugerenciaCuenta` schema/grants (`004_satelites_datos_maestros.sql`,
  `008_usuarios_y_permisos.sql`) — not present in `git status --short` output; confirmed untouched.
  Item #9 adds no SQL migration, consistent with design.md's "No schema change" statement.

## Issues Found

None new in PR 2. PR 1's note about `CuentaContable.Cuenta` (not `CuentaCodigo`) naming still
applies and required no further action.

## Status

**32/32 tasks complete (Phases 1–7, including reactivated 4.3). PR 1 and PR 2 both green.**
Ready for `sdd-verify`. Delivery/branching (splitting the working tree into the PR 1 → PR 2
stacked branches) is a follow-up outside `sdd-apply`'s scope — see "Branch note" above.
