```yaml
schema: gentle-ai.verify-result/v1
evidence_revision: sha256:a6759eef18ca72b3b4150f155b756177921cd0661b521e806b7f4405d5599a40
verdict: pass_with_warnings
blockers: 0
critical_findings: 0
requirements: 11/11
scenarios: 27/27
test_command: SmartNetWeb npx ng test --watch=false then SmartNetApi dotnet test inbox and api
test_exit_code: 0
test_output_hash: sha256:521f2b8bce056cadca7a7133894ee97e12c7b71fc5d43bbc63d29d92ec298101
build_command: SmartNetWeb npm run lint then npm run build
build_exit_code: 0
build_output_hash: sha256:7735c2b7de2d4f3b8475f895973f00731427bb1cc3545df0abdb3de4b9372933
```

## Verification Report

**Change**: item-21-bandeja-shell-nav
**Mode**: Strict TDD

### Completeness
Tasks total 32. Substance of all 32 verified via source inspection plus green tests plus git
history (commits a93f4c7, a83c5ee, 84c05e4, groundwork cafa478). tasks.md checkboxes: 0 ticked
(WARNING, bookkeeping only). apply-progress obs 232 documents all 3 phases done.

### Build and Tests Execution
Build PASS: npm run lint (tsc, no ESLint per project convention) exit 0; npm run build exit 0, no
anyComponentStyle budget warning. Component styles: shell-layout.css 1368 B, sidebar.css 3041 B,
inbox-resumen.css 776 B, inbox-list.css 1495 B, all under 4 kB.

Tests PASS:
- SPA ng test (Vitest): 39 files, 379 of 379 passed, 0 skipped.
- SmartNet.Inbox.Infrastructure.Tests (real local SQL Server): 48 of 48 passed.
- SmartNet.Api.Tests (WebApplicationFactory plus real DB plus real session): 164 of 164 passed.

Coverage: not collected, no coverage tool configured (informational).

### Spec Compliance Matrix
All 11 requirements / 27 scenarios COMPLIANT (covering test passed at runtime this session):

- shell-nav only routed destinations (Bandeja, Configuracion) + active state: sidebar.spec.ts
  "renders exactly the two routed destinations", "links to the existing routes"; shell-layout.spec.ts.
- shell-nav single hairline divider: sidebar.spec.ts "separates the primary and utility groups
  with exactly one hairline divider".
- shell-nav expanded by default + collapsible (216/60): sidebar.service.spec.ts "starts expanded",
  "alternar flips"; shell-layout.spec.ts "starts expanded with no stored preference and collapses".
- shell-nav localStorage fact.sidebar persistence + tampered fallback: sidebar.service.spec.ts
  "starts from the stored preference", "a tampered localStorage value resolves to expandido, not
  an error".
- shell-nav div-only glyphs: sidebar.spec.ts "builds glyphs from div only, no svg, img, icon font".
- shell-nav AA over fondo-sidebar both themes: carried by existing contraste.spec.ts, unchanged
  and green (fondo-sidebar already in SUPERFICIES).
- shell-nav shell CSS layout-only + budget: npm run build (no budget warning); paleta.spec.ts
  unchanged and green; every color via var token confirmed by inspection.
- bandeja rows carry comprobante fields (6 fields; INCIDENCIA all null; TipoComprobante as code):
  SqlBandejaRepositoryTests ProjectsEnrichedComprobanteFields_FromFacturaAndProveedor,
  ProveedorNombreIsNull_WhenCodproIsAbsentFromCatalog, EnrichedFieldsAreNull_ForIncidenciaRows;
  BandejaEndpointsTests asserts code 01.
