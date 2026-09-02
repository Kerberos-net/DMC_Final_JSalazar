```yaml
schema: gentle-ai.verify-result/v1
evidence_revision: sha256:162d7f5d231dddc5767ed1b15ebc3d5bc2f8620c08133ef5e5965296ca7dabd0
verdict: pass_with_warnings
blockers: 0
critical_findings: 0
requirements: 12/12
scenarios: 34/34
test_command: dotnet test per-suite --no-build sequential plus npm test SmartNetWeb
test_exit_code: 0
test_output_hash: sha256:162d7f5d231dddc5767ed1b15ebc3d5bc2f8620c08133ef5e5965296ca7dabd0
build_command: dotnet build SmartNet.sln
build_exit_code: 0
build_output_hash: sha256:e21a744dc2b2cc1ec674c2ee56f054f97884fc7e76aa4325e9170c3632db3c30
```

## Verification Report

**Change**: cablear-composicion-asiento (BACKLOG #24)
**Mode**: Strict TDD. **Spec**: 5 delta capabilities, 12 requirements, 34 scenarios.

### Completeness
Tasks: 30/30 complete across 6 phases; tasks.md matches working-tree code state.

### Build and Tests (authoritative sequential runs, local SQL Server)
- dotnet build SmartNet.sln -> 0 warnings, 0 errors.
- SmartNet.Facturacion.Core.Tests 186/186
- SmartNet.Contable.Core.Tests 51/51
- SmartNet.Facturacion.Infrastructure.Tests 72/72
- SmartNet.Inbox.Infrastructure.Tests 75/75
- SmartNet.Api.Tests 212/212
- SPA npm test (Vitest) 52 files / 491/491
- SPA npm run lint (tsc --noEmit) clean

All counts match the apply-phase claims exactly. A single full-solution parallel dotnet test
produced spurious SqlConnection.OnError failures across unrelated suites (disposable-DB
connection exhaustion from concurrent fixture creation), NOT a code defect; every suite is
green when run in isolation. Coverage tool: not configured.

### Spec Compliance (32/34 COMPLIANT, 2 DEFERRED)
nucleo-contable (7): valido->CONFIRMADO / descuadre rechaza [unchanged] COMPLIANT;
asiento sin lineas ya no pasa vacuamente -> InvariantesDeConfirmacionTests.DeVacuuming_AsientoSinLineas_*
and _AsientoConSoloElCreditoDelProveedor_* COMPLIANT; Sembrar 10.1/10.2/10.3 goldens ->
SembradoDeAsientoTests.Sembrar_10_1/10_2/10_3 (pure Core, exact numbers) COMPLIANT;
10.4 percepcion DEFERRED (owner decision 2, no PercepcionOrig column).

api-facturas (13): abrir seeds engine-composed asiento [modified] -> FacturaEndpointsTests.Reglas_10_1
+ Abrir_* COMPLIANT; no-suggestion placeholder -> SembradoDeAsientoTests.Sembrar_SinCuentaSugerida_*
COMPLIANT; existing BORRADOR no-op -> ServicioDeFacturasPhase2Tests idempotency COMPLIANT;
foreign no-TC 409 [unchanged] COMPLIANT; recomponer replaces lineas / rejected on CONFIRMADO ->
AsientoEndpointsTests recomponer (5) + ServicioDeAsientosTests.RecomponerAsync_* COMPLIANT;
fresh seed validates for real + 10.1/10.2/10.3 abrir->validar -> FacturaEndpointsTests.Reglas_10_*
-> CONFIRMADO + correlativo COMPLIANT; 10.4 not covered DEFERRED; manual split that balances
validates -> ServicioDeFacturasPhase2Tests COMPLIANT; vacuous-pass regression -> DeVacuuming_*
COMPLIANT; base/IGV edit unbalances -> 422 descuadre and recomponer clears it ->
FacturaEndpointsTests.PatchBaseIgv_UnbalancingASeededSplit_* (E2E) + ServicioDeFacturasPhase2Tests 3.7
COMPLIANT.

factura-promotion (4): PEN factura promoted with seeded asiento ->
PromocionBackgroundServiceTests.ProcesarPendientesAsync_AfterPromotingAFactura_SeedsItsAsientoExactlyOnce
COMPLIANT; associated-PDF merge branch seeds no asiento ->
_XmlThenAssociatedPdfMerge_SeedsOnlyForTheXmlPromotion + _PdfFirstDefer_NeverSeeds COMPLIANT;
idempotent re-promotion does not re-seed -> AbrirAsync idempotency COMPLIANT;
foreign-currency no-rate promotes without asiento -> PARTIAL (promotion side covered by a
FakeSembradorDeAsiento that cannot throw; the real SembradorDeAsientoAdapter swallow seam is untested).

sugerencia-cuenta (3): ServicioDeSugerencia resolves during seeding ->
SqlUnidadDeTrabajoSembradoTests resolver + Reglas_10_* real cascade COMPLIANT; suggested account on
default cargo linea -> Reglas_10_1_* (631111) COMPLIANT; no suggestion -> placeholder -> validar
blocked -> SembradoDeAsientoTests + ServicioDeFacturasPhase2Tests COMPLIANT.

pantalla-detalle-validacion (7): base/IGV from seeded asiento + no-asiento generar-asiento ->
detalle-page.spec.ts COMPLIANT; recomponer regenerates / hidden on CONFIRMADO ->
detalle-page.spec.ts (3) + asiento.service.spec.ts (2) COMPLIANT; descuadre marker appears / clears
/ #12+#19 regression -> detalle-page.spec.ts (3 + factura-form regression) PARTIAL (marker bound to
existing cuadre() all-lineas balance, not literal sum(PRINCIPAL cargos) vs header BasePEN/NetoPEN).

