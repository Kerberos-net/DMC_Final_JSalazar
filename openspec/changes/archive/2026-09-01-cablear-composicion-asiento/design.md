# Design: Wire ComposicionDeAsiento into the productive asiento lifecycle (BACKLOG #24)

## Technical Approach

Owner Option 3 (hybrid). `abrir` becomes: load factura → resolve external facts (Infrastructure) →
build `EntradaAsiento` (pure Core) → `ComposicionDeAsiento.Componer` (untouched) → persist header +
N lines in the same transaction. Manual line edits (#12) and #19's scalar projection layer on top.
`validar` keeps calling `InvariantesDeConfirmacion.Evaluar` against the *persisted* asiento — the
code does not change, only its inputs stop being empty. Promotion fires the same seed so the SPA
always finds an asiento. `recomponer` is the explicit regeneration escape hatch.

Scope: factura/boleta, gravada and no gravada. NC composition, percepción: non-goals.

---

## A. The `EntradaAsiento` builder

### Decision A1 — Where the builder lives

| Option | Tradeoff | Decision |
|---|---|---|
| Pure static in `facturacion/SmartNet.Facturacion.Core` fed by resolved facts | ADR 0019 level 1; unit-testable with no DB; mirrors the `ProveedorResuelto` precedent | **Chosen** |
| Builder reads catálogo itself | Breaks `PurityScanTests`, violates ADR 0019 | Rejected |
| Builder in `contable/SmartNet.Contable.Core` | That module must not know `FacturaPersistida` (a facturación shape) | Rejected |

New files in `SmartNet.Facturacion.Core`:

```csharp
/// Facts resolved by Infrastructure and passed in (ProveedorResuelto precedent, ADR 0019).
public sealed record HechosDeComposicion(
    bool EsRelacionada,              // fact.ProveedorAtributo.EsRelacionada (default false when absent)
    string? MotivoDescripcion,       // dbo.Motivo.descripcion by fact.Factura.Motivo; null when Motivo is null
    TipoCambioCongelado? TipoCambio, // ITipoCambioRepository.ObtenerVigenteAsync(FechaEmision); null for PEN
    CuentaContable? CuentaSugerida); // ServicioDeSugerencia winner, resolved via ObtenerAsync; null = no suggestion

public static class SembradoDeAsiento
{
    public static AsientoContable Sembrar(FacturaPersistida factura, HechosDeComposicion hechos);
    public static EntradaAsiento Construir(FacturaPersistida factura, HechosDeComposicion hechos);
}
```

### Field-by-field mapping (`Construir`)

| `EntradaAsiento` field | Source |
|---|---|
| `ProveedorCodigo` | `factura.ProveedorCodigo` |
| `EsRelacionada` | `hechos.EsRelacionada` |
| `Moneda` | `factura.Moneda == "PEN" ? Pen : Usd` |
| `FechaContable` | `factura.FechaEmision` (unchanged from today's `CrearAsientoBorradorAsync`) |
| `MotivoDescripcion` | `hechos.MotivoDescripcion` |
| `Comprobante` | `CodigoComprobante.Convertir(factura.TipoComprobante)` |
| `Afectacion` | `MapearAfectacion(factura.Afectacion)` |
| `IgvOrig` | `factura.IgvOrig ?? 0m` |
| `BaseOrig` | `factura.TotalOrig - (factura.IgvOrig ?? 0m)` |
| `PercepcionOrig` | `0m` — non-goal; no `fact.Factura` column exists |
| `TipoCambio` | `hechos.TipoCambio` (null for PEN → `Componer` skips §6 conversion) |
| `Cargos` | `hechos.CuentaSugerida is null ? [] : [ new CargoSolicitado(cuenta, importePEN) ]` |
| `Herencia` | `null` — NC composition out of scope (dormant `FacturaReferenciaId`) |

`importePEN` for the single default cargo is `ProyeccionDeImportes.Derivar(...).BasePEN` — the same
pure function #19 already uses, so the seed and the D4 scalar projection agree by construction for a
freshly opened factura (gravada → `BasePEN`; boleta / EXONERADA / INAFECTA → `NetoPEN`, per §5).

### Decision A2 — Seed with no suggested account

`Sembrar` calls `Componer` with **zero cargos** and then appends one placeholder line:
`Bloque=PRINCIPAL, Tipo=D, Debe=importePEN, CuentaCodigo=null` (→ `SinCuenta=1`), `Orden = max+1`
(`Orden` is presentation-only per ADR 0006, so appending is legal and avoids renumbering).

Consequence at `validar`: Global-1 still balances; **Global-2 fails** ("1 línea(s) sin cuenta
contable asignada") and PRINCIPAL fails (cargos sum 0). Both messages are truthful and actionable —
and Global-2 is literally ADR 0006's founding requirement ("impedir confirmar mientras exista alguna
línea sin cuenta"), which until now was unreachable. Rejected alternative: seed a sentinel account
code — it would fake a `CuentaCodigo` and silently pass Global-2.

### Decision A3 — How the facts reach Core

| Option | Tradeoff | Decision |
|---|---|---|
| One new `IUnidadDeTrabajo` member `ResolverHechosDeComposicionAsync(FacturaPersistida, ct)` | `ServicioDeFacturas`'s single-dependency ctor is untouched (≈10 test files spared); precedent is `ExisteTipoCambioVigenteAsync`, already a catálogo-ish read on this port | **Chosen** |
| New port injected into `ServicioDeFacturas` ctor | Honest about being non-transactional, but churns every construction site and both fakes | Rejected |
| Resolve in the API endpoint and pass down | Puts accounting orchestration in a controller — forbidden by CLAUDE.md rule 2 | Rejected |

Adapter: `SqlUnidadDeTrabajo` gains the method. `fact.ProveedorAtributo` and `dbo.Motivo` are read
**inside** the transaction via `CrearComando` (one 2-row query); `ITipoCambioRepository` and
`ServicioDeSugerencia` are called on their own connections (they already are today — external
read-only catálogos, ADR 0003). New `ProjectReference`s on
`SmartNet.Facturacion.Infrastructure`: `Catalogos.Core`, `Sugerencia.Core` — same pattern as the
`TiposCambio.*` references added in PR 5.

### Grants — no schema work

`008` already grants `usr_api`: `SELECT fact.ProveedorAtributo` (l.57), `SELECT dbo.Motivo` (l.153),
`SELECT dbo.CuentaContable` (l.151), `SELECT fact.SugerenciaCuenta` (l.59),
`SELECT/INSERT fact.AsientoContableDetalle` (l.52). **No new script, no checksum regeneration.**

---

## B. Persistence of the composed asiento

### Decision B1 — `CrearAsientoBorradorAsync` signature

```csharp
Task<long> CrearAsientoBorradorAsync(long facturaId, AsientoContable asiento, CancellationToken ct);
```

`AsientoContable` already carries every header column (`ProveedorCodigo`, `FechaContable`,
`MotivoDescripcion`, `TipoCambioVenta`, `BasePEN`, `IgvPEN`, `NetoPEN`) plus `Lineas` — passing the
composed aggregate is narrower than adding seven parameters. One INSERT with
`OUTPUT inserted.AsientoContableId`, then a **loop** of parameterized line INSERTs reusing the
existing `AgregarParametrosDeLinea` helper verbatim. Rejected: multi-row `VALUES` tuple building —
N is bounded at ~9 lines (5 for §10.1), so a loop inside one transaction is not a hot path and
reuses tested code. Note for a future reader: revisit if N ever exceeds ~50.

Line inserts here **must not** go through `AgregarLineaAsync` — that does a `TocarEncabezadoAsync`
CAS against a `Version` the caller does not hold; the row was created microseconds ago inside this
transaction and no one else can hold a version for it.

### Decision B2 — `ReemplazarLineasAsync` (recomponer)

```csharp
Task<ResultadoEscritura> ReemplazarLineasAsync(
    long asientoContableId, byte[] versionEsperada, AsientoContable asiento, CancellationToken ct);
```

Order inside one transaction: `TocarEncabezadoAsync` CAS → `DELETE FROM fact.AsientoContableDetalle
WHERE AsientoContableId=@id` → `UPDATE` header scalars (`BasePEN/IgvPEN/NetoPEN/MotivoDescripcion/
TipoCambioVenta/FechaContable`) → re-INSERT the composed lines. BORRADOR-only; the guard lives in
Core (`CargarAsientoAsync` + `Estado` check), matching every `ServicioDeAsientos` command.

### Decision B3 — Audit rows

| Event | Audit | Rationale |
|---|---|---|
| Seed at `abrir` / at promotion | **None** | ADR 0006 / #19 D6: `abrir` is not in the `Accion` enum; the seed is machine output, not a user correction. The asiento row *is* the record. |
| `recomponer` | **One `REPARTO_MANUAL` row** (`EntidadTipo=ASIENTO`, `Campo="Cargos"`, `ValorOriginal`=serialized prior lines, `ValorNuevo`=serialized regenerated lines) | Recomposition destroys the assistant's manual split — that needs a rastro. Reuses `ServicioDeAsientos.SerializarLineas`. |

Rejected: adding a `RECOMPOSICION` value to `CK_AuditoriaCorreccion_Accion` — it needs a new schema
script + checksum regeneration, and the proposal's rollback plan promises "no schema changes".
`REPARTO_MANUAL` is semantically exact: the reparto was replaced.

### Decision B4 — `CargarAsientoAsync`

**Unchanged.** It already reads the three scalars from the header and the lines from
`fact.AsientoContableDetalle`; the `?? 0m` coalesce stays as the defence for pre-#24 rows. The only
difference is that the values are now engine-produced instead of NULL.

---

## C. `abrir` / `recomponer` / promotion wiring

### `ServicioDeFacturas.AbrirAsync`

Confirmed orchestrator default (a): **seed only on first create; idempotent no-op otherwise.**

```
CargarFacturaAsync ─ null ─▶ 404
        │
   ObtenerAsientoVigenteIdAsync ─ not null ─▶ Commit, 200 (no-op; lines untouched)
        │
   Moneda != PEN && !ExisteTipoCambioVigenteAsync ─▶ 409 SinTipoCambio  (unchanged gate)
        │
   ResolverHechosDeComposicionAsync ─▶ SembradoDeAsiento.Sembrar ─▶ CrearAsientoBorradorAsync ─▶ Commit
```

The idempotent branch deliberately does **not** re-seed: a returning user's manual split must
survive a second `abrir`. `recomponer` is the explicit opt-in.

### Decision C1 — `recomponer` endpoint shape

`POST /api/asientos/{id}/recomponer`, `If-Match` required, optional body `{ "cuentaCodigo": "631111" }`,
returns the recomposed `AsientoRespuesta` + new `ETag` (reuses
`AsientoEndpoints.ResponderConAsientoActualizadoAsync`). Implemented as
`ServicioDeAsientos.RecomponerAsync`.

Rejected `POST /api/facturas/{id}/recomponer`: the mutation targets the asiento aggregate and needs
its ETag; `/abrir` is factura-shaped only because no asiento exists yet to name. The SPA already
holds both `asientoContableId` and the ETag.

The optional `cuentaCodigo` closes the A2 loop: when there was no suggestion, the assistant picks an
account once and gets a fully composed, DESTINO-complete asiento in one action, instead of hand-
building the reflejo/puente pair. When absent, the sugerencia cascade runs again.

### Decision C2 — Promotion auto-seed without coupling Inbox to Facturación

`SmartNet.Inbox.Infrastructure` references only `Inbox.Core`. Rather than break that:

```
Inbox.Core          NEW port:  ISembradorDeAsiento { Task SembrarAsync(long facturaId, CancellationToken ct); }
SmartNet.Api        NEW adapter: SembradorDeAsientoPorServicio → ServicioDeFacturas.AbrirAsync
                    (composition root already references both modules)
```

Exact call site — `PromocionBackgroundService.PromoverAsync`, last statement, replacing the
currently-discarded return value:

```csharp
var resultado = await _promocionRepository.PromoverAsync(
    pendiente.InboxEventId, pendiente.ProcesamientoId, facturaPromovida, documentoPromovido, ct);

// BACKLOG #24 -- best-effort seed AFTER the promotion transaction committed.
await _sembradorDeAsiento.SembrarAsync(resultado.FacturaId, ct);
```

Non-disturbance of shipped #25/#26:
- `ProcesarDocumentoAsociadoAsync` (#25 merge/defer/discard) is **not touched** — it creates zero
  `fact.Factura` rows and never calls `PromoverAsync`. The `Fusiona` branch already runs against an
  invoice that was seeded on its own promotion.
- `DescartarAsync` branch: untouched.
- #26 re-emit / idempotent re-promotion (`ResultadoPromocion.YaExistia == true`): still calls
  `SembrarAsync`. This is safe *and useful* — `AbrirAsync` is idempotent, and it re-attempts a seed
  that previously failed for lack of a tipo de cambio.

### Decision C3 — Seed failure at promotion

The adapter **swallows** a `Conflicto(SinTipoCambio)` / `NoEncontrado` result and never throws; an
exception here would abort the whole `foreach` in `ProcesarPendientesAsync` and strand the remaining
pending events. Promotion still succeeds; the factura simply has no asiento yet. Confirms owner
intent: the detalle screen keeps a "generar asiento" affordance for exactly this case, and the next
promotion cycle does not retry (the event is already `PROMOVIDO`) — the user's button is the retry.

---

## D. §7 reconciliation with #19's scalar projection

`ProyeccionDeImportes.Derivar` and `PatchAsync`'s D4 block stay **byte-for-byte unchanged** (owner
decision 4). `ActualizarProyeccionEscalarAsync` writes only the three header scalars.

After a base/IGV edit on an asiento with a manual split, the header moves and the lines do not:

- **Invariant**: `InvarianteContable.Principal`, first branch of `EvaluarPrincipal`.
- **Exact message**: `$"Los cargos 6x/1x suman {sumaCargos}, se esperaba {esperadoCargos}."` where
  `esperadoCargos = esGravada ? asiento.BasePEN : asiento.NetoPEN`.
- **HTTP**: 422 `InvariantesIncumplidas` (not remapped — only `FechaAnteriorAlCorte` and
  `ProveedorVarios` are remapped to 409 by `MapearAConflictoDeNegocio`).
- **Way out**: edit the líneas (#12), or press *recomponer*.

`InvariantesDeConfirmacion.cs` is **not modified by this change.** Its inputs stop being empty; that
is the whole point of #24.

---

## E. SPA

| File | Action | Change |
|---|---|---|
| `detalle/data-access/asiento.service.ts` | Modify | `recomponer(asientoId, cuentaCodigo?)` — POST with `If-Match`, `aplicar(respuesta)` (same shape as `actualizarLinea`) |
| `detalle/feature/detalle-page.ts` / `.html` | Modify | "Recomponer asiento" button (BORRADOR only); keep a "generar asiento" button calling `/abrir` when `asiento()` is null (the no-TC case); descuadre marker bound to the existing `cuadre()` computed |
| `detalle/ui/factura-form/*` | **No change** | Already binds `asiento()?.basePEN` / `igvPEN`; they simply stop being null |
| `detalle/ui/asiento-lineas/*` | **No change** | Still editable on BORRADOR |
| `detalle/data-access/cuadre.ts` | **No change** | `calcularCuadre` already computes the Debe/Haber marker; reuse it read-only as #23 does |

No new component is needed for the descuadre marker — `cuadre()` already exists in `detalle-page.ts`
and only lacked lines to sum.

---

## F. Testing Strategy (Strict TDD, ADR 0019 levels)

| Level | Project | What | Approach |
|---|---|---|---|
| 1 pure | `Facturacion.Core.Tests` | **NEW** `SembradoDeAsientoTests` — field-by-field mapping; PEN vs USD; gravada vs boleta; no-suggestion placeholder line; `Cargos` empty ⇒ balanced | No DB, no clock; `PurityScanTests` guards it |
| 1 pure | `Contable.Core.Tests` | `ComponerGoldenTests` | **Untouched** — `Componer` does not change |
| 2 boundary | `Facturacion.Infrastructure.Tests` | **NEW** `SqlUnidadDeTrabajoSembradoTests` — seed persists header scalars + N lines; `ReemplazarLineasAsync` deletes+reinserts under CAS; stale ETag ⇒ `VersionEnConflicto`; `ResolverHechosDeComposicionAsync` reads `ProveedorAtributo`/`dbo.Motivo` | Real versioned schema via `TestDatabaseFixture` |
| 3 orchestration | `Facturacion.Core.Tests` | `ServicioDeFacturasPhase2Tests` — `abrir` composes; second `abrir` is a no-op that does not touch lines; foreign currency w/o TC still 409 before any write. **NEW** `ServicioDeAsientosRecomponerTests` — replaces lines, writes one `REPARTO_MANUAL`, 409 on CONFIRMADO | `FakeUnidadDeTrabajo` gains `HechosACargar` + records `CrearAsientoBorradorAsync(asiento)` / `ReemplazarLineasAsync` |
| 3 orchestration | `Contable.Core.Tests` | `InvariantesDeConfirmacionTests` — migrate the vacuous fixtures to real `Sembrar` output. Expected churn: the "empty asiento is confirmable" cases invert; PRINCIPAL/DESTINO/Global-1/2/5 get real assertions | The §10 goldens become the fixture source |
| 3 | `Inbox.Infrastructure.Tests` | `PromocionBackgroundService` calls `ISembradorDeAsiento` once per promoted factura; **zero** calls on the #25 `Fusiona`/`Difiere`/`Descarta` paths; a failing sembrador does not abort the cycle | Fake sembrador + existing one-cycle driver |
| 4 E2E | `SmartNet.Api.Tests` | `FacturaEndpointsTests` — POST `/abrir` → GET `/asiento` asserts REGLAS **§10.1** (5 lines, 1000/180/1180 + 946311/791111), **§10.2** (2+2 lines, 1180 total, no 401111), **§10.3** (USD, TC 3.7895 → 3789.50/682.11/4471.61, cuenta 431212) → POST `/validar` → 200 with `NumeroAsiento`. Plus: PATCH base/IGV on a manual split → `/validar` 422 with the descuadre message → POST `/recomponer` → `/validar` 200 | `WebApplicationFactory` + real schema, existing `FacturaTestDataHelper` |
| SPA | Vitest | `asiento.service.spec` (recomponer threads ETag), `detalle-page.spec` (button visible on BORRADOR only; "generar asiento" shown when `asiento()` is null) | Existing `HttpTestingController` patterns |

RED-first ordering per phase: builder tests → persistence tests → orchestration → E2E.

---

## Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or
process-integration boundary. The one new endpoint reuses the existing `RequireAuthorization()` +
`If-Match` surface; the background-service change adds no new process or external call.

---

## G. Size + delivery

Estimated changed lines (additions + deletions, authored):

| Slice | Est. |
|---|---|
| Core builder + `HechosDeComposicion` + `IUnidadDeTrabajo`/`FakeUnidadDeTrabajo` + `AbrirAsync` | ~330 |
| `SqlUnidadDeTrabajo` (seed, reemplazar, resolver) + csproj refs | ~280 |
| Invariant/Phase2 fixture migration + new Core/Infra tests | ~600 |
| API E2E | ~180 |
| sugerencia DI + resolver wiring | ~90 |
| promotion port + adapter + service + tests | ~190 |
| recomponer endpoint + service + tests | ~200 |
| SPA + specs | ~220 |
| **Total** | **≈1,750–1,950** |

Session strategy is `single-pr` with an 800-line budget. **This exceeds it by >2×.**

`Decision needed before apply: Yes`
`Chained PRs recommended: Yes`
`400-line budget risk: High`

Recommended Feature Branch Chain on `item-24-cablear-composicion-asiento`:

| PR | Scope | Est. | Standalone verification |
|---|---|---|---|
| **PR1** | `HechosDeComposicion` + `SembradoDeAsiento` + `IUnidadDeTrabajo`/`SqlUnidadDeTrabajo` seed & resolver (sugerencia **not** wired ⇒ `CuentaSugerida` always null ⇒ placeholder line) + `AbrirAsync` composes + §7 de-vacuuming test migration | ~800 | `dotnet test`; `/abrir` produces a balanced asiento whose only failure at `validar` is "línea sin cuenta" |
| **PR2** | `ServicioDeSugerencia` into `Program.cs` DI + resolver returns the suggested `CuentaContable` | ~200 | `dotnet test`; §10.1–§10.3 E2E goes green end to end |
| **PR3** | `recomponer` endpoint + `ISembradorDeAsiento` promotion auto-seed + SPA (buttons, service, descuadre marker) | ~750 | `dotnet test` + `npm test`; promotion→detalle→recomponer→validar walkthrough |

PR1 targets the feature branch; PR2 targets PR1's branch; PR3 targets PR2's branch. The split is
natural because PR1's placeholder-line behaviour is a *correct, shippable* state (ADR 0006's
"no confirmar con línea sin cuenta"), not a stub.

---

## Migration / Rollout

No schema change, no data migration (dev/demo only, per the proposal). Existing dev asientos with
NULL scalars and zero lines become unconfirmable the moment PR1 lands — intended: they were only
ever confirmable because §7 evaluated nothing. `recomponer` regenerates them. Code-only rollback:
revert the merge and `abrir` returns to header-only seeding.

---

## H. Open Questions / owner ruling

- [ ] **REGLAS §12 (points 1 and 5) — recommendation: a note is enough, no ratification gate.**
  #24 wires §5–§7 into production but changes no rule; §12's own text already declares the six
  rules unratified and the project an academic demonstration with the risk deliberately accepted.
  Point 5 (NC inherits the factura's TC) is *unreachable* this cycle — NC composition is a declared
  non-goal and `FacturaReferenciaId` is never populated. Point 1 (TC venta) already executes today
  via #19's `ProyeccionDeImportes`; #24 does not widen that exposure, it makes the lines agree with
  scalars that are already being written. **Recommendation**: add one line to `DEUDA-TECNICA.md`
  noting that §5–§7 are now executable in production on unratified TC rules, and ship. Not CRITICAL.
- [ ] **Confirm C3**: promotion seed failure (foreign currency, no TC) leaves the factura with no
  asiento and no automatic retry; the detalle "generar asiento" button is the retry. Stated as owner
  intent in the brief — flagged here for explicit confirmation, not blocking.

No CRITICAL blockers. Design is ready for `sdd-tasks`.
