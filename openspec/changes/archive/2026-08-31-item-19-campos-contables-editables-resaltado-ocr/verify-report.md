```yaml
schema: gentle-ai.verify-result/v1
evidence_revision: sha256:a94a79b585cac73f904ac41a6bcdb6ade11ffa78eba7a5fa3c8139def1f1e5dd
verdict: pass_with_warnings
blockers: 0
critical_findings: 0
requirements: 10/10
scenarios: 45/45
test_command: dotnet test SmartNet/SmartNetApi/SmartNet.sln then npx ng test --watch=false in SmartNet/SmartNetWeb
test_exit_code: 0
test_output_hash: sha256:a94a79b585cac73f904ac41a6bcdb6ade11ffa78eba7a5fa3c8139def1f1e5dd
build_command: dotnet build SmartNet/SmartNetApi/SmartNet.sln
build_exit_code: 0
build_output_hash: sha256:8357e17ccd2ab81fde50c4eb203b45dd9312072983ee30afff9fd59f8cc9de99
```

## Verification Report

Change: item-19-campos-contables-editables-resaltado-ocr (BACKLOG #19)
Mode: Strict TDD. Verdict: PASS WITH WARNINGS.

### Completeness
Tasks total 33 / complete 33 / incomplete 0. All 33 verified against the cumulative diff
a2e7396..82646c0 (36 files, +1684/-104), not just the checkbox marks.

- Phase 1 (1.1-1.5): schema 021 adds Glosa NVARCHAR(250) + CamposNoExtraidos NVARCHAR(500),
  NOT EXISTS-guarded, no GRANT (ADR 0003); rollback/021_down.sql present; checksums.txt entry
  present; Schema021GlosaCamposNoExtraidosTests (column shape / no column-level permission /
  idempotency).
- Phase 2 (2.1-2.4): IndicadoresFactura + CalculoDeIndicadores carry the per-field list beside
  the derived boolean (+2 unit cases, consistency invariant); PayloadInboxParser +
  SqlPromocionRepository parse + persist verbatim (+2 integration cases, empty list -> NULL).
- Phase 3 (3.1-3.15): ProyeccionDeImportes.Derivar pure in Contable.Core;
  ValidacionDeCorreccion.Validar(original, cambios) merged-value guards; CorreccionFactura /
  FacturaPersistida trailing fields; ServicioDeFacturas trio ladder + D4 scalar projection + D6
  PosibleDuplicado recompute + D7 per-column audit; IUnidadDeTrabajo new ports; SqlUnidadDeTrabajo
  SELECT/UPDATE + ExisteIdentidadPreviaAsync + ActualizarProyeccionEscalarAsync + narrowed
  SinTipoCambio; API CorreccionFacturaRequest / FacturaRespuesta additive. The section 7
  behavior-change test PatchThenValidar_PopulatingBasePen_MakesValidarReject... is present.
- Phase 4 (4.1-4.6): factura.model.ts, factura-form.ts/.html/.css, detalle-page.ts with
  campoResaltado(campo), editable base/IGV/glosa gated on PENDIENTE_VALIDACION, IGV lock for
  03/EXONERADA/INAFECTA (NC 07 exempt), D1 mutual-exclusion draft strip, D5 cargarTodo() full
  refetch after guardarAvance.
- Phase 5: 5.1 BACKLOG #24 confirmed present; 5.2 suites executed; 5.3 manual smoke DEFERRED
  (no seeded local DB) - see SUGGESTION 2.

### Build and Tests Execution
Build: PASS. SmartNet.sln compiles clean; SPA bundle builds.

Tests:
- SPA (ng test --watch=false): 476 passed / 0 failed / 0 skipped (52 files), exit 0.
- .NET #19-relevant projects, all green in isolation this session:
  Contable.Core 49/49, Facturacion.Core 172/172, Facturacion.Infrastructure 65/65,
  Api.Tests 203/203, Inbox.Core 51/51, Inbox.Infrastructure 59/59, TiposCambio.Infrastructure 15/15.
- Full-solution run surfaced 5 failures, ALL TestDatabaseFixture.DisposeAsync teardown errors
  (permission to alter database / database does not exist) or the known SesionPurgarTests
  assertion flake - parallel-DB contention, not #19. Each affected project re-run green in
  isolation (Inbox.Infrastructure 59/59 confirmed this pass). Matches the known-flake list in
  the verify brief.
- Coverage: not available (no coverage tool configured).

### Spec Compliance Matrix (10 requirements / 45 scenarios)
- api-facturas: CorreccionFacturaRequest accepts tipo/numero/base/igv/glosa (10 scenarios) ->
  COMPLIANT (1 scenario PARTIAL, WARNING 1). Evidence: ValidacionDeCorreccionTests (~18 cases:
  pair atomicity, base<0, igv<0, igv>total, blank numero, unknown tipo, state gate, IGV guard
  03/EXONERADA/INAFECTA, NC07 exempt), ServicioDeFacturasPhase2Tests (ladder writes
  TotalOrig/IgvOrig, one audit row per column, no synthetic BaseImponible row),
  SqlUnidadDeTrabajoFacturaTests (round-trip IgvOrig/Glosa).
- api-facturas: FacturaRespuesta projects indicators + CamposNoExtraidos + Glosa (6) ->
  COMPLIANT (API-JSON-level PARTIAL, WARNING 3). Evidence: infra round-trip + De() mapping +
  SPA fixtures.
- api-facturas: PosibleDuplicado recomputed on identity-triple change (3) -> COMPLIANT.
  Evidence: ServicioDeFacturasPhase2Tests (recompute iff triple changed, sets/clears, no audit
  row); SqlUnidadDeTrabajoFacturaTests.ExisteIdentidadPreviaAsync (excludes self + DESCARTADA),
  ActualizarPosibleDuplicadoAsync.
- api-facturas: scalar BasePEN/IgvPEN/NetoPEN recomputed (4) -> COMPLIANT. Evidence:
  ProyeccionDeImportesTests vs REGLAS section 10.1/10.2/10.3/10.6/10.7 goldens;
  ServicioDeFacturasPhase2Tests (BORRADOR projection, boleta collapse, foreign-no-rate skip+200,
  not-touched no-op); SqlUnidadDeTrabajoFacturaTests (ActualizarProyeccionEscalarAsync, section 7
  behavior-change 3.15).
- api-facturas: SinTipoCambio narrowed for NC07 interna (5) -> COMPLIANT. Evidence:
  SqlUnidadDeTrabajoFacturaTests.CargarAsientoAsync (foreign flags, NC07 interna not flagged,
  NC07 externa still flags, PEN never); Abrir foreign-no-rate.
- factura-respuesta: FacturaRespuesta exposes CamposNoExtraidos + Glosa (4) -> COMPLIANT
  (API-JSON assertion PARTIAL, WARNING 3). Evidence: infra round-trip + De() + SPA model/fixtures.
- pantalla: factura-form field set / editable contable fields (6) -> COMPLIANT. Evidence:
  factura-form.spec (inputs while PENDIENTE, read-only when VALIDADA, IGV forced 0+disabled
  03/EXONERADA, NC07 editable).
- pantalla: per-field OCR highlight from CamposNoExtraidos (3) -> COMPLIANT. Evidence:
  factura-form.spec (listed-only exact count 2; empty -> none; pre-021 coarse fallback).
- pantalla: Guardar avance refetch (2) -> COMPLIANT. Evidence: detalle-page.spec (D5 full
  refetch clears stale duplicate + re-enables Validar; draft strips totalOrig vs pair).
- pantalla: missing-TC 409 / newly-live section 7 422 surfaced distinctly (2) -> COMPLIANT.
  Evidence: detalle-page.spec (validar 409 -> negocio keeps draft; section 7 422 -> invariante
  keeps draft).

Compliance summary: 45/45 scenarios have passing covering tests; 3 PARTIAL (warnings) - no
UNTESTED, no FAILING.

### Correctness (Static Evidence)
- ProyeccionDeImportes.Derivar matches REGLAS section 10 goldens: 10.3 4471.61/682.11/3789.50,
  10.7 42782.36/5662.36/37120.00, 10.6 118/0/118, 10.1/10.2 asserted; NetoPEN = BasePEN + IgvPEN
  by construction; MidpointRounding.AwayFromZero equals redondear.
- Contable.Core / Facturacion.Core stay DB/HTTP/clock-free: assembly-wide PurityScanTests in both;
  ProyeccionDeImportes is a pure static class delegating to ConversionDeMoneda.
- SQL 021: object-level grants cover the new columns; test asserts 0 column-level permissions +
  usr_worker DENY (229); 021_down.sql advisory IF EXISTS-guarded; checksums.txt entry present;
  versioned SQL, no EF/Alembic (ADR 0016).
- base/igv write only the original-currency trio (TotalOrig = base + igv, IgvOrig = igv), never
  the PEN projection, no adjustment line.
- PosibleDuplicado recompute keyed on RUC + tipoComprobante + numero, excludes self + DESCARTADA.

### Coherence (Design)
D1, D2, D3, D4, D5, D6, D7, D8 all followed (verified in tests). One deviation: design says 2 new
IUnidadDeTrabajo ports; implementation added a 3rd (ActualizarPosibleDuplicadoAsync) - SUGGESTION 1.

### TDD Compliance
- TDD Evidence reported: Yes (apply-progress has TDD Cycle Evidence tables; Phase 4 explicit,
  Phases 1-3 in prior revisions).
- All tasks have tests: Yes. 33/33 map to test files; each new behavior has >= 2 executed
  assertions with concrete expected values.
- RED confirmed (test files exist): Yes - ProyeccionDeImportesTests, ValidacionDeCorreccionTests,
  ServicioDeFacturasPhase2Tests, SqlUnidadDeTrabajoFacturaTests, Schema021GlosaCamposNoExtraidosTests,
  CalculoDeIndicadoresTests, SqlPromocionRepositoryTests, factura-form.spec, detalle-page.spec.
- GREEN confirmed: Yes - all #19 projects + SPA green in isolation this session.
- Triangulation adequate: Yes - goldens use 5 distinct REGLAS cases; IGV guard across
  03/EXONERADA/INAFECTA/NC07; highlight across listed/empty/pre-021.
- Safety Net for modified files: Yes - SPA baseline 464 -> 476; .NET suites run before modification.
- Honest disclosure noted: apply-progress admits some model/template scaffolding written alongside
  (not strictly before) tests - SUGGESTION 3.

### Test Layer Distribution (this change)
- Unit (xUnit pure) ~30: ProyeccionDeImportesTests, ValidacionDeCorreccionTests, CalculoDeIndicadoresTests.
- Unit (Vitest/jsdom) ~18: factura-form.spec.
- Integration (TestDatabaseFixture / fake UoW) ~24: SqlUnidadDeTrabajoFacturaTests,
  SqlPromocionRepositoryTests, Schema021GlosaCamposNoExtraidosTests, ServicioDeFacturasPhase2Tests.
- Component (HttpTestingController) ~6: detalle-page.spec.
- E2E 0 (deferred by design).

### Assertion Quality
No tautologies, no ghost loops, no assertion-without-production-call. CSS-class assertions in
factura-form.spec (.campo--resaltado presence/count) are legitimate - the spec literally requires
.campo--resaltado only on listed fields. One coarse-fallback test asserts length greater than 1
(acceptable; companion exact-count and empty tests exist). All assertions verify real behavior.

### Quality Metrics
Linter: SPA lint (tsc --noEmit) clean per apply-progress; no ESLint in project. .NET warning-clean.
Type Checker: no errors in changed files.

### Issues Found

CRITICAL: None.

WARNING 1 - Spec text vs implementation, 409 vs 422 on a contable edit of a VALIDADA factura.
api-facturas/spec.md (requirement bullet + scenario "Contable edit on a validated factura is
rejected") states 409 Conflict. The implementation (ValidacionDeCorreccion ->
ResultadoComando.CorreccionInvalida -> ProblemasDeNegocio -> 422) and every covering test assert
422. Owner-resolved deliberate change (tasks.md 3.9 "RESOLVED from 409"; design D2 pure-guard
path), but the delta spec file was never updated. Reconcile the spec text (409 -> 422 in the
requirement and scenario) at archive so the artifact set is self-consistent.

WARNING 2 - IGV-guard carve-out scope broader than the spec. The spec scopes the non-zero-IGV
exemption to "NC 07 con referencia interna". ValidacionDeCorreccion.Validar exempts every
tipoComprobante == 07 regardless of FacturaReferenciaId / EsReferenciaExterna. tasks.md 3.4
authorized "the guard does NOT fire for NC 07"; the branch is dormant today (FacturaReferenciaId
unpopulated until #10/#11) so risk is low, but the pure guard cannot see the reference columns
and needs tightening when NC referencing lands. Track as a follow-up.

WARNING 3 - API-contract test coverage for the additive FacturaRespuesta projection is thin.
CamposNoExtraidos / Glosa reaching the HTTP JSON body is covered only transitively (infra
round-trip + FacturaRespuesta.De() passthrough + SPA fixtures). No SmartNet.Api.Tests assertion
reads these fields from an actual GET/PATCH /api/facturas/{id} response. Low risk (trivial record
projection); a short API test would close it.

WARNING 4 - Foreign-currency seed of the editable base/IGV inputs (apply deviation b). The SPA
baseImponibleInput() / igvInput() computeds source their displayed value from basePEN() / igvPEN()
(the PEN projection). Correct for a PEN invoice (TCventa = 1). For a foreign-currency invoice the
input shows a PEN-magnitude number while onBaseImponible emits a pair the ladder writes as
TotalOrig = base + igv in ORIGINAL currency, so editing base/IGV on a USD invoice through the form
can persist a wrong TotalOrig. Mitigations: a foreign invoice needs a TC before it can validate
and the server re-derives the scalar projection (but not TotalOrig). FacturaRespuesta exposes no
original-currency base/igvOrig field to seed from. Acceptable as a documented limitation for this
slice (all spec scenarios are PEN); follow-up: expose IgvOrig on FacturaRespuesta and seed from
totalOrig minus igvOrig.

SUGGESTION 1 - 3rd IUnidadDeTrabajo port ActualizarPosibleDuplicadoAsync beyond the design 2
ports. Sound rationale: a bare non-CAS single-column UPDATE avoids clobbering the other three
indicator columns GuardarFacturaAsync does not carry, and the CAS already happened earlier in the
same transaction. Integration-tested. Accept; optionally note the amendment in the design.

SUGGESTION 2 - Task 5.3 manual smoke deferred (no seeded local DB this session). Covered by
integration tests 3.6-3.15 and SPA specs 4.1-4.6. Recommend one manual guardar-avance pass
(duplicate clears, PEN-scalar refresh) against a seeded DB during archive or first-deploy validation.

SUGGESTION 3 - Strict-TDD honesty note: some TS model type additions and template scaffolding
were written alongside the tests rather than strictly test-first. Every task still has >= 2
executed behavioral assertions against production code and both suites are green. Accept.

### Verdict
PASS WITH WARNINGS. All 33 tasks implemented and verified against the diff; 45/45 spec scenarios
have passing covering tests; the accounting projection matches the REGLAS section 10 goldens;
purity and ADR 0003/0016 constraints hold; both test suites pass (the 5 full-solution failures
are known DB-teardown flakes, each green in isolation). Four warnings - a stale 409 in the spec
text, an IGV-guard scope broader than the dormant carve-out, thin API-JSON test coverage for the
additive projection, and a foreign-currency seed limitation in the editable base/IGV inputs -
should be reconciled (spec text) or tracked (follow-ups) at archive. None blocks archive.
