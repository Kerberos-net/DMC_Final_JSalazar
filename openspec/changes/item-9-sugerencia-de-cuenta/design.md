# Design: Sugerencia de cuenta (BACKLOG #9)

## Technical Approach

New bounded-context module `SmartNet/sugerencia/`, mirroring the layout every other capability
already uses (`auth`, `catalogos`, `tipos-de-cambio`, `inbox`, `contable`). Two projects: a pure
`SmartNet.Sugerencia.Core` holding the cascade, and its test project. **No `.Infrastructure`
project**: #9 adds no SQL adapter — it consumes ports item #3 already shipped. No schema change:
`fact.SugerenciaCuenta` and its `fact_api` grants exist (`004_satelites_datos_maestros.sql:35`,
`008_usuarios_y_permisos.sql:59`). ADR 0016 is untouched.

## Architecture Decisions

### Decision 1: New `SmartNet.Sugerencia.Core`, not an extension of `SmartNet.Catalogos.Core`

| Option | Tradeoff | Decision |
|---|---|---|
| Extend `Catalogos.Core` | Zero new csproj, but ranking would live in the assembly item #3 deliberately excluded it from | Rejected |
| New `Sugerencia.Core` | 2 csproj + sln entries; boundary becomes structural, not nominal | **Chosen** |

`NoRankingStructuralTests.cs` asserts by reflection **only over `ISugerenciaCuentaRepository`'s
members** (exactly 4, none ranking-shaped). Adding a `CascadaDeSugerencia` class to
`Catalogos.Core` would not literally fail it — but it would defeat its documented intent and leave
the boundary unguarded at assembly level. A separate assembly lets #9 *strengthen* the guard
(Decision 4). Namespace `SmartNet.Sugerencia.Core` also avoids colliding with the existing storage
record `SmartNet.Catalogos.Core.SugerenciaCuenta`.

### Decision 2: Orchestrator lives in `Sugerencia.Core`, not in an `.Infrastructure` project

It depends only on ports (`ISugerenciaCuentaRepository`, `ICuentaContableRepository`,
`IMotivoRepository`, `IMotivoAtributoRepository`) and needs no SqlClient, no hosting, no clock —
unlike `PromocionBackgroundService`, which is in Infrastructure precisely because it needs both.
The purity scan stays green and applies to the orchestrator too. Rejected: a new `.Application`
project — no such layer exists anywhere in this solution.

### Decision 3: `Fundamento` denominator counts only currently-valid candidates

> **Extended during PR 1 implementation (owner-confirmed 2026-08-20).** `SugerirMotivo`'s own
> `VecesDelAmbito` was left undefined by this decision (it only covered `SugerenciaDeCuenta`).
> Confirmed with the project owner: it is the **total `Veces` across every offerable motivo for
> the provider** (sum over all motivos with `Activo && OrigenLibro == "02"`), not just the
> winning motivo's own count — matching what PR 1 already implemented
> (`CascadaDeSugerenciaTests.SugerirMotivo_ReturnsProvidersMostUsedMotivo`). No code change
> required.