- bandeja estado aggregate over wider predicate (global; PROMOVIDO in validadas; partition sums to
  total; error+alert counts as conError): SqlBandejaRepositoryTests
  BucketsPartitionTheSet_AndCountPromotedRowsInValidadas,
  FirstMatchPrecedence_ErrorBeatsAlerta_DescartadoBeatsError,
  IsIdenticalAcrossFilterAndPaginationParameters, WidenedBatch_RunsAsUsrApi;
  BandejaEndpointsTests CarriesEnrichedComprobanteFields_AndAGlobalResumen.
- visual-bandeja inbox-list section-2 table + derived Estado chip (10 columns in order; client
  comprobante map; dash for null factura cells; component-scoped tabular class on F.emision and
  Monto not global tabular-nums; chip precedence and chipsDe column unchanged): inbox-list.spec.ts
  "renders the handoff section 2 compras columns in order", "renders the compras cells for a
  FACTURA row", "maps 01 to Factura and 03 to Boleta", "renders an unknown comprobante code
  verbatim", "renders dash in every factura-only cell for an INCIDENCIA row", "gives the date and
  monto cells a component-scoped tabular-figures class, not the global one", "widens the empty
  state colspan to 10"; item-20 chipEstadoDe precedence tests unchanged and green.
- visual-bandeja inbox-page global summary cards (4 cards; display-only; hidden before first
  load): inbox-resumen.spec.ts "renders exactly four cards", "shows each bucket value from the
  input", "does not render descartadas or total", "is display-only: no button, no output";
  inbox-page.spec.ts "renders the four global summary cards not derived from items", "keeps the
  card numbers stable when a filter changes", "renders no summary strip before the first load
  completes"; inbox.service.spec.ts "exposes the global resumen aggregate null before first load".

spa-design-tokens carries 0 requirements, documented no-op; src/styles.css confirmed untouched.

### Correctness (Static Evidence)
- SqlBandejaRepository widening: Implemented. LEFT JOIN dbo.Proveedor on resultset 2 only; new
  aggregate resultset 3 with NO WHERE clause, CASE order equals chip precedence, unfiltered EXISTS
  on fact.ProcesamientoError per D2b. FiltroWhere, the pagina INSERT, and the fallback count are
  untouched. Reader ordinals correctly shifted by 6.
- IBandejaRepository contract: Implemented. 6 nullable fields after RucProveedor; ResumenBandeja
  record; required positional Resumen on PaginaBandeja of T so stale sites become compile errors.
- cs to ts contract drift: None. ResumenBandeja Pascal vs camel 1:1
  (pendientes/validadas/conError/alertas/descartadas/total); 6 enriched fields 1:1; DateOnly
  nullable serializes yyyy-MM-dd matching fechaEmision string-or-null.
- BandejaEndpoints.cs: Unchanged, confirmed via diff.
- ADR 0003: Respected. No dbo write; dbo.Proveedor join proven under real usr_api by
  ListarAsync_WidenedBatch_RunsAsUsrApi_ProvingProveedorAndAggregateGrants via ExecuteAsUserAsync.
- ADR 0016: Respected. SmartNetBD schema untouched in the diff, no new SQL script, no new grant.
- ADR 0009: Respected. SidebarService and InboxService.resumen use private signal plus asReadonly;
  no state library.
- CONVENTIONS: Respected. Spanish domain nouns; no accents in identifiers; tsc is the gate.
- Frozen specs: paleta.spec.ts and contraste.spec.ts NOT modified. app.routes.spec.ts appears in
  the diff only from prior-session groundwork cafa478, not from a93f4c7..HEAD; all three green.
- Project rule 1: Done. spa-visual-bandeja Purpose and Out of Scope updated to unfreeze the
  item-13 query and inbox.service.ts for the enriched fields; bandeja spec carries the D2b OBSOLETO
  asymmetry note.

