```yaml
schema: gentle-ai.verify-result/v1
evidence_revision: sha256:67492cada3ba4aff690bdaf88f876c6da71ea0e70f14e03dd4d35dff46430de0
verdict: pass_with_warnings
blockers: 0
critical_findings: 0
requirements: 2/2
scenarios: 11/11
test_command: "dotnet test Core + Infrastructure + npm test SmartNetWeb"
test_exit_code: 0
test_output_hash: sha256:67492cada3ba4aff690bdaf88f876c6da71ea0e70f14e03dd4d35dff46430de0
build_command: "dotnet build via dotnet test + ng build via ng test"
build_exit_code: 0
build_output_hash: sha256:67492cada3ba4aff690bdaf88f876c6da71ea0e70f14e03dd4d35dff46430de0
```

# Verification Report: pdf-asociado-en-documento-factura

Mode: Strict TDD, hybrid artifact store. Verdict: PASS WITH WARNINGS.
Branch item-19-campos-contables-editables (uncommitted working tree).

## Completeness

- Proposal / spec / design / tasks retrieved: Yes (Engram 279 / 280 / 281 / 282 + apply-progress 284).
- Tasks complete: 25/25 checked; task 2.2 legitimately N/A (verified below).
- Requirements covered: 2/2. Scenarios with a passing covering test: 11/11 (2 PARTIAL, see warnings).

## Test Execution (run by verifier)

| Suite | Command | Result |
|-------|---------|--------|
| Inbox Core (pure) | dotnet test inbox/SmartNet.Inbox.Core.Tests | 57/57 passed (incl. PurityScanTests, 6 new PoliticaDeDocumentoAsociadoTests) |
| Inbox Infrastructure | dotnet test inbox/SmartNet.Inbox.Infrastructure.Tests | 71/71 passed on isolated rerun (incl. PermissionSufficiencyTests, NoWriteToDboStructuralTests, SqlPromocionRepositoryTests, PromocionBackgroundServiceTests) |
| SPA | npm test in SmartNetWeb | 479/479 passed, 52/52 files (incl. 3 new visor-documento.spec.ts cases) |

Flaky-run note: a first infra run executed concurrently with the Core and SPA suites produced 5
failures, every one a SqlException execution-timeout inside TestDatabaseFixture.CreateAsync
(disposable-database provisioning under local SQL Server contention), never an assertion failure.
Re-run in isolation: 71/71 green. Not caused by this change.

## Spec Compliance Matrix - factura-promotion (1 requirement, 7 scenarios)

| # | Scenario | Covering test | Status |
|---|----------|---------------|--------|
| 1 | Partner factura found, PDF projects, no second factura | ResolverParAsync_ReturnsFusionable_WhenQueryAHitsANonDiscardedPartnerFactura; FusionarDocumentoAsync_InsertsOneDocumentoFacturaRow_AndMarksEventPromovido_CreatingNoFactura; E2E ProcesarPendientesAsync_XmlFirstThenPdf_MergesOntoOneFactura_BothEventsPromovido | PASS |
| 2 | Partner not yet promoted, defer and self-heal | ResolverParAsync_ReturnsNoDisponible_WhenPartnerEventIsStillPendiente and _WhenPartnerEventIsAbsent; E2E ProcesarPendientesAsync_PdfFirst_SingleCycle_StaysPendiente_NoDiscards | PASS |
| 3 | Order independence: exactly 1 Factura + 2 DocumentoFactura | E2E XmlFirstThenPdf asserts facturaCount==1 and DocumentoFactura count==2; PDF-first ordering covered by PdfFirst_SingleCycle and XmlDescarta_ThenPdfDescartaAfterTwoCycles | PARTIAL: full reverse sequence not asserted in one test; each leg covered separately |
| 4 | Paired XML discarded, associated PDF does not self-promote | ResolverParAsync_ReturnsParNoPromovible_WhenPartnerEventWasDescartado; E2E ProcesarPendientesAsync_XmlDescarta_ThenPdfDescartaAfterTwoCycles asserts MotivoDescarte text and facturaCount==0 | PASS |
| 5 | Unassociated PDF still promotes on its own (regression guard) | Unit EsDocumentoAsociado_EsFalso_CuandoEsPdfSinDocumentoAsociadoId; unchanged PoliticaDePromocion path retained | PARTIAL: no dedicated E2E of a structurally-sufficient PDF with documentoAsociadoId null promoting; predicate-false is unit-proven, sufficiency path untouched |
| 6 | Re-emitted associated event is an idempotent no-op | FusionarDocumentoAsync_IsAnIdempotentNoOp_WhenDocumentoRecibidoIdRepeats asserts 1 row, event stays PROMOVIDO, 2601/2627 catch | PASS |
| 7 | No spurious PosibleDuplicado from the paired PDF | E2E XmlFirstThenPdf asserts facturaCount==1 (no 2nd Factura) | PASS |
| + | XML event carrying documentoAsociadoId stays on the normal PromoverAsync path (critical regression) | Unit EsDocumentoAsociado_EsFalso_CuandoEsXmlConDocumentoAsociadoId; E2E ProcesarPendientesAsync_PromotesASufficientPayload_ToAPendienteValidacionFactura (PayloadCompleto XML + documentoAsociadoId 2, asserts PROMOVIDO, 1 Factura PENDIENTE_VALIDACION, 1 DocumentoFactura, total factura count 1). Predicate DocumentoAsociadoId is not null AND TipoDocumento equals PDF confirmed in PoliticaDeDocumentoAsociado.cs lines 16-17 | PASS |
| + | Infinite-defer termination (design finding 4) | ResolverParAsync_ReturnsParNoPromovible_WhenPartnerFacturaWasDiscardedAfterPromotion: Query A empty + event PROMOVIDO becomes ParNoPromovible becomes Descarta, terminates | PASS |

