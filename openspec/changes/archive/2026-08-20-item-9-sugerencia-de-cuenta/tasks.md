# Tasks: Sugerencia de cuenta (BACKLOG #9)

## Review Workload Forecast
Estimated changed lines ~420-480. 400-line budget risk: Medium. Chained PRs recommended: Yes. 
Suggested split: PR 1 (scaffolding + contracts + pure CascadaDeSugerencia, fully TDD) → PR 2 (ServicioDeSugerencia orchestrator + structural guards + e2e). 
Delivery strategy: stacked-to-main (resolved by orchestrator for this apply batch).

## Phase 1: Scaffolding (PR 1) — ALL COMPLETE [x]
1.1 Create SmartNet.Sugerencia.Core.csproj with TargetFramework net10.0, no Infrastructure dependencies [x]
1.2 Create SmartNet.Sugerencia.Core.Tests.csproj, link to Core [x]
1.3 Add both projects to SmartNet.sln solution folder `sugerencia` [x]
1.4 Create 4 result records: EscalonSugerencia, SugerenciaDeCuenta, SugerenciaDeMotivo, SugerenciaParaFactura [x]

## Phase 2: CascadaDeSugerencia.SugerirCuenta — ALL COMPLETE [x]
2.1 Implement tier 1: most-used for (proveedor, motivo) pair [x]
2.2 Implement tier 2: most-used for motivo globally [x]
2.3 Implement tier 3: first by CuentaCodigo ASC [x]
2.4 Apply vigencia filter (ResolverCandidatas) at tier 1 and 2 [x]
2.5 Tie-break within tier 1/2: Veces DESC → UltimoUso DESC → CuentaCodigo ASC [x]
2.6 Deterministic tier 3 via HashSet.Min (Decision 4 from design) [x]
2.7 Handle empty candidates vigentes short-circuit [x]
2.8 Handle brand-new provider (no history, falls to tier 2/3) [x]
2.9 Test tier 1 with provider-specific history [x]
2.10 Test tier 2 fallback when provider has no history for motivo [x]
2.11 Test tier 3 determinism with reordered input [x]
2.12 Test tie-break Veces DESC with UltimoUso [x]
2.13 Test tie-break UltimoUso DESC with CuentaCodigo ASC [x]
2.14 Test vigencia filter: historically-used account no longer valid, excluded [x]
2.15 Test brand-new provider with global history → tier 2 [x]
2.16 Test brand-new provider + motivo with zero history → tier 3 [x]
2.17 Test empty SugerenciaCuenta rows [x]
2.18 Test empty candidatasVigentes short-circuit [x]

## Phase 3: CascadaDeSugerencia.SugerirMotivo — ALL COMPLETE [x]
3.1 Implement motivo aggregation by proveedor (sum Veces, max UltimoUso) [x]
3.2 Filter by motivosOfrecibles (Activo && OrigenLibro=="02") [x]
3.3 Apply tie-break comparator: Veces DESC → UltimoUso DESC → Motivo ASC [x]
3.4 Return null when no history for provider, or empty offerable motivos [x]

## Phase 4: Fundamento rationale — ALL COMPLETE [x]
4.1 Populate Fundamento/VecesDelAmbito for tier-1 results [x]
4.2 Populate Fundamento/VecesDelAmbito for tier-2 results [x]
4.3 REACTIVATED IN PR 2: Verify orchestration result exposes cuenta + motivo + fundamento together [x]

## Phase 5: ServicioDeSugerencia orchestrator (PR 2) — ALL COMPLETE [x]
5.1 Create ServicioDeSugerencia.cs with ctor over 4 ports [x]
5.2 Implement SugerirParaFacturaAsync(proveedorCodigo, motivoSeleccionado?, ct) [x]
5.3 When motivoSeleccionado is null: compute motivosOfrecibles and call SugerirMotivo [x]
5.4 Resolve candidatasVigentes via ResolverCandidatas for suggested/selected motivo [x]
5.5 Call SugerirCuenta with resolved candidates and return combined (cuenta, motivo, fundamento) [x]
5.6 Handle null motivoSeleccionado + no history → return (null, null, null) [x]
5.7 Handle motivo with zero live candidates → return (null, motivo, null) [x]
5.8 Test RegistrarUsoAsync is NEVER called by orchestrator (spy 0 calls) [x]
5.9 Verify combined result structure for both branches (explicit + null motivo) [x]

## Phase 6: Structural guards (PR 2) — ALL COMPLETE [x]
6.1 Create PurityScanTests.cs: NetArchTest rules (no SqlClient, AspNetCore, Http references) [x]
6.2 Add Mono.Cecil IL scan for DateTime.Now/UtcNow calls (zero allowed) [x]
6.3 Extend NoRankingStructuralTests.cs with assembly-wide scan: Catalogos.Core declares no ranking-shaped public types [x]

## Phase 7: End-to-end spec verification (PR 2) — ALL COMPLETE [x]
7.1 Verify all 7 spec requirements / 12 scenarios map to passing tests [x]
7.2 Confirm ADR 0011, REGLAS.md, fact.SugerenciaCuenta schema remain untouched [x]

## Summary
**32/32 tasks complete across PR 1 + PR 2.**
- 27/27 tests in SmartNet.Sugerencia.Core.Tests (15 cascade + 5 orchestrator + 7 purity)
- 2/2 tests in NoRankingStructuralTests extension
- Build: 0 errors, 0 warnings, 26 projects
- Ready for sdd-verify