### Coherence (Design)
D1 required Resumen: Yes. D2 third resultset no WHERE, CASE equals chip precedence: Yes. D2b
unfiltered EXISTS: Yes. D3 join projection-only: Yes. D4 6 nullable fields on shared base: Yes.
D5 and D5b presentational sidebar plus div glyphs, no pre-bootstrap applier, main.ts untouched:
Yes. D6 no new token, zero styles.css delta: Yes. D7 additive 10-column row, colspan 5 to 10,
order Recibido F.emision Proveedor Tipo Numero Monto Estado Detalle Indicadores Acciones: Yes.
D8 4 display-only cards, no output, descartadas and total not rendered: Yes. D9 one indented
BACKLOG note, checkbox line intact: Yes.

### TDD Compliance
- TDD Evidence reported: WARNING. apply-progress narrates completion but has no per-task
  RED/GREEN/TRIANGULATE/SAFETY-NET table.
- All tasks have tests: PASS. Every GREEN task has a matching spec file with new cases.
- RED confirmed (tests exist): PASS.
- GREEN confirmed (tests pass): PASS. 379 plus 48 plus 164 equals 591 tests green this session.
- Triangulation adequate: PASS. comprobante map 01/03/unknown/null; buckets 5-way partition plus
  precedence pairs; tampered storage valid/empty/invalid.
- Safety Net for modified files: PASS. Full suites re-run green; no pre-existing test weakened.

### Test Layer Distribution
Unit ~20 new (sidebar.service, inbox.service, inbox-resumen) via Vitest. Integration ~25 new
(sidebar, shell-layout, inbox-list, inbox-page on jsdom; SqlBandejaRepositoryTests,
BandejaEndpointsTests on real SQL Server) via Vitest and xUnit plus WebApplicationFactory.
E2E 0, not installed, accepted.

### Assertion Quality
No tautologies, ghost loops, or production-code-free assertions in the new tests. The
INCIDENCIA-row loop in inbox-list.spec.ts iterates a fixed literal list of testids, never empty.
The inbox-resumen display-only test asserts absence of button and output AND presence of the 4
card values. Assertion quality: all assertions verify real behavior.

### Quality Metrics
Linter: N/A, project has no ESLint by design; tsc noEmit on both tsconfigs exit 0.
Type Checker: PASS, 0 errors.

### integration-spa-api seam check
Local SQL Server reachable this session. The seam is covered by
BandejaEndpointsTests.GetBandeja_CarriesEnrichedComprobanteFields_AndAGlobalResumen, ran green.
SPA PaginaBandeja resumen (camelCase ResumenBandeja) and the 6 enriched per-item keys match the
API JSON 1:1; fechaEmision string form matches DateOnly. Seam PASS, not fabricated: real DB, real
WebApplicationFactory, real session cookie.

### Issues Found
CRITICAL: None.
WARNING:
1. tasks.md has 0 of 32 checkboxes ticked, and Engram obs 228 likewise. Work is complete (commits
   plus green suites) but the boxes were never marked. Orchestrator MUST reconcile tasks.md before
   sdd-archive; OpenSpec archive validation expects a complete task list.
2. apply-progress obs 232 has no structured per-task TDD Cycle Evidence table; RED-before-GREEN
   ordering is inferred from commit content and scenario-test presence.
3. SqlBandejaRepository.ListarConConexionAsync is now internal static, exposed for the usr_api
   impersonation test. Minor surface widening consistent with the existing ListarAsync split; no
   functional risk.
SUGGESTION:
1. Add an explicit bandeja spec scenario for the D2b OBSOLETO asymmetry.
2. No SPA coverage tool configured; adding one would enable changed-file coverage reporting.

### Verdict
PASS WITH WARNINGS. All 11 requirements and 27 scenarios are covered by tests that pass at runtime
(SPA 379/379, Inbox 48/48, Api 164/164); lint and build are clean; every design decision is
honored; ADR 0003, 0009, 0016, CONVENTIONS, and project rule 1 hold. The only blocker to archive
is bookkeeping: tasks.md and Engram obs 228 must have all 32 checkboxes marked complete before
sdd-archive runs.
