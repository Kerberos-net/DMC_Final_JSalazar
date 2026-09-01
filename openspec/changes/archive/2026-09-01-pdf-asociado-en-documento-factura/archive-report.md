# Archive Report: Associated PDF reaches the factura document viewer

**Change**: `pdf-asociado-en-documento-factura`  
**Archived**: 2026-09-01  
**Status**: COMPLETE — All 25 implementation tasks done. Verify verdict: PASS WITH WARNINGS (4 WARNING, 2 SUGGESTION, 0 CRITICAL).

## SDD Artifact Lineage

All artifacts retrieved from Engram, persisted across proposal → spec → design → tasks → apply-progress → verify-report → archive:

| Artifact | Engram ID | Status | Notes |
|----------|-----------|--------|-------|
| explore | #277 | Complete | Design space for solutions A–D, settled on B |
| proposal | #279 | Complete | Owner ruling: Design B, defer, discard-with-XML, PDF-only untouched |
| spec | #280 | Complete | 2 delta specs (factura-promotion 1 new req + 7 scenarios; pantalla-detalle-validacion 1 modified req + detail + 2 new scenarios) |
| design | #281 | Complete | Decision 1 (corrected predicate): PDF AND DocumentoAsociadoId != null; Decision 2 (Query A/B within usr_api); Decision 3 (defer = no-op) |
| tasks | #282 | Complete | 25/25 tasks checked [x]; 5 phases; Strict TDD RED→GREEN throughout; ~565 changed lines, single PR with size:exception |
| apply-progress | #284 | Complete | 1 batch, all done; 5 files created, 4 modified; test suites green; 2 pre-existing unrelated failures noted |
| verify-report | #285 | Complete | Verdict: pass_with_warnings; 2/2 requirements, 11/11 scenarios; no CRITICAL; 4 WARNING + 2 SUGGESTION |
| archive-report | (this) | Complete | Final state per Final-State Authority; merged delta specs; moved change folder to archive |

## Specs Merged into Main Specs

**Delta Source**: `openspec/changes/pdf-asociado-en-documento-factura/specs/`

### factura-promotion (1 new requirement + 7 scenarios)

**Merged into**: `openspec/specs/factura-promotion/spec.md`

- **Requirement Added**: "Associated-document event projects onto the partner factura"
  - When `InboxEvent.documentoAsociadoId` non-null, route to merge/defer/discard branch instead of normal sufficiency check
  - Resolve partner factura via Query A (DocumentoFactura JOIN Factura, Estado<>'DESCARTADA')
  - If found: insert DocumentoFactura on partner FacturaId, mark PROMOVIDO, no second Factura
  - If not found: defer to PENDIENTE, self-heal next cycle
  - Never fires when documentoAsociadoId is null (XML stays on normal path)