### Design Coherence
- Componer / InvariantesDeConfirmacion / ProyeccionDeImportes.Derivar / PatchAsync D4 /
  CargarAsientoAsync: byte-for-byte UNCHANGED. Verified: first 3 absent from git status;
  ServicioDeFacturas.cs diff is only the 7-line AbrirAsync seed block; SqlUnidadDeTrabajo diff adds
  a ctor arg + new methods, CargarAsientoAsync body untouched.
- Hybrid Option 3, single new IUnidadDeTrabajo member (ResolverHechosDeComposicionAsync, no new ctor),
  placeholder line for no-suggestion (no sentinel account), ISembradorDeAsiento port in Inbox.Core +
  adapter in SmartNet.Api, seed writes no audit / recomponer writes one REPARTO_MANUAL, single PR +
  size:exception: all followed.
- Zero-amount line guard in SembradoDeAsiento.Sembrar (drops Debe==0 && Haber==0, renumbers Orden);
  Componer unchanged; tested Sembrar_GravadaConIgvCero_* (2).
- cuentaCodigo override (design C1): IUnidadDeTrabajo.ObtenerCuentaContableAsync + RecomponerAsync
  resolve / CorreccionInvalida 422 when unknown; tested in SqlUnidadDeTrabajoSembradoTests and
  ServicioDeAsientosTests. Production code present and green in the working tree.

### TDD Compliance
Per-batch TDD Cycle Evidence tables present in apply-progress.md. RED via interface-break compile
fail / real-DB 500 repro; GREEN reproduced on independent re-run. Triangulation adequate
(10.1/10.2/10.3 distinct goldens; recomponer borrador/confirmado/stale-version). Assertion quality
good: exact numeric REGLAS 10 assertions and exact section-7 message strings; no tautologies, ghost
loops, or smoke-only tests found.

### Scope Guardrails (all held)
No .sql / no checksums.txt; no REGLAS.md or BACKLOG.md edit; Herencia = null (no NC wiring);
PercepcionOrig = 0m (no column); no sugerencia HTTP endpoint or SPA suggestion UI; no new
AuditoriaCorreccion.Accion value. REGLAS section-12 note added to DEUDA-TECNICA.md row 5.2 -
present and accurate (point 1 already executes via #19; point 5 unreachable this cycle) per obs 309.

### Issues
CRITICAL: None.

WARNING:
1. SembradorDeAsientoAdapter (SmartNet.Api) swallow logic (SinTipoCambio / NoEncontrado / unexpected
   -> log + return, never throw) has no direct automated test. Low risk (~15-line glue), owner
   decision 6 confirmed the behavior, but the foreign-currency no-rate promotion scenario is only
   partially covered.
2. Section 10.4 (percepcion): 2 spec scenarios explicitly deferred (owner decision 2, no
   PercepcionOrig column). Documented non-goal.
3. Descuadre marker binds to cuadre() (all-lineas Debe vs Haber), not literally
   sum(PRINCIPAL cargos) vs header base. A balanced-but-misallocated manual edit could hide the
   marker while server-side section-7 still returns 422. UX hint only; server gate proven by E2E.
4. 4.5 E2E goldens rely on the sugerencia cascade Tier-3 first-candidate path (one seeded hoja per
   prefix). Multi-candidate / usage-history branches are not exercised here (sugerencia module owns
   that). Asiento composition itself is faithfully exercised end to end.

SUGGESTION: MapearAfectacion is duplicated a third time (SembradoDeAsiento, ServicioDeFacturas,
SqlUnidadDeTrabajo) - candidate to hoist in a follow-up.

### Verdict
PASS WITH WARNINGS. All 12 requirements implemented; 32/34 scenarios covered by passing tests, 2
deferred by owner decision. Build 0/0; all suites green in isolation. The accounting-core
byte-for-byte-unchanged mandate is verified. No CRITICAL issues; 4 non-blocking WARNINGs. Ready for
sdd-archive.
