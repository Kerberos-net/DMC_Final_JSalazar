```yaml
schema: gentle-ai.verify-result/v1
evidence_revision: sha256:029336e58b43a3bd470aec773826f1b88c8a8489de5c842508cdfe3bc69bf9e8
verdict: pass
blockers: 0
critical_findings: 0
requirements: 13/13
scenarios: 36/36
test_command: dotnet test SmartNet.Facturacion.Core.Tests SmartNet.Facturacion.Infrastructure.Tests SmartNet.TiposCambio.Core.Tests SmartNet.TiposCambio.Infrastructure.Tests SmartNet.Api.Tests SmartNet.Contable.Core.Tests -c Release --no-build
test_exit_code: 0
test_output_hash: sha256:79ffa10ffc2a87a2f111149bcd3b17d11e02388eb8c336c5a55a22dda27d24c5
build_command: dotnet build SmartNet.sln -c Release
build_exit_code: 0
build_output_hash: sha256:32a4c361a4d5362d8d8c4a32a6eea6407aee399142b408de19f6663039a5e096
```

## Verification Report

**Change**: api-facturas-asientos (BACKLOG #11)
**Version**: N/A (delta specs, no semver)
**Mode**: Strict TDD, re-verification post-PR5

### Context

This is a re-verification of the full 5-PR chain (PR1-PR5), superseding the previous
verify-report.md (PASS WITH WARNINGS, one CRITICAL finding: the SinTipoCambio 409 gate for
abrir/validar was defined but never evaluated, HechosDeConflicto.SinTipoCambio hardcoded to
false). PR5 (tasks.md Phase 5, 4/4 tasks) closed that gap. All previous PASS/WARNING findings from
the PR1-4 pass are carried forward unchanged (re-confirmed against current code); only the CRITICAL
item and its test evidence are re-verified in full below.

### Completeness

| Metric | Value |
|--------|-------|
| Tasks total | 29 |
| Tasks complete | 29 |
| Tasks incomplete | 0 |

All 29/29 tasks.md checkboxes checked across Phases 1-5 (PR1-PR5). Matches apply-progress.md
per-phase breakdown exactly (11/11, 6/6, 3/3, 5/5, 4/4).

### Build & Tests Execution

Build: PASSED
```text
dotnet build SmartNet.sln -c Release
Compilacion correcta.
0 Advertencia(s)
0 Errores
```

Tests: PASSED - 253/253, 0 failed, 0 skipped (independently re-run this session, real SQL Server
instance backing all Infrastructure/Api suites)
```text
dotnet test SmartNet.Facturacion.Core.Tests            -> 66/66  (Correctas)
dotnet test SmartNet.Facturacion.Infrastructure.Tests  -> 31/31  (Correctas, real DB)
dotnet test SmartNet.TiposCambio.Core.Tests             -> 20/20  (Correctas)
dotnet test SmartNet.TiposCambio.Infrastructure.Tests   -> 12/12  (Correctas, real DB)
dotnet test SmartNet.Api.Tests                          -> 83/83  (Correctas, real DB + cookie auth)
dotnet test SmartNet.Contable.Core.Tests (REGLAS.md)    -> 41/41  (Correctas, unaffected sanity check)
Total: 253/253
```

This exactly matches the apply agent's claimed counts (66/31/20/12/83/41 = 253) with zero
regressions against the PR1-4 baseline (202/202 + 41/41 = 243; +10 new tests added by PR5: +4
Core, +3 Infrastructure, +3 Api).

Coverage: Not available - no coverage tool configured for this repo. Not a failure, per Strict
TDD verify rules (informational only).

### CRITICAL Finding Re-Verification: SinTipoCambio 409 gate

Read apply-progress.md's PR5 section plus the actual code at:

- SmartNet/facturacion/SmartNet.Facturacion.Core/ServicioDeFacturas.cs (AbrirAsync,
  lines 242-273): after the existing idempotency check (asiento already exists -> no-op success),
  gates on factura.Moneda != MonedaLocal ("PEN") -> calls
  uow.ExisteTipoCambioVigenteAsync(factura.FechaEmision, ct) -> if false, returns
  ResultadoComando.Conflicto(CasoConflicto.SinTipoCambio, ...) before
  CrearAsientoBorradorAsync is invoked. Confirmed by direct source read, matches claim exactly.
- SmartNet/facturacion/SmartNet.Facturacion.Core/IUnidadDeTrabajo.cs line 103: new port member
  Task<bool> ExisteTipoCambioVigenteAsync(DateOnly fecha, CancellationToken ct) - pure signature,
  DateOnly/CancellationToken/Task<bool> only, no System.Data/Microsoft.Data.SqlClient
  import anywhere in SmartNet.Facturacion.Core/*.cs (confirmed by grep, zero matches). Port stays
  a pure interface - Core still asks a yes/no question through an abstraction, same shape as every
  other IUnidadDeTrabajo member. ADR 0019 intact.
- SmartNet/facturacion/SmartNet.Facturacion.Infrastructure/SqlUnidadDeTrabajo.cs
  (CargarAsientoAsync, line 101): var sinTipoCambio = moneda != MonedaLocal && !await
  ExisteTipoCambioVigenteAsync(fechaEmision, ct); feeds HechosDeConflicto.SinTipoCambio for the
  validar path - no longer a hardcoded false. ExisteTipoCambioVigenteAsync (line 237) is a
  thin delegation to the pre-existing ITipoCambioRepository.ObtenerVigenteAsync; no new SQL
  written. Confirmed by direct source read.
- SmartNet/api/SmartNet.Api/ProblemasDeNegocio.cs line 124: CasoConflicto.SinTipoCambio =>
  (Base + "sin-tipo-cambio", "Sin tipo de cambio vigente") - 409 problem+json mapping present and
  wired, confirmed by grep.

Test coverage confirmed present and passing (not just claimed):

| Layer | Test | Assertion | Status |
|---|---|---|---|
| Core | ServicioDeFacturasPhase2Tests.AbrirAsync_ForeignCurrencyWithNoTipoCambio_ReturnsConflicto_AndNeverCreatesAnAsiento | conflicto.Caso == CasoConflicto.SinTipoCambio | PASSED (in 66/66 run) |
| Core | ServicioDeFacturasPhase2Tests.AbrirAsync_ForeignCurrencyWithNoTipoCambio_ButAsientoAlreadyExists_StaysIdempotent | idempotency check has priority over the new gate | PASSED |
| Infrastructure | SqlUnidadDeTrabajoTests.CargarAsientoAsync_ForeignCurrencyWithNoTipoCambioRow_ReportsSinTipoCambio | resultado.Hechos.SinTipoCambio == true (real DB) | PASSED (in 31/31 run) |
| Infrastructure | (companion) foreign currency WITH tipo de cambio row -> SinTipoCambio == false | PASSED |
| Infrastructure | (companion) PEN factura, no tipo de cambio row needed -> SinTipoCambio == false | PASSED |
| Api | FacturaEndpointsTests.Abrir_ForeignCurrencyWithNoTipoCambio_Returns409_AndCreatesNoAsiento | HTTP 409, zero AsientoContable rows created (real DB) | PASSED (in 83/83 run) |

Verdict on this finding: RESOLVED. Not a documentation claim - independently confirmed against
current source and a fresh, this-session real-database test run. Both call sites identified in the
PR1-4 pass (AbrirAsync pre-asiento gate, CargarAsientoAsync post-asiento HechosDeConflicto)
are wired; neither path retains the previous permanently-false branch.

### Spec Compliance Matrix

| Requirement | Scenario | Test | Result |
|---|---|---|---|
| api-facturas: PATCH concurrency | Matching If-Match succeeds | FacturaEndpointsTests | COMPLIANT |
| api-facturas: PATCH concurrency | Stale If-Match -> 412 | FacturaEndpointsTests | COMPLIANT |
| api-facturas: PATCH concurrency | Correction to validated factura writes AuditoriaCorreccion | FacturaEndpointsTests | COMPLIANT |
| api-facturas: abrir creates draft | Opening factura with no asiento | FacturaEndpointsTests | COMPLIANT |
| api-facturas: abrir creates draft | Opening foreign-currency factura, no tipo de cambio -> 409 | Abrir_ForeignCurrencyWithNoTipoCambio_Returns409_AndCreatesNoAsiento | COMPLIANT (was CRITICAL/UNTESTED pre-PR5, now RESOLVED) |
| api-facturas: validar confirms | Successful validar assigns correlativo | FacturaEndpointsTests | COMPLIANT |
| api-facturas: validar confirms | SumaDebeIgualHaber violated -> 422 | FacturaEndpointsTests | COMPLIANT |
| api-facturas: validar confirms | Business-state 409 cases (incl. tipo-de-cambio) | FacturaEndpointsTests | COMPLIANT (tipo-de-cambio sub-case now fires, PR5) |
| api-facturas: validar confirms | Rollback does not reuse correlativo | SqlUnidadDeTrabajoTests (PR1) | COMPLIANT |
| api-facturas: descartar | Discarding duplicate factura, no audit | FacturaEndpointsTests | COMPLIANT |
| api-facturas: adjuntos | POST adjunto to validated factura -> event, no audit | FacturaEndpointsTests | COMPLIANT |
| api-facturas: adjuntos | DELETE adjunto from validated factura -> event + audit | FacturaEndpointsTests | COMPLIANT |
| api-facturas: adjuntos | Adjunto changes on draft factura, no event/audit | FacturaEndpointsTests | COMPLIANT |
| api-asientos: PATCH concurrency | Matching If-Match succeeds | AsientoEndpointsTests | COMPLIANT |
| api-asientos: PATCH concurrency | Stale If-Match -> 412 | AsientoEndpointsTests | COMPLIANT |
| api-asientos: PATCH concurrency | Editing CONFIRMADO without reabrir -> 409 | AsientoEndpointsTests | COMPLIANT |
| api-asientos: lineas by LineaId | POST /lineas assigns stable LineaId | AsientoEndpointsTests | COMPLIANT |
| api-asientos: lineas by LineaId | PATCH /lineas/{lineaId} survives earlier deletion | AsientoEndpointsTests | COMPLIANT |
| api-asientos: lineas by LineaId | Manual redistribution -> REPARTO_MANUAL audit | AsientoEndpointsTests | COMPLIANT |
| api-asientos: reabrir | reabrir with motivo -> editable, REAPERTURA audit | AsientoEndpointsTests | COMPLIANT |
| api-asientos: reabrir | reabrir without motivo -> 400, no state change | AsientoEndpointsTests | COMPLIANT |
| api-asientos: reabrir | reabrir a BORRADOR asiento -> 409 | AsientoEndpointsTests | COMPLIANT |
| api-asientos: anular | anular CONFIRMADO -> ANULADO, audit, factura freed | AsientoEndpointsTests | COMPLIANT |
| api-asientos: anular | anular already-ANULADO -> 409 | AsientoEndpointsTests | COMPLIANT |
| api-asientos: InvarianteIncumplida | LineaSinCuenta -> 422, distinct type | AsientoEndpointsTests | COMPLIANT |
| api-asientos: InvarianteIncumplida | Destino incompleto -> 422, own type | AsientoEndpointsTests | COMPLIANT |
| api-incidencias-integraciones: enqueue-only | reprocesar -> CommandQueue row, no RPC, no audit | IntegracionEndpointsTests | COMPLIANT |
| api-incidencias-integraciones: enqueue-only | sincronizar/reconectar -> CommandQueue row, no audit | IntegracionEndpointsTests | COMPLIANT |
| api-incidencias-integraciones: enqueue-only | unknown integration name -> 409/400, no row | IntegracionEndpointsTests | COMPLIANT |
| api-incidencias-integraciones: estado | recent success -> Conectado | IntegracionEndpointsTests | COMPLIANT |
| api-incidencias-integraciones: estado | consecutive failures -> Con error | IntegracionEndpointsTests | COMPLIANT |
| tipos-de-cambio: POST /api/tipos-cambio | MANUAL insert for uncovered date -> 201/200 | TipoCambioEndpointsTests | COMPLIANT |
| tipos-de-cambio: POST /api/tipos-cambio | duplicate MANUAL -> 409, no overwrite | TipoCambioEndpointsTests | COMPLIANT |
| tipos-de-cambio: POST /api/tipos-cambio | malformed body -> 400, no insert | TipoCambioEndpointsTests | COMPLIANT |
| tipos-de-cambio: POST /api/tipos-cambio | SBS row exists -> MANUAL load still independent | TipoCambioEndpointsTests | COMPLIANT |

Compliance summary: 36/36 scenarios compliant (13/13 requirements). Zero UNTESTED, zero FAILING.

### Correctness (Static Evidence)

| Requirement | Status | Notes |
|------------|--------|-------|
| SinTipoCambio 409 gate (D4) | Implemented | Both AbrirAsync pre-asiento gate and CargarAsientoAsync HechosDeConflicto computation confirmed by source read, not just report claim |
| ADR 0019 Core purity | Implemented | Zero SQL/HTTP imports in SmartNet.Facturacion.Core/*.cs; new port member is a pure Task<bool> signature |
| ADR 0003 partition | Implemented | Zero Python files touched by PR5 (confirmed git status --porcelain \| grep .py -> none); .NET never calls Python directly |
| ADR 0016 versioned SQL | Implemented | PR5 added zero new schema files (wiring-only change over existing ITipoCambioRepository) |
| Spanish domain naming | Implemented | ExisteTipoCambioVigenteAsync, HechosDeConflicto, CasoConflicto.SinTipoCambio follow convention; technical scaffolding stays English |

### Coherence (Design)

| Decision | Followed? | Notes |
|----------|-----------|-------|
| D4 409-gate (all 9 CasoConflicto values wired) | Yes | Previously 8/9 (PR1-4 gap); now 9/9 with SinTipoCambio closed by PR5 |
| D2 concurrency split | Yes | Unchanged from PR1-4 pass |
| D5 transaction shape (UPDLOCK, gapless correlativo) | Yes | Unchanged |
| D6 AuditoriaCorreccion | Yes | Unchanged |
| D7 enqueue-only | Yes | Unchanged |
| D8 endpoints/DI | Yes | Unchanged |

### TDD Compliance

| Check | Result | Details |
|-------|--------|---------|
| TDD Evidence reported | Yes | apply-progress.md PR5 section documents RED-before-GREEN for the Core gate test explicitly |
| All tasks have tests | Yes | 4/4 Phase 5 tasks map to tests (5.1/5.2 RED, 5.3 GREEN wiring, 5.4 docs update) |
| RED confirmed (tests exist) | Yes | Test files verified present: ServicioDeFacturasPhase2Tests.cs, SqlUnidadDeTrabajoTests.cs, FacturaEndpointsTests.cs |
| GREEN confirmed (tests pass) | Yes | All re-run this session: 66/66, 31/31, 83/83 |
| Triangulation adequate | Yes | 3 layers (Core, Infrastructure, Api) each assert the gate from a different angle (unit fake, real-DB read, HTTP end-to-end) |
| Safety Net for modified files | Yes | Full 253-test suite re-run confirms zero regressions on all modified files (ServicioDeFacturas.cs, SqlUnidadDeTrabajo.cs, SqlFacturacionStore.cs, Program.cs) |

TDD Compliance: 6/6 checks passed

### Assertion Quality

No trivial/tautological assertions found in the PR5 test additions reviewed (ServicioDeFacturasPhase2Tests.AbrirAsync_ForeignCurrencyWithNoTipoCambio_*, SqlUnidadDeTrabajoTests's 3 SinTipoCambio cases, FacturaEndpointsTests.Abrir_ForeignCurrencyWithNoTipoCambio_Returns409_AndCreatesNoAsiento). Each asserts a distinct production-code-exercised outcome (enum case, boolean flag from a real DB read, HTTP status + zero-row side effect), not a smoke test or type-only check.

Assertion quality: All assertions verify real behavior

### Issues Found

CRITICAL: None. The sole CRITICAL finding from the prior verify pass (SinTipoCambio 409 gate
unimplemented) is confirmed RESOLVED by source inspection and passing real-DB tests.

WARNING: 19 non-blocking deviations, 18 carried forward unchanged from the PR1-4 verify pass
(see apply-progress.md deviations list) plus 1 new from PR5:
- Deviation 19 (PR5, new): SqlUnidadDeTrabajo.ExisteTipoCambioVigenteAsync opens its own
  SqlConnection via ITipoCambioRepository rather than reusing the ambient unit-of-work
  transaction, because fact.TipoCambio is read-only in this flow (no atomicity requirement ties
  the read to the surrounding factura/asiento transaction). Consistent with existing
  TipoCambioEndpoints.cs (PR4) precedent. Non-blocking.
- Deviations 1, 2, 6, 9, 10, 11, 12, 15, 16, 18: implementation choices where design/spec
  under-specified a detail; internally consistent, tested, do not contradict any MUST scenario.
- Deviations 3, 5, 7, 13, 14, 17: documented scope gaps (adjuntos byte storage, header/lineas
  split, motivo enforcement layer, REST idiom, CI wiring - already remediated in PR4).

SUGGESTION: None new this pass.

### Ready for sdd-archive?

Yes. The sole blocker from the prior verify pass is resolved and independently re-confirmed:
build clean (0/0), 253/253 tests green across 6 real-database-backed suites, zero regressions
against the PR1-4 baseline, all 29/29 tasks.md checkboxes complete, all 13 requirements / 36
scenarios across the 4 specs (api-asientos, api-facturas, api-incidencias-integraciones,
tipos-de-cambio) COMPLIANT with a passing covering test, and ADR 0019/0003/0016 plus Spanish
naming convention independently confirmed unbroken by PR5. The 19 WARNING-level deviations are
non-blocking implementation-choice notes for product-owner awareness, not spec violations - they
should be captured as a single follow-up note per design.md's own Open Questions section rather
than re-litigated here.

### Verdict
PASS WITH WARNINGS
Zero CRITICAL findings; 19 non-blocking WARNING-level deviations already self-flagged by the apply
agent and confirmed non-blocking by this pass. Ready for sdd-archive.