## Spec Compliance Matrix - pantalla-detalle-validacion (1 requirement, 4 scenarios)

| # | Scenario | Covering test | Status |
|---|----------|---------------|--------|
| 1 | Opening a factura with a rendered document (unchanged) | visor-documento.spec.ts: renders the first document by default same-origin (still green) | PASS |
| 2 | Factura with an XML and a PDF document, PDF selected by default | selects the PDF by default when the list has an earlier non-renderable XML row (asserts iframe src /api/documentos/ingesta-pdf/contenido) | PASS |
| 3 | Factura with only a non-renderable document, selects it, placeholder unchanged | falls back to documentos[0] when no document in the list is inline-renderable | PARTIAL: asserts selection lands on the XML row; does not additionally assert the download/placeholder affordance (unchanged UI, covered by pre-existing tests) |
| 4 | Factura with multiple documents, selector offered, one default | offers a selector to switch between multiple documents; keeps an explicit user selection even when a renderable document exists | PASS |

## ADR / Guardrail Checks

| Check | Evidence | Result |
|-------|----------|--------|
| ADR 0003: new SQL runs under usr_api grants only | PermissionSufficiencyTests.UsrApi_CanSelect_QueryA_DocumentoFacturaJoinFactura and UsrApi_CanSelect_QueryB_InboxEventPayloadJsonValue green under usr_api; UsrApi_IsDenied_SelectOnProcesamiento green | PASS |
| ADR 0003: no new access to fact.Procesamiento or fact.DocumentoRecibido | Query A touches only fact.DocumentoFactura + fact.Factura; Query B only fact.InboxEvent; NoWriteToDboStructuralTests green | PASS |
| ADR 0019: new Core types are pure | SmartNet.Inbox.Core.Tests PurityScanTests green (57/57); ResolucionPar / PoliticaDeDocumentoAsociado / DecisionDocumentoAsociado are records plus a static pure map | PASS |
| Out-of-scope guardrails | git diff --stat plus untracked: change-relevant edits only under SmartNet/SmartNetApi/inbox, SmartNet/SmartNetWeb/src/app/detalle/ui/visor-documento, openspec/changes/pdf-asociado-en-documento-factura. No .sql, no _VERSION bump, no Python/worker file, no backfill migration | PASS |
| Task 2.2 N/A accuracy | grep confirms SqlPromocionRepository is the only implementer of IPromocionRepository; no FakePromocionRepository exists anywhere | PASS |

## Design Coherence

Matches design 281: routing branch in ProcesarPendientesAsync before PoliticaDePromocion.Decidir
(PromocionBackgroundService.cs lines 54-58); Decision 1 corrected predicate; ResolverParAsync runs
Query A then Query B (Decision 2); FusionarDocumentoAsync is one SqlTransaction reusing
InsertarDocumentoFacturaAsync (2601/2627 catch) plus MarcarPromovidoAsync, never InsertarFacturaAsync
(Decision 4); defer is a pure no-op (Decision 3). SPA MIMES_RENDERIZABLES mirrors
DocumentoContenido.MimeAllowList; seleccionado prefers first renderable, falls back to documentos[0],
explicit selection wins.

Deviations (documented in apply-progress, none break a spec): motive strings in ResolverParAsync
chosen by apply phase; task 3.1 two cycles implemented as two ProcesarPendientesAsync calls.

## Issues

CRITICAL: none.

WARNING:
1. Scenario 3 (order independence): no single end-to-end test drives the full PDF-first, defer,
   XML-promotes, PDF-merges sequence; legs covered separately. Low risk.
2. Scenario 5 (unassociated PDF regression): covered only at unit level; no E2E promoting a
   structurally-sufficient documentoAsociadoId-null PDF. Sufficiency path unchanged by this work.
3. Pre-existing unrelated failures: RunnerFailureHaltTests.FailingScript_ExitsNonZero (xUnit
   parallelism disposal race) and SesionPurgarTests.DeletesRowsOlderThanTheRetentionWindow
   (retention boundary). Both in test projects with zero overlap with inbox or the viewer; apply
   phase confirmed both fail on a clean git stash. Verifier did not independently re-run git stash.
4. Working tree carries unrelated uncommitted edits (Program.cs, SmartNet.Api.csproj, .gitignore,
   SPRINT.md, skills-lock.json) present at session start; must not be swept into this commit.

SUGGESTION: add a single end-to-end order-independence test and a sufficient-unassociated-PDF E2E
to close the two PARTIAL rows; scenario 3 (viewer) could also assert the download affordance.

## TDD Compliance

- TDD evidence table present in apply-progress 284.
- All implementing tasks have test files (24/24; 2.2 N/A). All 4 test files inspected.
- GREEN reconfirmed on re-run: Core 57/57, Infra 71/71, SPA 479/479.
- Triangulation: 6 Core cases, 7 SqlPromocionRepository cases, 3 E2E cycle cases, 3 SPA cases.
- Assertion quality clean: state + motive-text + row-count assertions; no tautologies. XML-discard
  E2E deliberately asserts MotivoDescarte text to avoid a trivial green.

## Final Verdict

PASS WITH WARNINGS. All 11 scenarios have a passing covering test, both requirements implemented,
all ADR/guardrail checks pass, no CRITICAL issue. Warnings are coverage-completeness observations
and pre-existing/unrelated noise.

Next recommended: sdd-archive
