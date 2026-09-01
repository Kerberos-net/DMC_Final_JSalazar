# Apply Progress: pdf-asociado-en-documento-factura

**Mode**: Strict TDD (RED → GREEN → REFACTOR)
**Delivery**: single-pr with owner-approved `size:exception` (~565 forecast lines)
**Batch**: 1 (first and only) — all 25 tasks across 5 phases complete
**Status**: done

## TDD Cycle Evidence

| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 1.1–1.2 | `SmartNet.Inbox.Core.Tests/PoliticaDeDocumentoAsociadoTests.cs` | Unit (pure) | N/A (new) | ✅ compile-fail on missing types | ✅ 6/6 | ✅ 6 cases (PDF+asociado, XML+asociado guard, PDF-no-asociado, 3 Decidir maps) | ➖ clean |
| 1.3–1.5 | (same) | Unit (pure) | — | ✅ | ✅ 6/6 | — | ➖ clean |
| 1.6 | `PurityScanTests.cs` | Unit (arch) | ✅ 6/6 pre-run | N/A verify | ✅ 6/6 still green | — | — |
| 2.3–2.4 | `SmartNet.Inbox.Infrastructure.Tests/SqlPromocionRepositoryTests.cs` | L2 boundary (real SQL) | ✅ 11/11 pre-run | ✅ compile-fail on missing interface members | ✅ 18/18 | ✅ 7 new cases (Query A hit; Factura discarded→ParNoPromovible; event DESCARTADO→ParNoPromovible; PENDIENTE→NoDisponible; absent→NoDisponible; merge inserts 1 row/no Factura/PROMOVIDO; idempotent no-op) | ➖ clean |
| 2.5–2.6 | (same) | L2 | — | ✅ | ✅ 18/18 | — | ➖ clean |
| 2.7 | `PermissionSufficiencyTests.cs` | L2 permission matrix | ✅ 9/9 pre-run | ✅ (compile fix) | ✅ 11/11 (Query A + Query B replay under `usr_api`) | ➖ single per query | ➖ clean |
| 2.8 | `NoWriteToDboStructuralTests.cs` | Static | ✅ 2/2 | N/A verify | ✅ 2/2 still green | — | — |
| 3.1–3.2 | `PromocionBackgroundServiceTests.cs` | L3 E2E cycle (real SQL) | ✅ 3/3 pre-run | ✅ 3 new tests fail (PDF discarded via wrong path / no merge / trivial-green closed by `MotivoDescarte` assertion) | ✅ 6/6 | ✅ XML-first merge, PDF-first defer, XML-discard→PDF-discard-2-cycles + regression assertion on `PayloadCompleto` | ➖ clean |
| 3.3 | `PromocionBackgroundService.cs` | — | — | ✅ | ✅ 6/6 then 71/71 full suite | — | ➖ clean |
| 3.4 | full `SmartNet.Inbox.Infrastructure.Tests` | — | — | N/A verify | ✅ 71/71 | — | — |
| 4.1–4.2 | `visor-documento.spec.ts` | SPA unit (Vitest + jsdom) | ✅ 478/479 pre-run (1 pre-existing failure elsewhere n/a) | ✅ "selects PDF by default" fails (XML selected) | ✅ 479/479 | ✅ 3 cases (mixed list prefers PDF, all-XML falls back documentos[0], explicit selection wins) | ➖ clean |
| 4.3 | full SPA suite | — | — | N/A verify | ✅ 479/479, 52/52 files | — | — |
| 5.1–5.4 | `dotnet test SmartNet.sln` + `npm test` | — | — | N/A sweep | ✅ all change-relevant suites green | — | — |

## Test Summary
- .NET tests written/extended: 6 (Core) + 7 (SqlPromocionRepository) + 2 (PermissionSufficiency) + 3 new & 1 amended (PromocionBackgroundService) = 18 new + 1 amended
- SPA tests written: 3 new
- Layers: Unit pure (Core), L2 boundary + permission matrix (real `fact_test_<guid>` SQL Server), L3 E2E cycle, SPA unit
- Pure functions created: `PoliticaDeDocumentoAsociado.EsDocumentoAsociado` / `.Decidir`
- Full suites: `SmartNet.Inbox.Infrastructure.Tests` 71/71 · `SmartNet.Api.Tests` 203/203 · SPA 479/479

