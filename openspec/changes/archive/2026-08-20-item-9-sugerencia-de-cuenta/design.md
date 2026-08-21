# Design: Sugerencia de cuenta (BACKLOG #9)

**Corrección post-validación de contrato:** El diseño original listaba ADR 0011 como "Modify" (revisión 5) para fijar el denominador de `Fundamento`, contradiciendo `proposal.md` que cerró el ADR a ediciones. Corregido: la definición del denominador queda como aclaración de diseño únicamente, documentada sin modificar ADR 0011.

## Decisions

### D1: Assembly Structure
New assembly `SmartNet.Sugerencia.Core` (pure domain) with `SmartNet.Sugerencia.Core.Tests`.
No Infrastructure project — ranking logic is pure, orchestration depends only on ports
(ISugerenciaCuentaRepository, ICuentaContableRepository, IMotivoRepository, IMotivoAtributoRepository).

### D2: Orchestrator Location and Dependencies
`ServicioDeSugerencia` lives in Sugerencia.Core, not in Infrastructure. Constructor takes exactly
4 interfaces (ports only), zero SqlClient/AspNetCore/Http references.

### D3: Fundamento/VecesDelAmbito Denominator
Fundamento denominator (total uses in scope) = sum of Veces for filtered rows that survived
vigencia filtering and Veces > 0 from the winning tier, divided by sum of all Veces for offerable
motivos (when suggesting motivo). Clarification only, no ADR change.

### D4: Determinism Enforcement
Tier 3 (fallback to first candidate by CuentaCodigo ASC) uses `vigentes.Min(StringComparer.Ordinal)`,
not relying on caller order. Internal re-derivation via HashSet ensures determinism.

### D5: RegistrarUsoAsync Never Called
Orchestration never calls RegistrarUsoAsync (item #11's responsibility). Verified by spy tests (0 calls).

## Requirements Traceability

| Requirement | Design Section | Implementation |
|---|---|---|
| 3-tier cascade (strict order) | D4 | CascadaDeSugerencia.SugerirCuenta |
| Tie-break Veces > UltimoUso > CuentaCodigo | D1 | Comparador en ElegirGanador |
| Filter against vigencia (ResolverCandidatas) | D2 | Pre-filter rows in tiers 1-2 |
| New provider falls to tier 3 | D1 | Null check and fallback logic |
| Motivo suggestion (2-tier, provider-keyed) | D1 | CascadaDeSugerencia.SugerirMotivo |
| Auditable rationale (Fundamento + VecesDelAmbito) | D3 | Result record with Fundamento data |
| Combined orchestration (cuenta+motivo+fundamento) | D2 | ServicioDeSugerencia.SugerirParaFacturaAsync |

## File Changes

- Create: SmartNet/sugerencia/SmartNet.Sugerencia.Core/CascadaDeSugerencia.cs (pure ranking)
- Create: SmartNet/sugerencia/SmartNet.Sugerencia.Core/ServicioDeSugerencia.cs (orchestration)
- Create: SmartNet/sugerencia/SmartNet.Sugerencia.Core/Result types: EscalonSugerencia, SugerenciaDeCuenta, SugerenciaDeMotivo, SugerenciaParaFactura
- Create: SmartNet/sugerencia/SmartNet.Sugerencia.Core.Tests/ (TDD suite)
- Modify: SmartNet/sugerencia/SmartNet.Sugerencia.Core.csproj
- Modify: SmartNet/sugerencia/SmartNet.Sugerencia.Core.Tests.csproj
- Modify: SmartNet.sln (add sugerencia folder and 2 projects)
- Modify: .github/workflows/ci.yml (add SmartNet.Sugerencia.Core.Tests to static verification job)
- Create: PurityScanTests.cs (verify no Task/HttpClient/SqlClient/DateTime.Now)
- Modify: NoRankingStructuralTests.cs (extend to verify Catalogos.Core has no ranking methods)

## Testing Strategy

**Phases 1-4:** TDD (RED → GREEN) for each requirement.
- Phase 1: Scaffolding (csproj, sln, result records)
- Phase 2-3: Pure cascade logic with all 3 tiers and tie-breaks
- Phase 4: Fundamento rationale calculation, then orchestrator wiring

**Phase 5:** Orchestrator (`ServicioDeSugerencia`) with full ctor and method tests.

**Phase 6:** Structural guards — PurityScanTests and extended NoRankingStructuralTests.

**Phase 7:** E2E spec compliance verification — all 12 scenarios from spec.md mapped to passing tests.

All tests live in SmartNet.Sugerencia.Core.Tests and SmartNet.Catalogos.Infrastructure.Tests (for NoRankingStructuralTests extension).