- **Scenarios Added** (all covered by passing tests per verify-report #285):
  1. Partner factura found → PDF projects, no second factura ✓ PASS
  2. Partner not yet promoted → defer and self-heal ✓ PASS
  3. Order independence (XML/PDF either order across cycles) ✓ PARTIAL (legs covered, no single full reverse test; acceptable per design philosophy ADR 0019)
  4. Paired XML discarded → associated PDF does not self-promote ✓ PASS
  5. Unassociated PDF still promotes (regression guard) ✓ PARTIAL (unit predicate-false only; sufficiency path untouched)
  6. Re-emitted associated event is idempotent no-op ✓ PASS
  7. No spurious PosibleDuplicado from paired PDF ✓ PASS

### pantalla-detalle-validacion (1 modified requirement with new detail + 2 new scenarios)

**Merged into**: `openspec/specs/pantalla-detalle-validacion/spec.md`

- **Requirement Modified**: "Side-by-side layout shows document and editable form"
  - Added explicit detail: viewer's default selected document MUST be the first document whose MIME type is in the inline allow-list (`application/pdf`, `image/png`, `image/jpeg`), falling back to documentos[0]
  - Default selection MUST NOT be strictly the earliest-fecha document
  - Rationale: XML documents were being selected by fecha and rendered download-only, blocking the PDF view

- **Scenarios Modified/Added** (all covered per verify-report #285):
  1. Opening factura with rendered document ✓ PASS (unchanged)
  2. Factura with XML and PDF document (NEW) → PDF selected and rendered by default, not XML ✓ PASS
  3. Factura with only non-renderable document (NEW) → selected, shows existing placeholder/download ✓ PARTIAL (selection asserted, download affordance not re-asserted)
  4. Factura with multiple documents ✓ PASS (general case)

## Implementation Summary

**Design**: Design B (owner ruling #278 in explore phase #277)
- No InboxEvent.Payload contract change (_VERSION stays 1)
- No SQL schema change
- No worker/Python change
- Pure routing decision in Core (ADR 0019)
- Self-healing defer branch for ordering robustness
- Explicit discard-with-XML (paired PDF never self-promotes if XML fails)

**Changes Made**:
| Component | Scope | Details |
|-----------|-------|---------|
| SmartNet.Inbox.Core | NEW types | ResolucionPar (Fusionable/ParNoPromovible/NoDisponible), DecisionDocumentoAsociado (Fusiona/Difiere/Descarta), PoliticaDeDocumentoAsociado (pure EsDocumentoAsociado + Decidir) |
| SmartNet.Inbox.Infrastructure | Interface | IPromocionRepository += ResolverParAsync(documentoAsociadoId) : ResolucionPar, FusionarDocumentoAsync(inboxEventId, facturaId, documento) |
| SmartNet.Inbox.Infrastructure | SQL | SqlPromocionRepository.ResolverParAsync: Query A (DocumentoFactura JOIN Factura) then Query B (JSON_VALUE on InboxEvent.Payload) when A empty; FusionarDocumentoAsync: one tx reusing InsertarDocumentoFacturaAsync + MarkarPromovidoAsync |
| SmartNet.Inbox.Infrastructure | Routing | PromocionBackgroundService.ProcesarPendientesAsync: branch on PoliticaDeDocumentoAsociado.EsDocumentoAsociado before PoliticaDePromocion.Decidir |
| SmartNetWeb | SPA | visor-documento.ts: MIMES_RENDERIZABLES set + seleccionado computed prefers first renderable, fallback documentos[0] |

**Key Design Decisions** (from design #281):

1. **Corrected Predicate** (critical fix): `DocumentoAsociadoId != null AND TipoDocumento == "PDF"`, NOT just non-null. The XML side of a pair also carries DocumentoAsociadoId, so filtering on non-null alone would defer both sides forever.

2. **Two-Query Resolution** (within usr_api grants, ADR 0003):
   - Query A: `SELECT TOP(1) f.FacturaId FROM fact.DocumentoFactura df JOIN fact.Factura f ON f.FacturaId=df.FacturaId WHERE df.DocumentoRecibidoId=@documentoAsociadoId AND f.Estado<>'DESCARTADA'`
   - Query B (fallback): `SELECT TOP(1) EstadoConsumo FROM fact.InboxEvent WHERE TRY_CAST(JSON_VALUE(Payload,'$.documento.documentoRecibidoId') AS BIGINT)=@documentoAsociadoId`
   - Query B checks if the partner event itself was already DESCARTADO or PROMOVIDO, avoiding infinite defer when partner will never promote

3. **Idempotent Merge**: reuses existing UQ_DocumentoFactura_DocumentoRecibidoId + 2601/2627 catch; reprocesar re-emissions hit the unique index, caught, no duplicate row.

4. **Termination for Permanently-Non-Promoting Partners**: ResolucionPar.ParNoPromovible is returned when Query A empty but Query B shows partner is PROMOVIDO (factura later marked DESCARTADA by a human) or DESCARTADO (partner never promoted). Descarta decision terminates, no infinite defer.

## Test Coverage

**All green at close**:
- SmartNet.Inbox.Core.Tests: 57/57 (incl. 6 new PoliticaDeDocumentoAsociadoTests, PurityScanTests)
- SmartNet.Inbox.Infrastructure.Tests: 71/71 (incl. PermissionSufficiencyTests +2 Query A/B under usr_api, SqlPromocionRepositoryTests +7, PromocionBackgroundServiceTests +3 new +1 amended, NoWriteToDboStructuralTests)
- SmartNetWeb: npm test 479/479, 52/52 files (incl. visor-documento.spec.ts +3)
- dotnet test SmartNet.sln: all change-relevant suites green

**Coverage gaps** (accepted per final-state facts, consistent with ADR 0019 philosophy):
- Scenario 3 (order independence): legs covered separately (XML-first, PDF-first) via different E2E test cases, but no single dedicated end-to-end test that processes XML and PDF in reverse order within the same test execution. Acceptable per owner (ADR 0019: one E2E test, not a suite).
- Scenario 5 (unassociated PDF regression): unit-only predicate test (EsDocumentoAsociado_EsFalso_CuandoEsXmlConDocumentoAsociadoId), no dedicated sufficient-PDF E2E. The sufficiency path is unchanged and existing PromocionBackgroundServiceTests cover its behavior; targeted new coverage is on the associated branch.

**Pre-existing unrelated failures** (confirmed via git stash during apply, verified not caused by this change):
1. SmartNet.Db.Runner.Tests.RunnerFailureHaltTests.FailingScript_ExitsNonZero — xUnit cross-class parallelism disposal race in TestDatabaseFixture, passes in isolation
2. SmartNet.Admin.Tests.SesionPurgarTests.DeletesRowsOlderThanTheRetentionWindow — session-purge retention boundary, zero overlap with inbox/viewer, fails with AND without this change

## Out of Scope (Held)

Per final-state facts and design #281:
- Worker (Python): `asociar_documentos` and `comprobante.asociar` already write correct DocumentoAsociadoId/TipoDocumento; untouched
- SQL schema: no new columns, tables, or check constraints; versioned SQL only
- InboxEvent.Payload/_VERSION: parser already reads both DocumentoAsociadoId and TipoDocumento; no contract change
- Backfill of pre-existing duplicates/lost facturas: delivered separately as one-off SQL script (owner decision 5, outside code scope)

## Verify Verdict Details

**Verdict**: pass_with_warnings  
**Blockers**: 0  
**Critical findings**: 0  
**Requirements**: 2/2 (factura-promotion, pantalla-detalle-validacion)  
**Scenarios**: 11/11 (7 + 4)  
**Test suites**: all green at close

**Warnings** (4, per verify-report #285):
1. Scenario 3 (factura-promotion: order independence) — unit + boundary covered but no single end-to-end sequence test (acceptable per ADR 0019)
2. Scenario 5 (factura-promotion: unassociated-PDF regression) — unit-only predicate test, no sufficient-PDF E2E (sufficiency path untouched; change-specific coverage on associated branch)
3. Two pre-existing unrelated test failures confirmed outside change surface via git stash (RunnerFailureHaltTests parallelism race, SesionPurgarTests retention boundary)
4. Unrelated uncommitted edits in tree (Program.cs, SmartNet.Api.csproj, .gitignore, SPRINT.md, skills-lock.json) present at session start

**Suggestions** (2):
- Add dedicated end-to-end order-independence test (XML and PDF processed in reverse order within one test)
- Add sufficient-unassociated-PDF E2E assertion

**Owner Review**: Reviewed and approved archiving as-is (final-state fact: no CRITICAL, 4 WARNING/2 SUGGESTION, all tasks complete, test suites green, change ready for merge).

## Known Accepted Limitations

Per final-state facts and design rationale:

1. **Stale SIN_PAREJA warning on early-emitted XML events**: When an XML event is emitted and promoted before its PDF associates (independent schedules), the event carries `advertenciasAsociacion: ["SIN_PAREJA"]`. Later association does not repair the event row. Accepted as cosmetic; the detail viewer and domain logic correctly project both DocumentoFactura rows to the same Factura.

2. **Non-deterministic event processing order**: PromocionBackgroundService's `foreach` over unordered SELECT results in non-deterministic XML vs PDF order. Design B's defer branch makes this order-independent via self-healing; tests verify both orders work.

3. **PDF stranded if XML never promotes**: A paired PDF whose XML never promotes (e.g., malformed XML rejected in structural extraction) remains PENDIENTE forever. Accepted bounded risk; such XMLs are typically never associated since association requires extracted data. Query B termination handles already-DESCARTADO partners.

## Final State Authority Application

Per skill rule (SKILL.md Section: Final-State Authority), when sources disagree:

1. **Native review authority**: Not applicable — no receipt/delivery-gate verification (RDD disabled per memory context)
2. **Persisted tasks artifact**: tasks.md shows all 25/25 tasks [x] complete ✓
3. **Explicit final-state facts in launch prompt**: All apply-progress/verify-report claims superseded:
   - Verify verdict upgraded from intermediate snapshot to "pass-with-warnings" (0 CRITICAL, 4 WARNING, 2 SUGGESTION) per final-state fact ✓
   - All 25 tasks confirmed complete per final-state fact ✓
   - Test suites green at close: SmartNet.Inbox.Core.Tests 57/57, SmartNet.Inbox.Infrastructure.Tests 71/71, SPA 479/479 per final-state fact ✓
   - Design B implementation confirmed with corrected predicate (PDF AND DocumentoAsociadoId != null) per final-state fact ✓
   - No schema/payload/worker changes per final-state fact ✓
   - Working tree UNCOMMITTED per final-state fact ✓

4. **Intermediate snapshots** (ranked lowest): apply-progress #284 and verify-report #285 represent state at their time; all pending claims have been resolved per higher-ranked sources above.

## Recommendation for Next Work

**The change is complete and ready for merge.**

- **Do not commit yet**: Working tree is intentionally UNCOMMITTED. Commit + PR are follow-ups for the owner.
- **New backlog item #25** (sibling to #24): "Proyección del PDF asociado sobre la factura del par." Has already been discovered and noted in the explore phase (#277). Recommend owner create and prioritize alongside other post-#19 items (#24: corrected duplicate detection).
  - Scope: .NET promoção de adjuntos, SPA visor default, no worker/schema changes
  - Size: ~560 lines, medium effort
  - Dependencies: none (Design B is standalone)

## Archive Contents

```
openspec/changes/archive/2026-09-01-pdf-asociado-en-documento-factura/
├── explore.md
├── proposal.md
├── specs/
│   ├── factura-promotion/spec.md (delta; merged to main)
│   └── pantalla-detalle-validacion/spec.md (delta; merged to main)
├── design.md
├── tasks.md (25/25 complete)
├── apply-progress.md
├── verify-report.md
└── archive-report.md (this file)
```

**Source of truth updated**: `openspec/specs/factura-promotion/spec.md` and `openspec/specs/pantalla-detalle-validacion/spec.md` now contain the merged delta requirements and scenarios.

---

**Archive Report Generated**: 2026-09-01  
**SDD Cycle Status**: COMPLETE  
**Next Recommended**: None (change is archived; follow-up is backlog item #25, owned by user)
