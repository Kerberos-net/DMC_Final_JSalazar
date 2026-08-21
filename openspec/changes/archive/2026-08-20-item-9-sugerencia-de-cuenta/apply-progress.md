# Apply Progress: Sugerencia de cuenta (BACKLOG #9) — PR 1 + PR 2 (merged, 32/32 tasks complete)

Mode: Strict TDD. PR 1 (Phases 1-4 minus 4.3) was already green from a prior batch. This batch (PR 2)
completed Phase 5 (ServicioDeSugerencia orchestrator), Phase 6 (structural/purity guards), Phase 7
(e2e spec verification), and reactivated task 4.3.

## Files changed in PR 2
- Created SmartNet/sugerencia/SmartNet.Sugerencia.Core/ServicioDeSugerencia.cs: single entry point,
  ctor over the 4 ports (ISugerenciaCuentaRepository, ICuentaContableRepository, IMotivoRepository,
  IMotivoAtributoRepository). SugerirParaFacturaAsync(proveedorCodigo, motivoSeleccionado, ct): if
  motivoSeleccionado is null, computes motivosOfrecibles (Activo && OrigenLibro=="02") and calls
  CascadaDeSugerencia.SugerirMotivo; then resolves candidatasVigentes via ResolverCandidatas
  and calls CascadaDeSugerencia.SugerirCuenta. NEVER calls RegistrarUsoAsync (item #11's job).
- Created ServicioDeSugerenciaTests.cs: 5 tests using 4 hand-written in-memory fakes (incl. a spy
  counter on RegistrarUsoAsync). Covers: combined result for explicit motivo (5.2/5.3), null
  motivoSeleccionado + no history → no motivo/no cuenta (5.4/5.5), motivo with zero live candidates
  → Cuenta=null (5.6/5.7), RegistrarUsoAsync spy = 0 calls across 2 invocations (5.8/5.9), and
  reactivated 4.3: null motivoSeleccionado WITH history → motivo suggested + cuenta chained, both
  Fundamento populated.
- Created PurityScanTests.cs (copy of Contable.Core.Tests pattern, retargeted at
  SmartNet.Sugerencia.Core.dll): SqlClient/AspNetCore/Http dependency checks + Mono.Cecil IL scan
  for DateTime.Now/UtcNow, plus CascadaDeSugerencia_ExposesNoTaskReturningMember (reflection).
  RED confirmed by temporarily injecting a Task-returning stub method into CascadaDeSugerencia.cs,
  observed 1 real failure, then reverted → GREEN (7/7).
- Modified SmartNet/catalogos/SmartNet.Catalogos.Infrastructure.Tests/NoRankingStructuralTests.cs:
  EXTENDED, not replaced — original ISugerenciaCuentaRepository 4-method assertion untouched added
  CatalogosCore_DeclaresNoType_ThatIsRankingOrSelectionShaped, scanning every public type in
  Catalogos.Core.dll by name regex. 2/2 pass.

## Test results (actually run, not assumed)
- dotnet test SmartNet/sugerencia/SmartNet.Sugerencia.Core.Tests (no filter) → 27/27 passed (15
  cascade [PR1] + 5 orchestrator [PR2] + 7 purity [PR2]), 0 failed, 0 skipped.
- dotnet build SmartNet/SmartNet.sln → 0 errors, 0 warnings, all 26 projects compile.
- SmartNet.Catalogos.Infrastructure.Tests --filter NoRankingStructuralTests → 2/2 passed
  (confirms Catalogos.Core.dll still has zero ranking-shaped public types).

## E2E spec verification (Phase 7.1/7.2)
spec.md's 7 requirements map to 12 explicitly-headed Scenario: blocks. All 12 map to a passing test,
no gaps: tier1/tier2/tier3 (4 scenarios) + tie-break rules (2) + vigencia filter (1) + brand-new
provider (2) + motivo suggestion (1) + auditable rationale (1) + orchestration combined result (1,
covered by 2 tests incl. reactivated 4.3). Plus 8 supporting edge-case tests + 7 structural tests.

ADR 0011 shows modified in git status (rev.3→rev.4, removes historical seeding, fixes tie-break)
but that diff predates this apply session -- confirmed zero additional diff introduced by PR2
tasks. REGLAS.md and fact.SugerenciaCuenta schema/grants files: not present in git status, confirmed
untouched.

## Branch/delivery note
PR 1 branch: feat/item-9-sugerencia-cuenta-pr1 (commit b78b33b, over main)
PR 2 branch: feat/item-9-sugerencia-cuenta-pr2 (commit 1fa8125, over PR1)
Both branches created as part of this delivery implementation (not pre-existing).

## Status
32/32 tasks complete across PR1+PR2. Ready for sdd-verify.
