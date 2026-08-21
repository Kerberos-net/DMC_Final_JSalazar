# Verification Report: Sugerencia de cuenta (BACKLOG #9)

**Change**: item-9-sugerencia-de-cuenta  
**Mode**: Full artifacts (proposal/spec/design/tasks/apply-progress all present)  
**Verified from**: worktree item-9-verify on branch feat/item-9-sugerencia-cuenta-pr2 (PR1+PR2 stacked)  
**Verdict**: PASS WITH WARNINGS

## Completeness
proposal.md, spec.md (7 requirements, 12 scenarios - directly recounted), design.md (4 decisions), tasks.md (32/32 done), apply-progress.md all present.

## Build/Test Evidence (run live by this verifier)
- dotnet build SmartNet/SmartNet.sln → 0 errors, 0 warnings, 26 projects.
- dotnet test SmartNet/sugerencia/SmartNet.Sugerencia.Core.Tests → 27/27 passed.
- dotnet test SmartNet/catalogos/SmartNet.Catalogos.Infrastructure.Tests --filter NoRankingStructuralTests → 2/2 passed.

All figures match apply-progress.md's claimed numbers exactly.

## Spec Compliance (7 requirements / 12 scenarios, all PASS)
1. **3-tier cascade strict order**: Tier1_Resolves_When_ProviderSpecificHistoryExists, FallsToTier2_When_ProviderHasNoHistoryForThisMotivo, FallsToTier3_WithoutATie_ReturnsLowestCuentaCodigo, FallsToTier3_IsDeterministic_RegardlessOfInputRowOrder.
2. **Tie-break Veces→UltimoUso→CuentaCodigo**: Tier1_TieInVeces_ResolvedByUltimoUsoDescending, Tier2_TieInVecesAndUltimoUso_ResolvedByCuentaCodigoAscending.
3. **Vigencia filter**: HistoricallyUsedAccount_NoLongerInLiveCandidates_IsExcluded.
4. **New provider**: FirstEverInvoiceForProvider_MotivoHasPriorGlobalHistory_FallsToTier2, FirstEverInvoiceForProvider_MotivoHasNoHistoryAnywhere_FallsToTier3.
5. **Motivo cascade**: SugerirMotivo_ReturnsProvidersMostUsedMotivo.
6. **Auditable rationale**: Tier1Result_ExposesUsageCounts_ForRationaleRendering.
7. **Orchestration combined result**: SugerirParaFacturaAsync_ReturnsCombinedResult_ForGivenProveedorAndMotivo + SugerirParaFacturaAsync_CombinesCuentaMotivoYFundamento_WhenMotivoNotPreSelected (reactivated task 4.3).

All 12 scenarios map to passing, real (not stubbed) unit tests. No gaps, no UNTESTED or FAILING scenarios.

## Design Correctness (all Confirmed)
D1 separate assembly, no .Infrastructure project; D2 orchestrator depends only on 4 ports, no SqlClient/HTTP/clock; D3 Fundamento/VecesDelAmbito denominator = filtered winning-tier rows (cascade) / sum over offerable motivos (SugerirMotivo); D4 determinism re-derived internally via HashSet.Min, not caller order. RegistrarUsoAsync never called (spy test 0 calls x2 invocations).

## Task Completion
All 32 tasks in tasks.md marked done, spot-checked against real files/tests per phase.

## Scope Boundary
git diff --stat --name-only main..HEAD (19 files): only SmartNet/**, BACKLOG.md, adrs/0011-motivo-de-compra-y-sugerencia-de-cuenta.md, openspec/changes/item-9-sugerencia-de-cuenta/**. Zero .sql files, zero other ADRs, REGLAS.md untouched. ADR 0011 diff inspected: rev 3→4, removes historical-seed SQL section, adds tier 1/2 tie-break paragraph - matches spec/design description.

## Issues

### CRITICAL
None.

### WARNING
- (1) tasks.md 7.1 says "15 scenarios" vs actual 12 (already flagged honestly in apply-progress.md, cosmetic fix recommended before archive).
- (2) apply-progress.md's "Branch/delivery note" is stale - claims no branch was created / work is uncommitted on wu6-adr-fix branch, but worktree shows real commits on dedicated pr1/pr2 branches. Recommend updating apply-progress.md.

### SUGGESTION
- spec.md's rationale requirement doesn't mention the multi-motivo VecesDelAmbito denominator explicitly (only design.md + XML doc do) - non-blocking, consider a spec.md clarifying note.

## Final Verdict
**PASS WITH WARNINGS**. 0 CRITICAL / 2 WARNING / 1 SUGGESTION. Safe to proceed to sdd-archive; warnings are documentation-freshness only, not implementation defects.
