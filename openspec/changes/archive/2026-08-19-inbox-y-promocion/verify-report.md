# Verify Report: Inbox y promocion (BACKLOG #7)

## Scope verified

- HEAD: feat/inbox-y-promocion-wu6-adr-fix @ 86e7591
- Diff vs main: 93 files changed, +13018/-7
- tasks.md: 49/49 checkboxes done, all 6 work units present.

## Test evidence (real runs, this session)

Python unit: pytest tests/unit -q (SmartNet/worker) -> 177 passed
Python integration: pytest tests/integration -m integracion -q -> 13 passed, 1 deselected
.NET full solution: dotnet test SmartNet.sln -> SmartNet.Inbox.Core.Tests 29/29 pass; SmartNet.Inbox.Infrastructure.Tests 29/29 pass; 3 unrelated pre-existing failures (see Discrepancies)
.NET PurityScanTests isolated -> SmartNet.Inbox.Core.Tests: 6/6 pass (ADR 0019 purity confirmed)
Angular unit: npm test -- --watch=false -> 18/18 pass, 5/5 files
Angular prod build: ng build --configuration production -> succeeds, inbox-page lazy chunk 5.32 kB

## Acceptance criteria (proposal.md Success Criteria)

- [x] Every finished document produces exactly one InboxEvent row - inbox_event_repo.py (INSERT SELECT WHERE NOT EXISTS, D3) + test_cli_inbox.py/test_inbox_event_repo.py.
- [x] Sufficient-data documents produce Factura (PENDIENTE_VALIDACION) with correct indicators and FacturaExtraccion; idempotent re-run - SqlPromocionRepository (catch 2601/2627, D2) + SqlPromocionRepositoryTests double-promotion test.
- [x] Insufficient-data documents produce zero Factura rows, EstadoConsumo=DESCARTADO + MotivoDescarte - PoliticaDePromocion.Decidir + integration tests.
- [x] Angular Inbox screen lists all outcomes from the API - InboxService/InboxList/InboxFilter, GET /api/bandeja.
- [x] SmartNet.Inbox.Core passes PurityScanTests; contract tests confirm .NET reads exactly what Python writes - PayloadInboxContractTests, PayloadInboxParserTests, shared golden JSON fixture.
- [x] ADR 0005 text corrected to match the single-Tipo as-built schema - confirmed below.

## Design decisions D1-D9 vs code

D1 Sufficiency = 4 NOT NULL Factura cols + Procesamiento.Estado=COMPLETADO -> PoliticaDePromocion.cs
D2 INSERT-first, catch 2601/2627, resolve existing FacturaId -> SqlPromocionRepository.cs
D3 Atomic INSERT SELECT WHERE NOT EXISTS, no new unique index -> inbox_event_repo.py
D4 evidencia fuente only, no confianza -> payload_inbox.py _evidencia() - confirmed, no confidence field emitted
D5 5 indicators; EsReferenciaExterna stays DDL default -> IndicadoresFactura.cs (4 bool + tri-state AfectacionMixta, EsReferenciaExterna absent from the record)
D6 Reuse GET /api/bandeja?estado=&orden= -> BandejaEndpoints.cs
D7 .NET BackgroundService+PeriodicTimer(1min)+TimeProvider; Python single-run CLI -> PromocionBackgroundService.cs, cli_inbox.py
D8 No fact.EstadoIntegracion row from cli_inbox.py -> confirmed by inspection, no such write
D9 JSON parsing only in Infrastructure -> PayloadInboxParser.cs; SmartNet.Inbox.Core has zero PackageReference, confirmed by PurityScanTests

Open Questions (design.md) all resolved and reflected in code: Angular workspace bootstrapped (WU5, SmartNet/spa/); D5 (5 indicators) implemented; D4 (no confianza) implemented.

## CLAUDE.md / ADR compliance

- ADR 0019 (nucleo sin infraestructura): SmartNet.Inbox.Core.csproj has zero PackageReference; PurityScanTests (6/6) pass against the full assembly.
- ADR 0003 (particion de datos): PermissionSufficiencyTests (real usr_api/usr_worker logins) confirm usr_api denied on fact.Procesamiento, usr_worker denied on fact.Factura/fact.FacturaExtraccion (all 4 verbs); grep across SmartNet/ found no cross-partition usr_worker/usr_api writes outside the existing, expected grant files/tests. NoWriteToDboStructuralTests confirms SqlPromocionRepository only touches dbo.Proveedor via SELECT, and no Infrastructure adapter writes dbo.
- Esquema SQL versionado: no new migration files; item #7 reuses items #1/#3 DDL as scoped - confirmed no new SmartNet/db/schema/*.sql files in the diff.
- Dominio contable en espanol: EventoInbox, PoliticaDePromocion, DecisionPromocion, CalculoDeIndicadores, IndicadoresFactura, ConstruccionDeFactura, FacturaPromovida - all Spanish domain names, English technical scaffolding (SmartNet.Inbox.Core, BackgroundService), consistent with CONVENTIONS.md.