ADR 0011 shows *"usado 14 de 15 veces"* but never defines the denominator, and the proposal
(`proposal.md:63`) closed ADR 0011 to further edits in this change ("ya corregido a revisión 4,
no se toca en esta propuesta"). Choice: sum of `Veces` over the rows **that survived the
`ResolverCandidatas` filter** in the winning tier. Rejected: all stored rows — it would show a
fraction over history the assistant cannot act on. This is a **design-level clarification only**:
it is not written back into the ADR, it lives in this document and in `SugerenciaDeCuenta`'s XML
doc comment (`VecesDelAmbito`). No ADR file is touched by this change.

### Decision 4: Determinism is enforced by the core, not assumed from the caller

`ResolverCandidatas` already returns ordinal-ascending, but the cascade re-derives the minimum
itself instead of taking `[0]`, so an unsorted caller cannot make the suggestion non-deterministic.

## Data Flow

    #11 / #12 ──→ ServicioDeSugerencia ──→ IMotivoRepository (prefijos)
                        │                  ICuentaContableRepository (plan)
                        │                  IMotivoAtributoRepository (activo + origen 02)
                        │                  ISugerenciaCuentaRepository (Listar* only)
                        ↓
                  ResolverCandidatas (Catalogos.Core, pure)
                        ↓
                  CascadaDeSugerencia (pure) ──→ SugerenciaParaFactura

`RegistrarUsoAsync` is never called from this module — writing is item #11's job.

## File Changes

| File | Action | Description |
|---|---|---|
| `SmartNet/sugerencia/SmartNet.Sugerencia.Core/SmartNet.Sugerencia.Core.csproj` | Create | net10.0, zero PackageReference, ProjectReference → `Catalogos.Core` |
| `.../CascadaDeSugerencia.cs` | Create | Pure 3-tier cascade + tie-break |
| `.../SugerenciaDeCuenta.cs`, `SugerenciaDeMotivo.cs`, `EscalonSugerencia.cs`, `SugerenciaParaFactura.cs` | Create | Result records incl. `Fundamento` |
| `.../ServicioDeSugerencia.cs` | Create | Single entry point for #11/#12 |
| `SmartNet/sugerencia/SmartNet.Sugerencia.Core.Tests/*` | Create | Cascade cases, purity scan, structural guards |
| `SmartNet/SmartNet.sln` | Modify | Add `sugerencia` solution folder + 2 projects |

## Interfaces / Contracts

```csharp
public enum EscalonSugerencia { ProveedorYMotivo = 1, MotivoGlobal = 2, PrimeraCandidata = 3 }

public sealed record SugerenciaDeCuenta(
    string CuentaCodigo, EscalonSugerencia Escalon, int Veces, int VecesDelAmbito, string Fundamento);

public sealed record SugerenciaDeMotivo(int Motivo, int Veces, int VecesDelAmbito, string Fundamento);

public sealed record SugerenciaParaFactura(
    SugerenciaDeMotivo? Motivo, SugerenciaDeCuenta? Cuenta, IReadOnlyList<CuentaContable> CandidatasVigentes);

public static class CascadaDeSugerencia
{
    // No Task, no port, no clock — enforced structurally.
    public static SugerenciaDeCuenta? SugerirCuenta(
        IReadOnlyList<SugerenciaCuenta> usoDelProveedorEnElMotivo,
        IReadOnlyList<SugerenciaCuenta> usoGlobalDelMotivo,
        IReadOnlyList<CuentaContable> candidatasVigentes);

    public static SugerenciaDeMotivo? SugerirMotivo(
        IReadOnlyList<SugerenciaCuenta> usoDelProveedor, IReadOnlySet<int> motivosOfrecibles);
}

public sealed class ServicioDeSugerencia   // ctor takes the 4 ports
{
    public Task<SugerenciaParaFactura> SugerirParaFacturaAsync(
        string proveedorCodigo, int? motivoSeleccionado, CancellationToken ct);
}
```

**Cascade algorithm** (`SugerirCuenta`). Let `vigentes` = `candidatasVigentes[].Cuenta` (ordinal
set); empty ⇒ `null`.
1. Tier 1 — rows of `usoDelProveedorEnElMotivo` with `CuentaCodigo ∈ vigentes` and `Veces > 0`.
2. Tier 2 — same filter over `usoGlobalDelMotivo`.
3. Tier 3 — ordinal minimum of `vigentes`; `Veces = VecesDelAmbito = 0`.

Winner comparator for tiers 1–2 (ADR 0011 rev. 4): **`Veces` DESC → `UltimoUso` DESC →
`CuentaCodigo` ordinal ASC**. `VecesDelAmbito` = sum of `Veces` over the filtered rows of the
winning tier.

`SugerirMotivo`: aggregate `usoDelProveedor` by `Motivo` (sum `Veces`, max `UltimoUso`), keep only
`motivosOfrecibles` (`Activo && OrigenLibro == "02"`, computed in the service), then **`Veces` DESC
→ `UltimoUso` DESC → `Motivo` ASC**. No history ⇒ `null` (no tier 2/3 exists for motivo).

## Testing Strategy

| Layer | What | Approach |
|---|---|---|
| Unit | Cascade tiers, both tie-break levels, vigencia filter demoting a tier, empty candidates, `Fundamento` strings, motivo aggregation, unsorted input | xUnit over `CascadaDeSugerencia`, plain in-memory lists (no DB/HTTP/clock, ADR 0019) |
| Unit | `ServicioDeSugerencia` wiring, `motivoSeleccionado` null vs. supplied, motivo with no candidates | Fake in-memory ports; assert `RegistrarUsoAsync` is never invoked |
| Structural | `Sugerencia.Core.dll` purity scan (copy of `Contable.Core.Tests/PurityScanTests.cs`, incl. the `DateTime.Now/UtcNow` IL scan); `CascadaDeSugerencia` exposes no `Task`-returning member; `Catalogos.Core.dll` contains **no** ranking-shaped type | Reflection + Mono.Cecil + NetArchTest |
| Integration | None | #9 adds no adapter; `SqlSugerenciaCuentaRepository` is already covered by item #3 |

## Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or
process-integration boundary.

## Migration / Rollout

No migration. Additive code only; reverting the PR removes the capability without side effects.

## Open Questions

- [ ] `Fundamento` denominator (Decision 3) is a design-level clarification, not an ADR change —
      flag it in the PR so the owner can object; if objected, it becomes a scoped ADR 0011
      revision 5 in a follow-up, not in this change.