## Files Changed

| File | Action | What |
|------|--------|------|
| `inbox/SmartNet.Inbox.Core/ResolucionPar.cs` | Create | Closed hierarchy `Fusionable`/`ParNoPromovible`/`NoDisponible`, `private protected` ctor |
| `inbox/SmartNet.Inbox.Core/DecisionDocumentoAsociado.cs` | Create | Closed hierarchy `Fusiona`/`Difiere`/`Descarta` |
| `inbox/SmartNet.Inbox.Core/PoliticaDeDocumentoAsociado.cs` | Create | Pure predicate (`DocumentoAsociadoId is not null && TipoDocumento == "PDF"`) + pure 1:1 `Decidir` map |
| `inbox/SmartNet.Inbox.Core/IPromocionRepository.cs` | Modify | +`ResolverParAsync`, +`FusionarDocumentoAsync` |
| `inbox/SmartNet.Inbox.Infrastructure/SqlPromocionRepository.cs` | Modify | `ResolverParAsync` (Query A JOIN + Query B `JSON_VALUE`/`TRY_CAST`, all `SqlParameter`); `FusionarDocumentoAsync` (one `SqlTransaction` reusing existing `InsertarDocumentoFacturaAsync` 2601/2627 catch + `MarcarPromovidoAsync`) |
| `inbox/SmartNet.Inbox.Infrastructure/PromocionBackgroundService.cs` | Modify | Branch in `ProcesarPendientesAsync` before `PoliticaDePromocion.Decidir` + `ProcesarDocumentoAsociadoAsync` dispatch |
| `SmartNetWeb/src/app/detalle/ui/visor-documento/visor-documento.ts` | Modify | `MIMES_RENDERIZABLES` set + `seleccionado` computed prefers first renderable, falls back `documentos[0]`, explicit selection still wins |
| 4 test files | Create/Modify | See TDD evidence table |

## Deviations from Design

- **Task 2.2 (FakePromocionRepository / test double)**: N/A — no test double for `IPromocionRepository` exists anywhere in the codebase. Its only implementation (`SqlPromocionRepository`) is exercised directly against a real migrated `fact_test_<guid>` database in every test. Nothing to update. Marked complete with this note.
- **Task 3.1 "two cycles" wording**: implemented as two separate `ProcesarPendientesAsync` calls (cycle 1 discards the XML; cycle 2 the PDF resolves Query B = DESCARTADO and discards), avoiding any dependency on `ListarPendientesAsync`'s unordered single-cycle result. The XML-discard scenario's assertion checks the merge-branch-specific `MotivoDescarte` text ("El evento asociado fue descartado"), because the PDF fixture's own comprobante is also structurally incomplete and would discard via the unchanged path too — a state-only assertion would have been a trivial green.
- Motive strings chosen in `ResolverParAsync`: `"El evento asociado fue descartado"` (partner event DESCARTADO) and `"La factura del evento asociado ya no está vigente"` (partner event PROMOVIDO but Factura later DESCARTADA). Design left the exact text unspecified.

## Pre-existing Failures (NOT caused by this change, do not fix here)

1. `SmartNet.Db.Runner.Tests.RunnerFailureHaltTests.FailingScript_ExitsNonZero...` — xUnit cross-class parallelism disposal race in `TestDatabaseFixture.DisposeAsync` ("database does not exist or you do not have permission"). Passes in isolation. `TestDatabaseFixture.cs` comments already document this race.
2. `SmartNet.Admin.Tests.SesionPurgarTests.DeletesRowsOlderThanTheRetentionWindow_AndLeavesNewerRowsUntouched` — session-purge retention boundary. Confirmed failing on a clean `git stash` of this work, in isolation, WITH AND WITHOUT the change. Zero overlap with inbox/promotion/viewer.

## Rollback Boundary

Pure code revert. New Core files deletable independently. The two `SqlPromocionRepository` methods and the `PromocionBackgroundService` branch revert without touching `PromoverAsync`/`DescartarAsync`. The `visor-documento.ts` `seleccionado` computed reverts to the prior `documentos[0]` fallback. No schema, no payload version, no coordinated deploy. Already-merged `fact.DocumentoFactura` rows stay valid.

## Remaining Tasks

None. 25/25 complete. Ready for `sdd-verify`.