## ADR 0005 correction (WU6)

Confirmed corrected in place (adrs/0005-frontera-de-promocion-de-documento-procesado-a-factura.md): single Tipo=PROCESAMIENTO_FINALIZADO literal (line 35-36, explicit "Corregido en el item #7" note replacing the old two-literal text), and the indicator section now states 5 indicators computed with EsReferenciaExterna left at its DDL default (line 114-117, same pattern). Matches D5/code.

## Discrepancies found

### WARNING - specs were not updated to match resolved design deltas D4/D5

design.md Open Questions explicitly resolved two deltas against the original spec text, but the delta spec files under openspec/changes/inbox-y-promocion/specs/ were never edited to match, even though ADR 0005 and the code were corrected:

- specs/factura-promotion/spec.md - "Requirement: Pure promotion decision" (line 14) and the "Complete comprobante data promotes successfully" scenario (line 40) still say "6 indicator flags"; code computes 5 (D5, confirmed, matches ADR 0005 corrected text).
- specs/inbox-event-publishing/spec.md - line 24 still lists evidence as Fuente/confianza; code (payload_inbox.py) emits only fuente (D4, confirmed).
- specs/inbox-screen/spec.md - "Requirement: List all InboxEvent outcomes" (line 15) and its scenario (line 22) still say "6 indicator flags"; the Angular chip renderer (inbox-list.ts) renders 4 boolean chips + 1 tri-state afectacionMixta chip = 5, matching D5.

This does not block the change - code, ADR 0005, and design.md Open Questions are internally consistent with each other, and the discrepancy is confined to spec prose that predates the Open-Questions resolution. But the spec files are now stale relative to the as-built system and should be corrected in the same spirit as ADR 0005 was (WU6), ideally before/at archive, so a future reader of the spec alone does not reintroduce the "6 indicators"/"confianza" mistake.

### WARNING - 3 unrelated, pre-existing flaky test failures during full-solution run

Running dotnet test SmartNet.sln produced 3 failures outside item #7 scope: SmartNet.Catalogos.Infrastructure.Tests.SqlOrigenRepositoryTests.ListarAsync_ReturnsAllSeededRows, SmartNet.Api.Tests.EscalationEndToEndTests.EscalationSequence_LockA15Min_Margin_LockB30Min_ThroughTheRealApiAndDb, SmartNet.Db.Runner.Tests.BaseDataTests.MotivoAtributo_DoesNotReclassify_MotivesNotMarkedInTheSourceDocument. All three fail with SqlException "User does not have permission to alter database fact_test_(guid) ... or you do not have permission" during ephemeral test-DB teardown - a connection-pool/parallel-test-collision symptom against the shared local SQL Server instance, not a code defect. Confirmed pre-existing and unrelated:
- SqlOrigenRepositoryTests.cs was last touched by item #6 (4f7d270), not by this change.
- Re-running SqlOrigenRepositoryTests in isolation passes cleanly (2/2).
- SmartNet.Inbox.Core.Tests (29/29) and SmartNet.Inbox.Infrastructure.Tests (29/29) - the two test projects item #7 actually owns - are fully green in the same full-solution run.

Not a CRITICAL for this change; flagged as an environment note (parallel xUnit collections sharing one local SQL Server instance for ephemeral DB names) worth fixing separately, not blocking archive of item #7.

### No CRITICAL issues found.

## Verdict

PASS WITH WARNINGS - all 49/49 tasks complete and verified against real test runs; all 6 proposal acceptance criteria met; all 9 design decisions (D1-D9) match the shipped code; ADR 0005 correctly updated; ADR 0019 purity and ADR 0003 data-partition boundaries independently confirmed by dedicated, executed tests. Two WARNINGs recorded above (stale spec prose vs. resolved design deltas; pre-existing unrelated test flakiness) - neither blocks archiving this change, but the spec-prose staleness should be fixed (either now or explicitly deferred) so it does not become a future silent-discrepancy trap per CLAUDE.md rule 1.
