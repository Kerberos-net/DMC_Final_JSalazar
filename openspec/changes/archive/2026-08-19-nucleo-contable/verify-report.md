```yaml
schema: gentle-ai.verify-result/v1
evidence_revision: sha256:01db7d84cb10ff4ff5bb73bd8eac4247932104dfdb94a402a41decb832ee331f
verdict: pass
blockers: 0
critical_findings: 0
requirements: 11/11
scenarios: 16/16
test_command: dotnet test SmartNet/contable/SmartNet.Contable.Core.Tests -c Release
test_exit_code: 0
test_output_hash: sha256:01db7d84cb10ff4ff5bb73bd8eac4247932104dfdb94a402a41decb832ee331f
build_command: dotnet build SmartNet/SmartNet.sln -c Release
build_exit_code: 0
build_output_hash: sha256:559f611620367620e44eff38009b3b6da811a0874398b47a116aac9081f80730
```

## Verification Report

**Change**: nucleo-contable (BACKLOG #8)
**Version**: N/A (no spec version field)
**Mode**: Strict TDD
**Note**: Re-verification incorporating Phase 6 (post-verify regression follow-up, task 6.1). This report supersedes the 2026-08-19 pre-Phase-6 report, which recorded 47/47 tasks, 39/39 tests, and PASS WITH WARNINGS with one non-blocking WARNING about the Boleta+Gravada discriminator.

### Completeness
| Metric | Value |
|--------|-------|
| Tasks total | 49 |
| Tasks complete | 49 |
| Tasks incomplete | 0 |

### Build and Tests Execution
**Build**: Passed
```text
dotnet build SmartNet/SmartNet.sln -c Release
Compilacion correcta. 0 Advertencia(s), 0 Errores
```

**Tests**: 41 passed / 0 failed / 0 skipped (was 39/39 before Phase 6; +2 new regression tests)
```text
dotnet test SmartNet/contable/SmartNet.Contable.Core.Tests -c Release
Correctas! - Con error: 0, Superado: 41, Omitido: 0, Total: 41, Duracion: 179 ms
```

**Coverage**: Not available (no coverage tool detected in this dotnet test setup)

### Spec Compliance Matrix
| Requirement | Scenario | Test | Result |
|---|---|---|---|
| Bloque PRINCIPAL - factura gravada | Factura gravada en soles (10.1) | ComponerGoldenTests.cs > Golden_10_1_FacturaGravadaEnSoles_ConDestino | COMPLIANT |
| Bloque PRINCIPAL - factura gravada | Factura con percepcion (10.4) | ComponerGoldenTests.cs > Golden_10_4_FacturaConPercepcion | COMPLIANT |
| Bloque PRINCIPAL - boleta o no gravada | Boleta, IGV al costo (10.2) | ComponerGoldenTests.cs > Golden_10_2_Boleta_IgvAlCosto | COMPLIANT |
| Bloque PRINCIPAL - NC sobre factura gravada | NC sobre factura gravada (10.5) | ComponerGoldenTests.cs > Golden_10_5_NotaDeCreditoSobreFacturaGravada | COMPLIANT |
| Bloque PRINCIPAL - NC sobre boleta o no gravada | NC sobre boleta (10.6) | ComponerGoldenTests.cs > Golden_10_6_NotaDeCreditoSobreBoleta | COMPLIANT |
| Bloque DESTINO automatico | Destino de un flete (10.1) | ComponerGoldenTests.cs > Golden_10_1_FacturaGravadaEnSoles_ConDestino | COMPLIANT |
| Bloque DESTINO automatico | Destino invertido en NC (10.5) | ComponerGoldenTests.cs > Golden_10_5_NotaDeCreditoSobreFacturaGravada | COMPLIANT |
| Conversion de moneda ancla/deriva | Factura en dolares con redondeo derivado (10.3) | ComponerGoldenTests.cs > Golden_10_3_FacturaDolares_RedondeoDerivado | COMPLIANT |
| NC hereda el tipo de cambio de su factura | NC del 100% en dolares deja el pasivo en cero (10.7) | ComponerGoldenTests.cs > Golden_10_7_NotaDeCredito100PorCientoDolares_HeredaTipoDeCambio | COMPLIANT |
| Invariantes globales de confirmacion | Asiento valido pasa a CONFIRMADO | InvariantesDeConfirmacionTests.cs > Global1..5_x_Acepta (5 tests) | COMPLIANT |
| Invariantes globales de confirmacion | Descuadre o linea sin cuenta rechaza | InvariantesDeConfirmacionTests.cs > Global1_Descuadre_Rechaza, Global2_LineaSinCuenta_Rechaza (plus Global3/4/5 reject tests) | COMPLIANT |
| Invariante del bloque PRINCIPAL por tipo de comprobante | Factura gravada consistente | InvariantesDeConfirmacionTests.cs > Principal_FacturaGravadaConsistente_Acepta | COMPLIANT |
| Invariante del bloque PRINCIPAL por tipo de comprobante | 401111 indebido en NC sobre boleta rechaza | InvariantesDeConfirmacionTests.cs > Principal_401111IndebidoEnNotaCreditoSobreBoleta_Rechaza | COMPLIANT |
| Invariante del bloque DESTINO sobre datos congelados | Par reflejo/puente presente | InvariantesDeConfirmacionTests.cs > Destino_ParReflejoPuentePresente_Acepta | COMPLIANT |
| Invariante del bloque DESTINO sobre datos congelados | Falta el par aunque el catalogo ya no declare ctarefleja | InvariantesDeConfirmacionTests.cs > Destino_FaltaElPar_Rechaza | COMPLIANT |
| Consumo read-only de catalogos y tipos de cambio | El motor no re-resuelve el tipo de cambio | Structural evidence: ComposicionDeAsiento and ConversionDeMoneda take only TipoCambioCongelado.Venta (a decimal); PurityScanTests confirms zero infra I/O at assembly level | PARTIAL - structural evidence only |

**Compliance summary**: 15/16 scenarios fully COMPLIANT with a named covering test; 1/16 (the no-re-resolves-TC scenario) is compliant by structural/architectural evidence, unchanged from the prior report; Phase 6 did not touch this area.

### Correctness (Static Evidence)
| Requirement | Status | Notes |
|---|---|---|
| Motor puro sin BD/HTTP/reloj (ADR 0019, spec Purpose) | Implemented | PurityScanTests.cs unchanged; still passes against the Phase-6-updated assembly |
| Sin catalogo de rechazo par.8, sin sugerencia de cuenta (#9), sin precondicion vieja de NC | Implemented | Unchanged from prior report |
| Tope acumulado de NC fuera de alcance (Non-Goals) | Implemented (absence confirmed) | Unchanged |
| Componer never throws/rejects for domain reasons (ADR 0006 BORRADOR) | Implemented | Unchanged; Phase 6 adds a case exercising the same non-throwing path with a value assertion |
| ResultadoConfirmacion closed hierarchy, no exceptions for domain rejection (Decision 3) | Implemented | Unchanged |
| Par.7 evaluates ALL failed invariants, not first (spec, design.md) | Implemented | Unchanged |

### Coherence (Design)
| Decision | Followed? | Notes |
|---|---|---|
| Decision 1-5 | Yes | Unchanged from prior report; Phase 6 touched only test files |
| Interfaces/Contracts snippet (design.md) | Yes, with 2 documented extensions | Unchanged (Deviations #1, #2) |
| Testing Strategy (design.md) - Unit only, no Integration/E2E | Yes | 41/41 tests, all unit; Phase 6 added 2 more unit tests, no new layer introduced |

### Phase 6 Verification (incremental focus of this report)

| Check | Result | Details |
|---|---|---|
| Task 6.1 marked complete in tasks.md | Yes | task 6.1 present as [x], with full rationale inline |
| Componer_BoletaMarcadaGravada_PineaGeneracionActualDeLinea401111 exists | Yes | SmartNet/contable/SmartNet.Contable.Core.Tests/ComponerGoldenTests.cs line 311 |
| Principal_BoletaMarcadaGravadaConsistente_PineaAceptacionActual exists | Yes | SmartNet/contable/SmartNet.Contable.Core.Tests/InvariantesDeConfirmacionTests.cs line 194 |
| Both new tests actually exercise Boleta+Gravada | Yes | First builds an EntradaAsiento with Comprobante Boleta and Afectacion Gravada, and asserts 401111 D 18.00 and 421211 H 118.00 are generated (calls ComposicionDeAsiento.Componer, real production code, concrete numeric assertions, not tautological). Second builds a hand-crafted AsientoContable with the same combination and a matching 401111 line, and asserts InvariantesDeConfirmacion.Evaluar returns Confirmable (calls real production code, type-based assertion on a closed-hierarchy discriminated result, not a tautology) |
| Both new tests pass at runtime | Yes | Full-suite dotnet test re-run independently in this verify session: 41/41 passed, was 39/39 before Phase 6 |
| Production code (ComposicionDeAsiento.cs, InvariantesDeConfirmacion.cs) untouched | Yes | Source-inspected both files: the discriminator remains esGravada equals afectacionEfectiva equals Afectacion.Gravada (Componer, line 59) and esGravada equals asiento.AfectacionCongelada equals Afectacion.Gravada (InvariantesDeConfirmacion, line 101); neither checks TipoComprobante. No git history exists for these files, since the contable tree is untracked in this worktree, so this was confirmed by direct source inspection against the apply-progress artifact claim that no production code was touched, cross-checked line-by-line, not by diffing against a prior commit |
| WARNING from the prior report addressed as PINNED, not fixed | Yes | Apply-progress explicitly states PINNED not fixed, matching the intent of prior SUGGESTION 1, which asked only for detectability via regression test, not for the upstream guard itself. The upstream guard (rejecting Boleta+Gravada as illegal at #3/#11) remains explicitly out of scope for #8, consistent with both the prior verify-report recommendation and the tasks.md 6.1 scope note |
| Assertion quality of the 2 new tests | Pass | No tautologies, no assertion-free loops, no smoke-test-only pattern; both assert concrete decimal values or a concrete discriminated-union type against real production-code output |

### Issues Found

**CRITICAL**: None

**WARNING**: None new. The original WARNING (Boleta+Gravada discriminator trusts an unenforced upstream invariant from #3/#11) is downgraded to informational: it is now pinned by an explicit, passing regression test on both call sites (Componer and EvaluarPrincipal), so a future silent drift in the discriminator or a change to the upstream guarantee will be caught by CI rather than only by manual review. The underlying accounting-correctness risk itself is not eliminated by Phase 6; that requires the upstream guard in #3/#11, which remains explicitly out of scope for #8 (tracked as a follow-up against #3/#11, not #8).

**SUGGESTION**:
1. When #3/#11 is implemented, the two Phase-6 pinning tests (Componer_BoletaMarcadaGravada_PineaGeneracionActualDeLinea401111, Principal_BoletaMarcadaGravadaConsistente_PineaAceptacionActual) should be revisited: once the upstream guard makes Boleta+Gravada structurally unreachable, these tests may need to move to asserting rejection instead of pinning the current silent-acceptance behavior, or be retired if the illegal state becomes unconstructible.
2. InvarianteIncumplida.Detalle (design deviation #4, non-nullable string) is a debuggability addition beyond design.md; no action needed, noted for traceability only, unchanged from prior report.

### Verdict
**PASS WITH WARNINGS**
49/49 tasks complete (47 original plus 2 Phase 6 follow-up), build 0 errors and 0 warnings, 41/41 tests pass (independently re-run, was 39/39 pre-Phase-6), all 7 REGLAS.md par.10 goldens and all 7 par.7 invariants (both accept and reject paths) remain covered, and the Phase 6 regression tests independently confirmed to exist, exercise the exact Boleta+Gravada combination named in the prior WARNING, call real production code with concrete assertions, and pass. Production business logic (ComposicionDeAsiento.cs, InvariantesDeConfirmacion.cs) confirmed unchanged by source inspection. The prior WARNING is downgraded from an untested assumption to a pinned assumption: still non-blocking, still requiring the actual upstream guard as a follow-up against #3/#11, which remains correctly out of scope for #8.
