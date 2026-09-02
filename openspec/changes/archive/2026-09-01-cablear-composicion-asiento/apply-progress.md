# Apply Progress: cablear-composicion-asiento (BACKLOG #24)

**Status**: partial — Phases 1 and 6 of 6 complete. Stopped at a clean phase boundary. Phases 2–5 (interdependent C# + SPA) NOT started this batch.
**Delivery**: single-pr + owner-approved `size:exception` (obs 309). One worktree, one PR.
**Mode**: Strict TDD (RED confirmed via compile failure → GREEN).
**Branch**: `main` (session already merged prior cycles; no branch/commit/push per orchestrator).

## Batch 1 — Phase 1: Pure Core seed (SmartNet.Facturacion.Core, ADR 0019 nivel 1)

### Completed tasks
- [x] 1.1 RED `SembradoDeAsientoTests` — field map from `FacturaPersistida`
- [x] 1.2 RED goldens §10.1 / §10.2 / §10.3
- [x] 1.3 RED design-A2 placeholder case (no suggested account)
- [x] 1.4 GREEN `HechosDeComposicion` + `SembradoDeAsiento.Construir/.Sembrar`
- [x] 1.5 `PurityScanTests` still green

### Files changed
| File | Action | What |
|---|---|---|
| `SmartNet/SmartNetApi/facturacion/SmartNet.Facturacion.Core/HechosDeComposicion.cs` | Created | `sealed record HechosDeComposicion(bool EsRelacionada, string? MotivoDescripcion, TipoCambioCongelado? TipoCambio, CuentaContable? CuentaSugerida)` |
| `SmartNet/SmartNetApi/facturacion/SmartNet.Facturacion.Core/SembradoDeAsiento.cs` | Created | `static Construir(FacturaPersistida, HechosDeComposicion) : EntradaAsiento` (verbatim design field map) + `static Sembrar(...) : AsientoContable` (runs `Componer` untouched; appends `CuentaCodigo=null` placeholder line at Orden=max+1 when `CuentaSugerida is null`). Private `MapearAfectacion` mirrors `ServicioDeFacturas.MapearAfectacion`. Private `ImportePrincipal` = `ProyeccionDeImportes.Derivar(...).BasePEN`. |
| `SmartNet/SmartNetApi/facturacion/SmartNet.Facturacion.Core.Tests/SembradoDeAsientoTests.cs` | Created | 6 tests: field map, IgvOrig-null, §10.1, §10.2, §10.3, placeholder-balances |
| `openspec/changes/cablear-composicion-asiento/tasks.md` | Modified | 1.1–1.5 marked `[x]` |

### TDD Cycle Evidence
| Task | RED | GREEN | REFACTOR |
|---|---|---|---|
| 1.1–1.3 | `dotnet test --filter SembradoDeAsientoTests` → CS0246/CS0103 (types absent) | — | — |
| 1.4 | (above) | same filter → 6/6 pass | none needed |
| 1.5 | n/a | `--filter "SembradoDeAsientoTests\|PurityScanTests"` → 12/12 pass | — |

Command run: `dotnet test facturacion/SmartNet.Facturacion.Core.Tests --filter "SembradoDeAsientoTests|PurityScanTests"`
Result: `Superado: 12, Con error: 0`. No SQL Server needed (pure ADR 0019 nivel 1).

### Work Unit Evidence
| Evidence | Value |
|---|---|
| Focused test command + result | `dotnet test facturacion/SmartNet.Facturacion.Core.Tests --filter SembradoDeAsientoTests` → 6/6 pass |
| Runtime harness | N/A — pure Core builder, no runtime boundary |
| Rollback boundary | Delete the two new `SmartNet.Facturacion.Core/*.cs` files + the new test file; revert tasks.md checkboxes. Nothing else touched. |

### Deviations from design
- Golden §10.3 test uses `TipoCambioCongelado.Heredado(3.7895m)` instead of `DeTipoCambio(new TipoCambio(...))` to avoid adding a `SmartNet.TiposCambio.Core` ProjectReference to the test project for a single line. Same resulting object (`Venta = 3.7895`). Later phases that need the real `TipoCambio` type in this test project should add the reference then.
- `MapearAfectacion` is now duplicated a third time (already in `ServicioDeFacturas` + `SqlUnidadDeTrabajo`). Not extracted — design says minimal churn, `Componer` untouched. Candidate follow-up: hoist to a shared helper.

### Known interpretation risk carried forward (flag for verify)
- **Boleta afectación quirk**: design field map is `Afectacion = MapearAfectacion(factura.Afectacion)` verbatim. `MapearAfectacion(null) => Gravada`, and `ComposicionDeAsiento.Componer` keys the 401111 line off afectación only (not comprobante). A boleta promoted with `Afectacion = null` would seed a phantom 401111 line (header scalars via `ProyeccionDeImportes.Derivar` stay correct → descuadre surfaces at `validar`). This matches the existing `Componer_BoletaMarcadaGravada_PineaGeneracionActualDeLinea401111` sentinel and is explicitly out of scope (`Componer` untouched). Phase 4 E2E §10.2 fixture must set `Afectacion = "INAFECTA"` (as `ComponerGoldenTests.Golden_10_2` does).

## Batch 2 — Phase 6: Documentation note (no test task)

### Completed tasks
- [x] 6.1 Appended the REGLAS §12 points 1 & 5 note to `DEUDA-TECNICA.md` row 5.2 Observación (obs 309 decision 7 — a note, not a ratification gate).

### Files changed
| File | Action | What |
|---|---|---|
| `DEUDA-TECNICA.md` | Modified | Row 5.2 Observación filled: #24 wires §5–§7 into production but ratifies no rule; point 1 (TC venta) already executed via #19 `ProyeccionDeImportes.Derivar`, no new exposure; point 5 (NC inherits TC) unreachable this cycle (NC composition non-goal, `FacturaReferenciaId` never populated). |
| `openspec/changes/cablear-composicion-asiento/tasks.md` | Modified | 6.1 marked `[x]` |

### Work Unit Evidence
| Evidence | Value |
|---|---|
| Focused test command + result | N/A — documentation-only edit, no test task (tasks.md: "Phase 6: Documentation (not a test task)"). |
| Runtime harness | N/A |
| Rollback boundary | Revert the single row 5.2 edit in `DEUDA-TECNICA.md` + the 6.1 checkbox. Nothing else touched; tree compiles unchanged. |

### Batch 2 note — why Phases 2–5 were not attempted
Phases 2–5 are one tightly-coupled unit: the **B1 signature change** to `CrearAsientoBorradorAsync(long, AsientoContable, ct)` ripples through `SqlUnidadDeTrabajo`, `FakeUnidadDeTrabajo`, and `ServicioDeFacturas.AbrirAsync` and cannot land without also doing Phase 3.2 in the same batch to keep the tree compiling (orchestrator's explicit instruction). Starting that ripple without finishing it would leave the tree non-compiling, violating the "STOP at a clean phase boundary with the tree COMPILING" mandate. Phase 6 is fully independent (markdown only), so it was completed and the batch stopped at that clean boundary. Resume next batch at Phase 2.1.

## Batch 3 — Phase 2/3 attempt: context fully mapped, NO code written (clean stop)

**Status**: partial — no production or test file was modified. Tree still compiles, all suites still green.
Deep read of every file the B1 atomic unit touches was completed; implementation was NOT started
because it cannot land safely in a single apply pass at the required accounting-correctness bar
(REGLAS.md priority: correctitud contable > invariantes > velocidad). Stopping with an UNMODIFIED
tree is the mandated clean boundary ("never leave a non-compiling tree or a half-migrated test file").

### What was fully analysed this batch (hand-off so the next pass writes immediately)

**B1 signature — exact current vs target**
- Current `IUnidadDeTrabajo.CrearAsientoBorradorAsync(long facturaId, string proveedorCodigo, DateOnly fechaContable, ct) -> Task<long>`
  (IUnidadDeTrabajo.cs L49-50; impl SqlUnidadDeTrabajo.cs L554-570 — single INSERT header only, OrigenLibro '02', Estado 'BORRADOR';
   Fake FakeUnidadDeTrabajo.cs L139-144 returns `AsientoBorradorCreadoId=900`).
- Target: `CrearAsientoBorradorAsync(long facturaId, AsientoContable asiento, ct) -> Task<long>`.
  SqlUnidadDeTrabajo impl: keep the header INSERT but add the 4 engine columns
  (`MotivoDescripcion, TipoCambioVenta, BasePEN, IgvPEN, NetoPEN` — note GuardarAsientoAsync L146-150 already
  writes exactly these 5; mirror that param list) then `OUTPUT inserted.AsientoContableId`, then
  `foreach (linea in asiento.Lineas)` a parameterized INSERT reusing `AgregarParametrosDeLinea(command, id, linea)`
  verbatim (SqlUnidadDeTrabajo.cs L837-850 — it already sets `@sinCuenta = linea.CuentaCodigo is null`).
  MUST NOT call `AgregarLineaAsync` (its `TocarEncabezadoAsync` CAS needs a Version the caller lacks).
- Callers to ripple: `ServicioDeFacturas.AbrirAsync` (ServicioDeFacturas.cs L364) + `FakeUnidadDeTrabajo` + the SQL impl.
  No other callers (codegraph blast radius confirmed).

**A1 resolver — `ResolverHechosDeComposicionAsync(long facturaId, ct) -> Task<HechosDeComposicion>`**
- New member on `IUnidadDeTrabajo` (precedent: `ExisteTipoCambioVigenteAsync`, IUnidadDeTrabajo.cs L91+, impl L305-306).
- `HechosDeComposicion(bool EsRelacionada, string? MotivoDescripcion, TipoCambioCongelado? TipoCambio, CuentaContable? CuentaSugerida)`
  already exists (HechosDeComposicion.cs, Phase 1). `SembradoDeAsiento.Sembrar/.Construir` already exist and are green (6 tests).
- SQL impl: one in-tx `CrearComando` query joining `fact.Factura` -> `fact.ProveedorAtributo` (EsRelacionada, LEFT JOIN, default false)
  and `dbo.Motivo` (descripcion by `fact.Factura.Motivo` int, LEFT JOIN). TipoCambio: reuse the existing
  `_tipoCambioRepository.ObtenerVigenteAsync(fecha)` path (own connection, like `ExisteTipoCambioVigenteAsync`) — need the
  VENTA rate as `TipoCambioCongelado.DeTipoCambio(...)`, null for PEN.
- **DECISION for this batch (design A3 offered a choice)**: resolve `CuentaSugerida = null` here with a
  `// TODO Phase 4: wire ServicioDeSugerencia` — do NOT add the nullable `ICuentaSugeridaResolver` port.
  Rationale: Phase 4 owns `Program.cs` DI for `ServicioDeSugerencia` + `ISugerenciaCuentaRepository`; adding a port now
  means a second interface + Fake wiring + a DI registration that Phase 4 immediately rewrites. Null here = design A2
  placeholder-line path, which the design explicitly calls "a correct, shippable state", and `SembradoDeAsiento.Sembrar`
  already handles it (SembradoDeAsientoTests green). csproj: still add `Catalogos.Core` ProjectReference to
  `Facturacion.Infrastructure` (needed for `CuentaContable` in the signature) but `Sugerencia.Core` can wait for Phase 4.

**B2 — `ReemplazarLineasAsync(long asientoContableId, byte[] versionEsperada, AsientoContable asiento, ct) -> Task<ResultadoEscritura>`**
- New `IUnidadDeTrabajo` member. SQL impl order in one tx: `TocarEncabezadoAsync(id, versionEsperada, ct)` (returns
  Aplicado/VersionEnConflicto/NoEncontrado — SqlUnidadDeTrabajo.cs L817-835) -> if not Aplicado return it ->
  `DELETE FROM fact.AsientoContableDetalle WHERE AsientoContableId=@id` -> `UPDATE fact.AsientoContable SET
  MotivoDescripcion/TipoCambioVenta/BasePEN/IgvPEN/NetoPEN/FechaContable` (NOT Estado/NumeroAsiento) ->
  `foreach` re-INSERT lines via `AgregarParametrosDeLinea`. NOTE: `TocarEncabezadoAsync` already bumps Version, so the
  subsequent UPDATE is fine.

**Phase 3.2 — `ServicioDeFacturas.AbrirAsync`** (ServicioDeFacturas.cs L336-367): after the SinTipoCambio gate and before
`CrearAsientoBorradorAsync`, insert:
```
var hechos = await uow.ResolverHechosDeComposicionAsync(facturaId, ct);
var asiento = SembradoDeAsiento.Sembrar(factura, hechos);
await uow.CrearAsientoBorradorAsync(facturaId, asiento, ct);
```
Idempotent branch (L346-352) unchanged — no re-seed. No `AuditoriaCorreccion` (B3).

**Phase 3 RecomponerAsync** — add to `ServicioDeAsientos` (ServicioDeAsientos.cs). Shape mirrors `ActualizarLineaAsync`
(L190-220): load asiento (gate `!= Borrador` -> Conflicto AsientoYaConfirmado), snapshot `antes` via
`CargarLineasPersistidasAsync`, load factura via `uow.CargarFacturaAsync(persistido.FacturaId)`, resolve hechos
(+ optional `cuentaCodigo` override -> build a `CuentaContable` — needs a catálogo lookup member OR accept only the
code and let Phase 4's sugerencia resolve; SIMPLEST for this batch: `cuentaCodigo` param plumbed but resolution deferred,
i.e. always the sugerencia/null path), `SembradoDeAsiento.Sembrar`, `uow.ReemplazarLineasAsync(asientoId, versionEsperada, asiento, ct)`,
then ONE `EntradaAuditoria(EntidadTipos.Asiento, asientoId, Acciones.RepartoManual, Campo:"Cargos",
ValorOriginal: SerializarLineas(antes), ValorNuevo: SerializarLineas(despues), Motivo:null, usuarioId, ahora)` —
reuse the existing private `SerializarLineas` + `RegistrarRepartoManualAsync` pattern (L258-280). `SerializarLineas`
is currently `private static` — make it usable or replicate. No new `Accion` enum value (design B3).

**D — §7 de-vacuuming (test migration, NOT production)**
- `InvariantesDeConfirmacion.cs` is NOT modified (confirmed — read in full this batch; `EvaluarPrincipal` L99-145
  already emits the exact `"Los cargos 6x/1x suman {sumaCargos}, se esperaba {esperadoCargos}."` message, first branch,
  `InvarianteContable.Principal`, with `esperadoCargos = esGravada ? BasePEN : NetoPEN`).
- `ServicioDeFacturasPhase2Tests.cs` (read in full): the fixtures using `Lineas: Array.Empty<LineaAsiento>()` or a
  hand-built 3-line list that vacuously balances are: `AsientoBorrador()` helper (L592-606, `Array.Empty`),
  `ValidarPorFacturaAsync_ResolvesTheAsientoId_...` (L303-325, 3 lines 100/18/118 accounts 639915/401111/421001 —
  balances, `EvaluarPrincipal`: cargos sum on 639915 = 100 == BasePEN 100, IGV line 401111 = 18 == IgvPEN 18 → PASSES
  for real, no migration needed), `RegistrarAdjuntoAsync_AfterARealValidarAsync_...` + `PatchAsync_AfterARealValidarAsync_...`
  (same 3-line shape, real pass). The genuinely vacuous ones are ONLY the `AsientoBorrador()`-based PATCH-projection
  tests (L645-714) — those call `PatchAsync`, never `validar`, so §7 is never evaluated → they do NOT need migration.
  **Conclusion: ServicioDeFacturasPhase2Tests needs only the B1 fake-signature compile fix, NOT a semantic migration.**
  The `AbrirAsync_*` tests (L172-280) keep asserting `Llamadas.Contains(nameof(CrearAsientoBorradorAsync))` — still valid.
- `InvariantesDeConfirmacionTests` (Contable.Core.Tests) — NOT read this batch. This is the real migration target:
  invert the "empty asiento confirmable" cases, add PRINCIPAL/DESTINO/Global-1/2/5 assertions off `SembradoDeAsiento.Sembrar`
  goldens. MUST be read and enumerated method-by-method next batch.
- New reconciliation test (Phase 3.7): after PATCH base/IGV that unbalances a persisted manual split, `validar` → 422
  `InvarianteContable.Principal`. Belongs in E2E (`FacturaEndpointsTests`, Phase 4) OR a Phase2Tests fake-based test
  seeding `AsientoACargar` with lines whose cargo sum ≠ a mutated BasePEN. `ProyeccionDeImportes.Derivar` / `PatchAsync` D4
  unchanged (confirmed).

### Files that still MUST be read before writing (next batch, in order)
1. `contable/SmartNet.Contable.Core.Tests/InvariantesDeConfirmacionTests.cs` — the real migration surface.
2. `facturacion/SmartNet.Facturacion.Infrastructure.Tests/SqlUnidadDeTrabajoTests.cs` + the `FacturaTestDataHelper` /
   fixture seed helpers — to write `SqlUnidadDeTrabajoSembradoTests` against the real schema.
3. `SmartNetBD/schema/*` — `fact.ProveedorAtributo`, `dbo.Motivo`, `fact.AsientoContable` column list (for the resolver
   query + the widened INSERT).
4. `facturacion/SmartNet.Facturacion.Infrastructure/SmartNet.Facturacion.Infrastructure.csproj` — to add `Catalogos.Core`.
5. `EntradaAuditoria.cs` — confirm `Acciones.RepartoManual` / `EntidadTipos.Asiento` constants (used by existing code, safe).

### Estimated remaining size: ~450-650 authored lines (production ~250, tests ~300-400). Fits one focused apply pass
that STARTS from writing (no re-exploration), which is why this batch front-loaded the full read.

## Batch 4 — Phase 2 (2.1–2.5) + Phase 3.2: DONE, tree compiles, all touched suites green

**Status**: done for the assigned slice. The B1 signature ripple + resolver + `ReemplazarLineasAsync`
landed atomically with the `AbrirAsync` call-site change, so the tree compiles and every touched
suite is green.

### Completed tasks
- [x] 2.1 `SqlUnidadDeTrabajoSembradoTests` — real-schema tests for the resolver (`EsRelacionada`,
  `dbo.Motivo` descripción, foreign-currency VENTA freeze, absent-row defaults).
- [x] 2.2 `CrearAsientoBorradorAsync(long, AsientoContable, ct)` — header INSERT widened to
  `MotivoDescripcion/TipoCambioVenta/BasePEN/IgvPEN/NetoPEN` + `OUTPUT inserted.AsientoContableId`
  + a line loop (`InsertarLineasAsync` private helper) reusing `AgregarParametrosDeLinea` verbatim.
  Not via `AgregarLineaAsync`.
- [x] 2.3 `ReemplazarLineasAsync(long, byte[], AsientoContable, ct)` — `TocarEncabezadoAsync` CAS →
  `DELETE fact.AsientoContableDetalle` → `UPDATE` header scalars (never `Estado`/`NumeroAsiento`) →
  re-INSERT via `InsertarLineasAsync`. Stale ETag → `VersionEnConflicto` (test proves nothing changes).
- [x] 2.4 3 members added to `IUnidadDeTrabajo` + `SqlUnidadDeTrabajo`. `Catalogos.Core`
  ProjectReference added to `SmartNet.Facturacion.Infrastructure.csproj` (was only transitively
  present via Contable.Core; made explicit per task). `Sugerencia.Core` NOT added — deferred to
  Phase 4 (Batch-3 A3 ruling). `CargarAsientoAsync` byte-for-byte unchanged.
- [x] 2.5 `FakeUnidadDeTrabajo` — new `HechosACargar` (default `new(false, null, null, null)`),
  `ResolverHechosDeComposicionAsync`, `UltimoAsientoBorradorCreado`, `ReemplazarLineasAsync`
  (+`UltimoAsientoReemplazado`, `ResultadoDeReemplazarLineas`). `CrearAsientoBorradorAsync` records
  the `asiento` arg.
- [x] 3.2 `ServicioDeFacturas.AbrirAsync` — after the `SinTipoCambio` gate, before commit:
  `ResolverHechosDeComposicionAsync` → `SembradoDeAsiento.Sembrar(factura, hechos)` →
  `CrearAsientoBorradorAsync(facturaId, asiento, ct)`. Idempotent branch untouched; no `AuditoriaCorreccion`.

### Files changed
| File | Action | What |
|---|---|---|
| `facturacion/SmartNet.Facturacion.Core/IUnidadDeTrabajo.cs` | Modified | +`ResolverHechosDeComposicionAsync`; `CrearAsientoBorradorAsync` signature → `(long, AsientoContable, ct)`; +`ReemplazarLineasAsync` |
| `facturacion/SmartNet.Facturacion.Core/ServicioDeFacturas.cs` | Modified | `AbrirAsync` seeds + persists the composed asiento |
| `facturacion/SmartNet.Facturacion.Infrastructure/SqlUnidadDeTrabajo.cs` | Modified | resolver impl (in-tx join + own-connection TC), widened `CrearAsientoBorradorAsync`, `ReemplazarLineasAsync`, private `InsertarLineasAsync` |
| `facturacion/SmartNet.Facturacion.Infrastructure/SmartNet.Facturacion.Infrastructure.csproj` | Modified | +`Catalogos.Core` ProjectReference |
| `facturacion/SmartNet.Facturacion.Core.Tests/FakeUnidadDeTrabajo.cs` | Modified | new members + recording props |
| `facturacion/SmartNet.Facturacion.Infrastructure.Tests/SqlUnidadDeTrabajoSembradoTests.cs` | Created | 6 real-schema tests |
| `facturacion/SmartNet.Facturacion.Infrastructure.Tests/SqlUnidadDeTrabajoFacturaTests.cs` | Modified | `CrearAsientoBorradorAsync_InsertsAHeaderRow_InBorrador` migrated to the new signature (compile fix) |
| `openspec/changes/cablear-composicion-asiento/tasks.md` | Modified | 2.1–2.5 + 3.2 marked `[x]` |

### TDD Cycle Evidence
| Task | RED | GREEN | REFACTOR |
|---|---|---|---|
| 2.1–2.3 / 3.2 | `dotnet build SmartNet.sln` → CS1501 (`CrearAsientoBorradorAsync` arity) + CS0535 (missing interface members) across `SqlUnidadDeTrabajo`/`FakeUnidadDeTrabajo` — the documented interface-break RED signal | build clean after impl | private `InsertarLineasAsync` extracted to share the line loop between create + replace |
| 2.1 (resolver) | `SqlUnidadDeTrabajoSembradoTests` referenced absent members → compile fail | `dotnet test …SqlUnidadDeTrabajoSembradoTests` → 6/6 | — |

### Work Unit Evidence
| Evidence | Value |
|---|---|
| Focused test command + result | `dotnet test facturacion/SmartNet.Facturacion.Infrastructure.Tests --filter "…SqlUnidadDeTrabajoSembradoTests|…SqlUnidadDeTrabajoFacturaTests|…SqlUnidadDeTrabajoAsientoLineasTests"` → **36/36 pass** (2m19s, real disposable SQL Server DB). Core: `dotnet test facturacion/SmartNet.Facturacion.Core.Tests --filter "…ServicioDeFacturasPhase2Tests|…SembradoDeAsientoTests|…ServicioDeFacturasTests"` → **53/53 pass**. |
| Runtime harness | Real versioned schema via `FacturacionTestDatabaseFixtureHelper.MigratedDatabaseAsync()` (creates `fact_test_<guid>`, seeds `dbo.Motivo`, runs migrations). Resolver + persistence exercised end to end against it. |
| Rollback boundary | Revert the 8 files above + the tasks.md checkboxes. `InvariantesDeConfirmacion.cs` / `ComposicionDeAsiento.cs` / `ProyeccionDeImportes.cs` / `PatchAsync` D4 / `CargarAsientoAsync` untouched. |
| Full build | `dotnet build SmartNet.sln` → 0 warnings, 0 errors. |

### Deviations from design
- `Catalogos.Core` was already transitively available to `Facturacion.Infrastructure` via
  `Contable.Core`; the explicit ProjectReference is redundant-but-harmless and kept for parity with
  the design's file-changes row.
- `CuentaSugerida` is resolved as `null` in `ResolverHechosDeComposicionAsync` with a
  `// TODO Phase 4` — this is the Batch-3 A3 decision (design offered the choice), landing the
  design-A2 placeholder path that `SembradoDeAsiento.Sembrar` already handles green.

### What Phase 3.1 / 3.3–3.7 + 4 + 5 still need (next batches)
- **3.1** RED `ServicioDeFacturasPhase2Tests` for `AbrirAsync` semantics (composes+persists on first
  create; second `abrir` no-op; foreign no-TC 409 before any write). The fake now records
  `UltimoAsientoBorradorCreado` + `Llamadas` for these assertions.
- **3.3/3.4** `ServicioDeAsientos.RecomponerAsync(asientoId, versionEsperada, string? cuentaCodigo, ct)`
  via `uow.ReemplazarLineasAsync` + ONE `REPARTO_MANUAL` audit row (`EntidadTipo=ASIENTO`,
  `Campo="Cargos"`, `SerializarLineas` before/after). No new `Accion` enum value. `SerializarLineas`
  is `private static` in `ServicioDeAsientos` today — hoist or replicate.
- **3.5** MIGRATE `InvariantesDeConfirmacionTests` (Contable.Core.Tests) — still not read; the real
  migration surface. Invert "empty asiento confirmable" cases; add PRINCIPAL/DESTINO/Global-1/2/5
  assertions off `SembradoDeAsiento.Sembrar` goldens.
- **3.6** `ServicioDeFacturasPhase2Tests` confirm fixtures → fresh valid single-cargo seed.
- **3.7** RED #19 reconciliation test — PATCH base/IGV unbalancing persisted cargos → `validar` 422
  `InvarianteContable.Principal` `"Los cargos 6x/1x suman {X}, se esperaba {N}"`.
- **4** `Program.cs` sugerencia DI; `POST /api/asientos/{id}/recomponer`; `ISembradorDeAsiento` port
  in `Inbox.Core` + adapter + `PromocionBackgroundService` call site; E2E goldens §10.1–§10.3.
- **5** SPA recomponer/generar-asiento buttons + descuadre marker.

## Remaining (Phases 3.1, 3.3–5) — NOT started

- **Phase 2** (Infra, needs SQL Server — cannot run boundary tests here): `ResolverHechosDeComposicionAsync` + change `CrearAsientoBorradorAsync` signature to `(long facturaId, AsientoContable asiento, ct)` + new `ReemplazarLineasAsync` under `TocarEncabezadoAsync` CAS; `SqlUnidadDeTrabajo` impl; add `Catalogos.Core` + `Sugerencia.Core` ProjectReferences to `Facturacion.Infrastructure`; extend `FakeUnidadDeTrabajo` (`HechosACargar` + record both new members). NO schema script (grants in 008). `CargarAsientoAsync` unchanged.
- **Phase 3** (Service + §7 de-vacuuming): `ServicioDeFacturas.AbrirAsync` → load → `ResolverHechosDeComposicionAsync` → `SembradoDeAsiento.Sembrar` → `Componer` → `CrearAsientoBorradorAsync` (seed writes NO audit); `ServicioDeAsientos.RecomponerAsync` via `ReemplazarLineasAsync` (ONE `REPARTO_MANUAL` audit, `EntidadTipo=ASIENTO`, `Campo="Cargos"`, no new `Accion` enum value); MIGRATE `InvariantesDeConfirmacionTests` + `ServicioDeFacturasPhase2Tests` vacuous fixtures to real `Sembrar` output; #19 reconciliation test (422 `InvarianteContable.Principal` `"Los cargos 6x/1x suman {X}, se esperaba {N}"`, `Derivar`/`PatchAsync` D4 unchanged). `InvariantesDeConfirmacion.cs` NOT modified.
- **Phase 4** (API): `Program.cs` register `ServicioDeSugerencia` + `ISugerenciaCuentaRepository` + `ResolverCandidatas` (compose-time only, no endpoint); `POST /api/asientos/{id}/recomponer` (`If-Match`, optional `{cuentaCodigo}` body, `RequireAuthorization`); NEW `ISembradorDeAsiento` port in `Inbox.Core` + adapter in `SmartNet.Api` → `AbrirAsync`, call site `PromocionBackgroundService.PromoverAsync` last stmt via `ResultadoPromocion.FacturaId` after tx commit; adapter SWALLOWS `Conflicto(SinTipoCambio)`/`NoEncontrado`; regression: ZERO new calls into #25 `ProcesarDocumentoAsociadoAsync` / #26 re-emit paths; E2E goldens §10.1/§10.2/§10.3 + PATCH-descuadre-recomponer.
- **Phase 5** (SPA, `npm test`): `asiento.service.ts` `recomponer(asientoId, cuentaCodigo?)` POST `If-Match` + `aplicar`; `detalle-page` "recomponer asiento" button (BORRADOR only) + keep "generar asiento" → `/abrir` when `asiento()` null; descuadre marker bound to EXISTING `cuadre()` computed (`cuadre.ts` unchanged, no new component); `factura-form` / `asiento-lineas` unchanged (add regression test only).
- **Phase 6** (doc): DONE in batch 2 — see the Batch 2 section above.

## Batch 5 — Phase 3 remaining (3.1, 3.3, 3.4, 3.5, 3.6, 3.7): DONE, all touched suites green

**Status**: done for the assigned slice. No production change outside `ServicioDeAsientos.RecomponerAsync`.
Build clean, `SmartNet.Facturacion.Core.Tests` 182/182, `SmartNet.Contable.Core.Tests` 51/51.

### Completed tasks
- [x] 3.4 `ServicioDeAsientos.RecomponerAsync(long asientoId, byte[] versionEsperada, string? cuentaCodigo, long usuarioId, DateTimeOffset ahora, ct)` — shape mirrors `ActualizarLineaAsync`: load asiento → `NoEncontrado` if null → `Conflicto(AsientoYaConfirmado)` if `Estado != Borrador` → `CargarFacturaAsync(persistido.FacturaId)` (`NoEncontrado` if null) → snapshot `antes` via `CargarLineasPersistidasAsync` → `ResolverHechosDeComposicionAsync(persistido.FacturaId)` → `SembradoDeAsiento.Sembrar(factura, hechos)` → `ReemplazarLineasAsync(asientoId, versionEsperada, asiento, ct)` → `ServicioDeFacturas.TraducirResultadoEscritura` (412 / NoEncontrado) → reused `RegistrarRepartoManualAsync` (ONE audit: `EntidadTipo=ASIENTO`, `Accion=REPARTO_MANUAL`, `Campo="Cargos"`, `SerializarLineas` before/after, `Motivo=null`) → commit → `Aplicado`. `cuentaCodigo` accepted on the signature, discarded with `_ = cuentaCodigo;` + `// TODO Phase 4` (always design-A2 placeholder path this batch). No new `AuditoriaCorreccion.Accion` enum value. `SerializarLineas` / `RegistrarRepartoManualAsync` reused unchanged (already `private static`, callable from the new method in-class).
- [x] 3.3 3 Fake-based tests in `ServicioDeAsientosTests.cs`: `RecomponerAsync_WhenAsientoIsBorrador_ReplacesLineas_WritesOneRepartoManualAudit_AndCommits`, `..._WhenAsientoIsConfirmado_ReturnsConflicto_AndNeverReplacesLineas`, `..._WhenVersionIsStale_ReturnsVersionEnConflicto_AndNeverCommits`.
- [x] 3.7 `ValidarAsync_WhenAManualSplitNoLongerSumsToTheMovedHeaderBase_ReturnsInvariantesIncumplidas_Principal_NotRemappedTo409` in `ServicioDeFacturasPhase2Tests.cs` — seeds a BORRADOR asiento whose header (`BasePEN 1000/IgvPEN 180`, moved by a #19 D4 PATCH) no longer matches the persisted manual split (cargos sum 100); `ValidarAsync` → `ResultadoComando.InvariantesIncumplidas` containing `InvarianteContable.Principal` with `Detalle == "Los cargos 6x/1x suman 100, se esperaba 1000."`; no commit. Production D4 / `ProyeccionDeImportes.Derivar` / `InvariantesDeConfirmacion.cs` untouched.
- [x] 3.1 + 3.5 De-vacuuming sentinels in `InvariantesDeConfirmacionTests.cs` (Contable.Core.Tests): `DeVacuuming_AsientoSinLineas_YaNoEsConfirmable_FallaPrincipal` (asserts exact message `"Los cargos 6x/1x suman 0, se esperaba 1000.00."`) + `DeVacuuming_AsientoConSoloElCreditoDelProveedor_FallaPrincipalYGlobal1`.
- [x] 3.6 Verified no-op — see the method-by-method note below.

### Files changed
| File | Action | What |
|---|---|---|
| `facturacion/SmartNet.Facturacion.Core/ServicioDeAsientos.cs` | Modified | +`RecomponerAsync` (~48 lines incl. doc comment) |
| `facturacion/SmartNet.Facturacion.Core.Tests/ServicioDeAsientosTests.cs` | Modified | +3 `RecomponerAsync` tests |
| `facturacion/SmartNet.Facturacion.Core.Tests/ServicioDeFacturasPhase2Tests.cs` | Modified | +1 §7-reconciliation test (3.7) |
| `contable/SmartNet.Contable.Core.Tests/InvariantesDeConfirmacionTests.cs` | Modified | +2 de-vacuuming sentinel tests |
| `openspec/changes/cablear-composicion-asiento/tasks.md` | Modified | 3.1, 3.3–3.7 marked `[x]` with notes |

### §7 de-vacuuming — method-by-method (design assumption vs reality)
Design F/tasks 3.5 said "invert the 'empty asiento is confirmable' cases" and "add positive cases built from `SembradoDeAsiento.Sembrar` output". Neither is literally applicable:
- `InvariantesDeConfirmacionTests.cs` has **no** vacuous fixture. `AsientoValido()` is a hand-built mirror of golden §10.1 (real 5 lines). Every existing method (`Global1..5`, `Principal_*`, `Destino_*`, `MultiFallo_*`) already asserts against real non-empty asientos. `InvarianteContable_NoCodificaLaPrecondicionViejaDeNC` only inspects the enum names, not confirmability. Nothing to invert.
- `SembradoDeAsiento` lives in `SmartNet.Facturacion.Core`; `Contable.Core.Tests` cannot reference it (module dependency direction is facturación → contable). Positive goldens off `Sembrar` are structurally impossible in this project — they belong in `Facturacion.Core.Tests` (`SembradoDeAsientoTests`, already 6 green from Batch 1) and the Phase 4 E2E.
- **Action taken**: added two explicit sentinels pinning that a no-PRINCIPAL / empty asiento is now rejected by §7 — the actual "de-vacuuming" contract, additive since nothing previously asserted the opposite.
- `ServicioDeFacturasPhase2Tests` (3.6): `ValidarPorFacturaAsync_ResolvesTheAsientoId_ThenRunsTheSameEngineAsValidarAsync`, `RegistrarAdjuntoAsync_AfterARealValidarAsync_...`, `PatchAsync_AfterARealValidarAsync_...` all seed a real balanced 3-line asiento (cargo `639915` = 100 == BasePEN, `401111` = 18 == IgvPEN, credit `421001` = 118) → pass §7 for real. `AsientoBorrador()` with `Array.Empty<LineaAsiento>()` feeds only PATCH-projection tests that never reach `validar`. No migration needed; whole suite green.

### TDD Cycle Evidence
| Task | RED | GREEN | REFACTOR |
|---|---|---|---|
| 3.4 `RecomponerAsync` | new method referenced by 3 tests did not exist → compile-absent (interface break signal, same convention as Batch 4) | `dotnet test …ServicioDeAsientosTests` → all green (56 across the two filtered classes) | reused `RegistrarRepartoManualAsync`/`SerializarLineas` verbatim — no new serializer |
| 3.7 | characterization test of UNCHANGED production D4/§7 path — no production RED (design D: "byte-for-byte unchanged") | green on first run; pins the 422 (not 409) mapping | — |
| 3.1/3.5 | characterization/regression of UNCHANGED `InvariantesDeConfirmacion.cs` — no production RED | green on first run | — |

### Work Unit Evidence
| Evidence | Value |
|---|---|
| Focused test command + result | `dotnet test facturacion/SmartNet.Facturacion.Core.Tests --no-build` → **182/182**; `dotnet test contable/SmartNet.Contable.Core.Tests --no-build` → **51/51** |
| Runtime harness | N/A — all Fake `IUnidadDeTrabajo` (ADR 0019 nivel 1/3). `RecomponerAsync` rides the already-tested Batch-4 `ReemplazarLineasAsync` SQL boundary; no new Infra surface this batch. |
| Rollback boundary | Revert `RecomponerAsync` from `ServicioDeAsientos.cs` + the 3 test additions + 2 Invariantes sentinels + 1 Phase2 test + the tasks.md checkboxes. `InvariantesDeConfirmacion.cs` / `ComposicionDeAsiento.cs` / `ProyeccionDeImportes.cs` / `PatchAsync` D4 / all Infra untouched. |
| Full build | `dotnet build SmartNet.sln` → 0 warnings, 0 errors. |

### What Phase 4 + 5 still need
- **4.1** `Program.cs`: register `ServicioDeSugerencia` + `ISugerenciaCuentaRepository` + `ResolverCandidatas` (compose-time only). Then wire `ResolverHechosDeComposicionAsync` in `SqlUnidadDeTrabajo` to return the suggested `CuentaContable` (replace the `// TODO Phase 4` null) + add `Sugerencia.Core` ProjectReference to `Facturacion.Infrastructure`.
- **4.2** `POST /api/asientos/{id}/recomponer` — `If-Match` required, optional body `{cuentaCodigo}`, `RequireAuthorization`, returns `AsientoRespuesta` + new ETag via `ResponderConAsientoActualizadoAsync` → `ServicioDeAsientos.RecomponerAsync(id, version, cuerpo?.CuentaCodigo, ResolverUsuarioId(http), tiempo.GetUtcNow(), ct)`. Add the route in `AsientoEndpoints.MapAsientoEndpoints`.
- **4.3/4.4** NEW `ISembradorDeAsiento` port in `Inbox.Core` + adapter in `SmartNet.Api` → `ServicioDeFacturas.AbrirAsync`; call site `PromocionBackgroundService.PromoverAsync` last stmt via `ResultadoPromocion.FacturaId` after tx commit; adapter SWALLOWS `Conflicto(SinTipoCambio)`/`NoEncontrado`; `Inbox.Infrastructure.Tests` for once-per-promoted + zero on Fusiona/Difiere/Descarta + #26 re-emit.
- **4.5/4.6** E2E `FacturaEndpointsTests` (SmartNet.Api.Tests): §10.1/§10.2 (set `Afectacion="INAFECTA"` on the boleta fixture — see Batch 1 carried-forward risk)/§10.3 goldens; PATCH base/IGV → `/validar` 422 → `/recomponer` → `/validar` 200; pre-#24 empty asiento can no longer confirm.
- **5** SPA: `asiento.service.ts` `recomponer(asientoId, cuentaCodigo?)`; `detalle-page` recomponer button (BORRADOR only) + keep generar-asiento → `/abrir`; descuadre marker on existing `cuadre()`; `factura-form`/`asiento-lineas` regression tests only.

### NON-GOALS to keep guarding
No NC/`HerenciaNotaCredito` wiring (`Herencia = null`). No percepción / `PercepcionOrig` column (`= 0`, §10.4 unreachable). No `.sql` schema script / no `checksums.txt`. No REGLAS rule change. `InvariantesDeConfirmacion.cs` / `ComposicionDeAsiento.Componer` / `ProyeccionDeImportes.Derivar` / `PatchAsync` D4 / `CargarAsientoAsync` byte-for-byte unchanged. No `sugerencia` endpoints or SPA suggestion UI. No new `AuditoriaCorreccion.Accion` value. No `BACKLOG.md` edit (owner-managed — record #24-closed note for the orchestrator when the cycle finishes).

## Batch 6 — Phase 4 partial (4.2, 4.3, 4.4) + pre-existing regression fix: DONE, tree compiles, all touched suites green

**Status**: partial. 4.2 (`recomponer` endpoint) + 4.3 (`ISembradorDeAsiento` promotion auto-seed) + 4.4 (Inbox regression tests) complete. **4.1 (sugerencia DI wiring) and 4.5/4.6 (E2E §10.1–§10.3 goldens + PATCH-descuadre-recomponer) NOT done — deferred to Batch 7.** Mode: Strict TDD where a runner exists (RED via interface-break compile fail / real-DB 500 → GREEN).

### Completed tasks
- [x] 4.2 `POST /api/asientos/{id}/recomponer` — see tasks.md 4.2 for the full description. `AsientoEndpoints.cs`: route in `MapAsientoEndpoints` + `RecomponerAsync` handler (`IfMatch.Requerido` → `ServicioDeAsientos.RecomponerAsync` → `ResponderConAsientoActualizadoAsync`, which already maps `Conflicto(AsientoYaConfirmado)`→409, version conflict→412, not found→404 via `ProblemasDeNegocio.Map`) + `internal sealed record RecomposicionAsientoRequest(string? CuentaCodigo)` (nullable param ⇒ empty body allowed, so "no If-Match → 428" is reached before body binding matters). `ServicioDeAsientos.RecomponerAsync` unchanged (Batch 5 already shipped the exact `(id, version, string? cuentaCodigo, usuarioId, ahora, ct)` signature the design/endpoint needs).
- [x] 4.3 `ISembradorDeAsiento` port + adapter + promotion call site — see tasks.md 4.3.
- [x] 4.4 `PromocionBackgroundServiceTests` — see tasks.md 4.4. Full `SmartNet.Inbox.Infrastructure.Tests` **75/75** (was 71/71).

### Files changed
| File | Action | What |
|---|---|---|
| `inbox/SmartNet.Inbox.Core/ISembradorDeAsiento.cs` | Created | port `Task SembrarAsync(long facturaId, ct)` |
| `inbox/SmartNet.Inbox.Infrastructure/PromocionBackgroundService.cs` | Modified | +ctor param `ISembradorDeAsiento`; `PromoverAsync` → `Task<long>` returning `resultado.FacturaId`; seed call in the `Promueve` branch after `PromoverAsync` |
| `api/SmartNet.Api/SembradorDeAsientoAdapter.cs` | Created | `ISembradorDeAsiento` adapter over `ServicioDeFacturas.AbrirAsync` — own DI scope per call, swallows `SinTipoCambio`/`NoEncontrado`/unexpected, logs, never throws |
| `api/SmartNet.Api/Program.cs` | Modified | `AddSingleton<ISembradorDeAsiento, SembradorDeAsientoAdapter>()` |
| `api/SmartNet.Api/AsientoEndpoints.cs` | Modified | `recomponer` route + handler + `RecomposicionAsientoRequest` |
| `inbox/SmartNet.Inbox.Infrastructure.Tests/PromocionBackgroundServiceTests.cs` | Modified | `+using SmartNet.Inbox.Core;` + nested `FakeSembradorDeAsiento` + `BuildSut` 4th arg + 4 tests |
| `api/SmartNet.Api.Tests/AsientoEndpointsTests.cs` | Modified | 5 `recomponer` E2E tests |
| `api/SmartNet.Api.Tests/FacturaTestDataHelper.cs` | Modified | **pre-existing regression fix** — `InsertarFacturaAsync` seeds coherent `IgvOrig = 18.00` by default (+`afectacion`/`totalOrig`/`igvOrig` params); un-broke 3 `FacturaEndpointsTests.Abrir_*` that were already RED on `main` (Batch-4 seed vs `CK_Linea_Tipo`, `Debe>0`) |
| `openspec/changes/cablear-composicion-asiento/{tasks.md,apply-progress.md}` | Modified | 4.2–4.4 `[x]`; this section |

### TDD Cycle Evidence
| Task | RED | GREEN | REFACTOR |
|---|---|---|---|
| 4.3/4.4 | `dotnet build` → CS7036 (`PromocionBackgroundService` 4-arg ctor) in the test project — documented interface-break RED | Inbox.Infrastructure.Tests **75/75** | `PromoverAsync` return type widened once; no other churn |
| 4.2 | first `Recomponer_OnABorradorAsiento` run → real-DB **500** `CK_Linea_Tipo` on a `401111 Debe=0` line (degenerate GRAVADA fixture) | fixture-coherence fix (`IgvOrig=18`) → `AsientoEndpointsTests` **26/26**, `recomponer` filter **5/5** | per-test `UPDATE fact.Factura` hack replaced by the shared helper default |

### Work Unit Evidence
| Evidence | Value |
|---|---|
| Focused test command + result | `dotnet test inbox/SmartNet.Inbox.Infrastructure.Tests --no-build` → **75/75**; `dotnet test api/SmartNet.Api.Tests --no-build` → **208/208** (full suite, real SQL Server + `WebApplicationFactory<Program>`) |
| Runtime harness | E2E via `SmartNetApiFactory` (real migrated `fact_test_<guid>`); promotion via `PromocionBackgroundService.ProcesarPendientesAsync` over real `SqlEventoInboxRepository`/`SqlPromocionRepository` + a fake `ISembradorDeAsiento` |
| Rollback boundary | Delete `ISembradorDeAsiento.cs` + `SembradorDeAsientoAdapter.cs`; revert the `PromocionBackgroundService` ctor/`PromoverAsync`, the `Program.cs` line, the `AsientoEndpoints` route+handler+record, and the 3 test files + `FacturaTestDataHelper`. `ServicioDeAsientos.RecomponerAsync` / `ComposicionDeAsiento` / `InvariantesDeConfirmacion` / `ProyeccionDeImportes` / D4 all untouched. |
| Full build | `dotnet build SmartNet.sln` → 0 warnings, 0 errors |

### Deviations from design
- **`SqlFacturacionStore` / `SqlUnidadDeTrabajo` NOT touched this batch** — 4.1 (thread `ServicioDeSugerencia` into `ResolverHechosDeComposicionAsync`) deferred. The `// TODO Phase 4` null still stands; `CuentaSugerida` remains null ⇒ design-A2 placeholder path.
- Adapter resolves `ServicioDeFacturas` via `IServiceScopeFactory.CreateAsyncScope()` (design said "adapter in SmartNet.Api delegating to AbrirAsync" without specifying lifetime plumbing; `ServicioDeFacturas` is AddScoped and the hosted service is a singleton, so a per-call scope is required).
- `recomponer` optional body via a **nullable record parameter** rather than `[FromBody(EmptyBodyBehavior=Allow)]` — minimal-API nullable complex params already allow an empty body, one less attribute.

### What Phase 4 (4.1, 4.5, 4.6) + Phase 5 still need — hand-off for Batch 7
- **4.1** `Program.cs`: `ServicioDeSugerencia` needs `ISugerenciaCuentaRepository` + `IMotivoRepository` + `IMotivoAtributoRepository` + `ICuentaContableRepository` (last one already registered). SQL adapters: `SqlSugerenciaCuentaRepository`, `SqlMotivoRepository`, `SqlMotivoAtributoRepository` (all in `SmartNet.Catalogos.Infrastructure`, ctor = connection string). Register `ServicioDeSugerencia` as a plain instance/singleton (no per-request state). Then thread it: `SqlFacturacionStore` gets a 3rd optional ctor arg `ServicioDeSugerencia?` → passes to `SqlUnidadDeTrabajo` → `ResolverHechosDeComposicionAsync` calls `servicioDeSugerencia.SugerirParaFacturaAsync(proveedorCodigo, motivoInt, ct)` and resolves `CuentaSugerida` = `resultado.CandidatasVigentes.FirstOrDefault(c => c.Cuenta == resultado.Cuenta?.CuentaCodigo)` (null when no suggestion → placeholder path stays). Current query already selects `f.Moneda`, `f.FechaEmision`, `f.Motivo` (int) via the `dbo.Motivo` join — add `f.ProveedorCodigo` + the raw `f.Motivo` int to the SELECT. Add `Sugerencia.Core` ProjectReference to `SmartNet.Facturacion.Infrastructure.csproj`. Keep the 2-arg `SqlFacturacionStore` ctor (≈20 call sites, incl. the infra test suites) so `ServicioDeSugerencia?` stays optional/null-safe there.
- **4.5** E2E §10.1–§10.3 goldens: the abrir→seed→`GET /asiento`→`validar` path is now healthy (Batch 6 fixture fix). To reach a suggested account (so `validar` → CONFIRMADO instead of the placeholder block) you need, in `SmartNetApiFactory`'s DB: a `dbo.Motivo` row with prefijos, `dbo.CuentaContable` hoja rows matching those prefijos, and `fact.Factura.Motivo` pointing at that motivo. `DboCatalogSeedHelper` (in `SmartNet.Catalogos.Infrastructure.Tests`, internal) is the pattern — replicate a small seeder in `SmartNet.Api.Tests`. §10.3 also needs `Db.InsertarTipoCambioAsync(fecha, venta: 3.7895m)` for the emisión date. Assert `AsientoRespuesta.BasePEN/IgvPEN/NetoPEN` + líneas against the REGLAS §10 numbers.
- **4.6** PATCH base/IGV → `/validar` 422 `"Los cargos 6x/1x suman {X}, se esperaba {N}"` → `POST /recomponer` → `/validar` 200. Does NOT need plan-contable seeding if it starts from `InsertarAsientoBorradorBalanceadoAsync` (real 3-line balanced asiento), PATCHes a scalar to unbalance, then recomposes. `recomponer` after that lands the placeholder path though — so to get to CONFIRMADO the test needs a suggested account too, OR assert only the 422→(recompose)→still-blocked-by-Global-2 sequence. Coordinate with 4.5's seeding.
- **Phase 5 (SPA, `npm test`, `SmartNet/SmartNetWeb/**`)**: `asiento.service.ts` `recomponer(asientoId, cuentaCodigo?)` = POST `/api/asientos/{id}/recomponer` with `If-Match` + `aplicar(respuesta)` (response shape == `PATCH /lineas`, ETag in header). `detalle-page` "recomponer asiento" button (BORRADOR only, confirmation dialog) + keep "generar asiento" → `/abrir` when `asiento()` null + descuadre marker bound to the EXISTING `cuadre()` computed (`cuadre.ts` UNCHANGED, no new component). `factura-form`/`asiento-lineas` UNCHANGED — regression test only that base/IGV populate from the seeded asiento.

### Carried-forward risk (flag for verify)
- **GRAVADA factura with no `IgvOrig`** composes a phantom `401111 Debe=0` line that `CK_Linea_Tipo` rejects at persist time (`abrir`/`recomponer` → 500). Batch 6 made the Api.Tests fixture coherent; production has no such guard. Either `ConstruccionDeFactura`/promotion always sets `IgvOrig` for GRAVADA (needs confirming) or `SembradoDeAsiento`/`Componer` should skip a zero IGV line — the latter touches `Componer` (NON-GOAL). Owner decision needed; unchanged by this cycle.
  - **RESOLVED in Batch 7** via the seed-level guard below (production-safe now; the owner decision on whether promotion should reject an incoherent GRAVADA-without-IGV factura at ingest is still open but no longer causes a 500).

## Batch 7 — Zero-line seed guard + Phase 4.1 (sugerencia DI): DONE, tree compiles, all touched suites green

**Status**: partial. The guard + task 4.1 (sugerencia DI wiring) are complete and verified. **Tasks 4.5 / 4.6 (E2E §10.1–§10.3 goldens + PATCH-descuadre-recomponer) and the `RecomponerAsync` `cuentaCodigo` override remain NOT DONE** — deferred to Batch 8. Mode: Strict TDD (RED via real-DB `CK_Linea_Tipo` 500 reproduction / interface-shape → GREEN).

### Completed this batch
- **Zero-amount line guard** — `SembradoDeAsiento.Sembrar` (pure Core, `SmartNet.Facturacion.Core`): after `ComposicionDeAsiento.Componer` returns, drop any `LineaAsiento` where `Debe == 0m && Haber == 0m`, then (placeholder appended if no suggested account) renumber `Orden` 1..n. `ComposicionDeAsiento.Componer` byte-for-byte unchanged. Rationale (recorded): a zero line carries no accounting content and violates the persisted shape (`CK_Linea_Tipo`: `Tipo='D' AND Debe > 0`); §7's PRINCIPAL invariant still catches the resulting imbalance / missing-401111 and blocks `validar` (Option 3 — the seed is best-effort, §7 is the gate).
- [x] 4.1 sugerencia DI (see tasks.md 4.1 for the full description).

### TDD Cycle Evidence
| Task | RED | GREEN | REFACTOR |
|---|---|---|---|
| guard | new `Sembrar_GravadaConIgvCero_*` tests assert no `401111`/no zero line + §7 blocks → fail against the pre-guard `Sembrar` (zero `401111` line present) | `SembradoDeAsientoTests` **8/8** (was 6) | placeholder-append + renumber folded into one `Select((l,i) => l with { Orden = i+1 })` pass |
| 4.1 | `Program.cs` factory referenced `ServicioDeSugerencia` (unregistered) + `SqlFacturacionStore` 2-arg only → `dotnet build` CS-error | `dotnet build SmartNet.sln` 0/0; `FacturaEndpointsTests` 32/32 (DI graph resolves, abrir still healthy) | 3rd ctor arg optional ⇒ zero infra-test churn |

### Files changed
| File | Action | What |
|---|---|---|
| `facturacion/SmartNet.Facturacion.Core/SembradoDeAsiento.cs` | Modified | `Sembrar` drops `Debe==0 && Haber==0` lines + renumbers `Orden` 1..n; `Componer` untouched |
| `facturacion/SmartNet.Facturacion.Core.Tests/SembradoDeAsientoTests.cs` | Modified | +2 guard tests (GRAVADA `IgvOrig=0`: with suggested account → no `401111`, balances, §7 blocks; no suggestion → placeholder, §7 blocks) |
| `facturacion/SmartNet.Facturacion.Infrastructure/SmartNet.Facturacion.Infrastructure.csproj` | Modified | +`Sugerencia.Core` ProjectReference |
| `facturacion/SmartNet.Facturacion.Infrastructure/SqlFacturacionStore.cs` | Modified | 3rd optional ctor arg `ServicioDeSugerencia?`; threaded to `SqlUnidadDeTrabajo` |
| `facturacion/SmartNet.Facturacion.Infrastructure/SqlUnidadDeTrabajo.cs` | Modified | ctor optional `ServicioDeSugerencia?`; `ResolverHechosDeComposicionAsync` SELECT adds `f.ProveedorCodigo`/`f.Motivo`, resolves `CuentaSugerida` via `SugerirParaFacturaAsync` (null-safe when service absent) |
| `api/SmartNet.Api/Program.cs` | Modified | register `ISugerenciaCuentaRepository`/`IMotivoRepository`/`IMotivoAtributoRepository`/`ServicioDeSugerencia` (`AddSingleton`); pass `ServicioDeSugerencia` to `SqlFacturacionStore` factory |
| `openspec/changes/cablear-composicion-asiento/{tasks.md,apply-progress.md}` | Modified | 4.1 `[x]`, guard + Batch-7 notes |

### Work Unit Evidence
| Evidence | Value |
|---|---|
| Focused test command + result | `dotnet test facturacion/SmartNet.Facturacion.Core.Tests --filter SembradoDeAsientoTests` → **8/8**; `dotnet test facturacion/SmartNet.Facturacion.Infrastructure.Tests --filter SqlUnidadDeTrabajoSembradoTests` → **6/6**; `dotnet test api/SmartNet.Api.Tests --filter FacturaEndpointsTests` → **32/32** |
| Full suites | `SmartNet.Facturacion.Core.Tests` **184/184** · `SmartNet.Contable.Core.Tests` **51/51** · `SmartNet.Facturacion.Infrastructure.Tests` **71/71** · `SmartNet.Api.Tests` **208/208** (all real SQL Server + `WebApplicationFactory<Program>`). `SmartNet.Inbox.Infrastructure.Tests` not re-run — no inbox change this batch (75/75 from Batch 6). |
| Runtime harness | E2E `SmartNetApiFactory` boots with the new DI graph (sugerencia registered) — `FacturaEndpointsTests` exercises `abrir`→seed→`validar`; no `dbo.Motivo`/`dbo.CuentaContable` seeded there yet so suggestion resolves null → design-A2 placeholder path (no regression). |
| Rollback boundary | Revert the 6 code files + the tasks.md/apply-progress.md notes. `ComposicionDeAsiento` / `InvariantesDeConfirmacion` / `ProyeccionDeImportes` / `PatchAsync` D4 / `CargarAsientoAsync` / all Inbox untouched. `SqlFacturacionStore`'s 1-/2-arg ctors preserved. |
| Full build | `dotnet build SmartNet.sln` → 0 warnings, 0 errors |

### Deviations from design
- The `cuentaCodigo` override in `ServicioDeAsientos.RecomponerAsync` (design C1 — "the optional cuentaCodigo closes the A2 loop") is NOT wired. The parameter is still accepted and discarded. Wiring it needs a new read member on `IUnidadDeTrabajo` (`ObtenerCuentaContableAsync` or similar) + `FakeUnidadDeTrabajo` + a `hechos with { CuentaSugerida = ... }` override in `RecomponerAsync`. Left for Batch 8 alongside 4.5/4.6.
- Guard renumbers `Orden` 1..n on every `Sembrar` (design A2 only spoke of appending the placeholder at `max+1`). `Orden` is presentation-only per ADR 0006, so contiguous 1..n after a drop is strictly cleaner and keeps the persisted shape tidy. Existing golden/placeholder tests are `Orden`-agnostic except the placeholder-is-last assertion, which still holds.

### What Batch 8 needs (hand-off)
- **`cuentaCodigo` override**: `IUnidadDeTrabajo.ObtenerCuentaContableAsync(string cuenta, ct) → Task<CuentaContable?>` (in-tx `SELECT cuenta, descripcion, nivel, ctarefleja, ctapuente FROM dbo.CuentaContable WHERE cuenta=@c` — grant in 008) + Fake + in `RecomponerAsync` when `cuentaCodigo is not null` resolve it and `hechos = hechos with { CuentaSugerida = cuenta }` before `SembradoDeAsiento.Sembrar`.
- **4.5 / 4.6 E2E** (`SmartNet.Api.Tests`): build a `CatalogoTestDataHelper` (mirror `DboCatalogSeedHelper` from `SmartNet.Catalogos.Infrastructure.Tests`, which is `internal`): seed `dbo.Motivo`(codigo + prefijos in `cuenta`), `dbo.CuentaContable` hoja rows (the §10 accounts: 631111/946311/791111 for §10.1; 656111 for §10.2; 601111/431212 + 401111/421211 for §10.3), `fact.SugerenciaCuenta` (pin the cascade to the wanted account), `fact.TipoCambio` venta 3.7895 for §10.3. Then: §10.1 gravada soles + destino; §10.2 boleta `Afectacion="INAFECTA"` IGV al costo (no 401111); §10.3 dólares. For each: `POST /abrir` → `GET` asiento → assert `BasePEN/IgvPEN/NetoPEN` + líneas match REGLAS §10.x exactly → `POST /validar` → 200 CONFIRMADO + correlativo. PATCH-descuadre: open+seed gravada, `PATCH /api/facturas/{id}` base/IGV so lines ≠ header → `POST /validar` 422 with `"Los cargos 6x/1x suman"` + `InvarianteContable.Principal` → `POST /api/asientos/{id}/recomponer` (If-Match) 200 → `POST /validar` 200 CONFIRMADO.
- **Phase 5 SPA** (`SmartNet/SmartNetWeb/**`, `npm test`) — untouched: `asiento.service.ts` `recomponer(asientoId, cuentaCodigo?)` POST `/api/asientos/{id}/recomponer` + `If-Match` + `aplicar(respuesta)`; `detalle-page` "recomponer asiento" button (BORRADOR only, confirmation dialog) + keep "generar asiento" → `/abrir` when `asiento()` null + descuadre marker bound to the EXISTING `cuadre()` computed (`cuadre.ts` UNCHANGED); `factura-form`/`asiento-lineas` UNCHANGED (regression test only).

## Batch 8 — Phase 4 complete (`cuentaCodigo` override + 4.5 + 4.6): DONE, build 0/0, all touched suites green

**Status**: done for the assigned slice. Phase 4 (API) is now fully complete. Only Phase 5 (SPA) remains.
Mode: Strict TDD (RED via real-DB 422 / invariant-name mismatch → GREEN).

**Recovery note**: a prior Batch-8 attempt died on an API rate limit but HAD already written the
`cuentaCodigo` override end to end (a previous batch summary said "nothing to recover" — that was
wrong; the working tree carried +70 `ServicioDeAsientos.cs`, +197 `SqlUnidadDeTrabajo.cs`, +96
`ServicioDeAsientosTests.cs`, +35 `IUnidadDeTrabajo.cs`, +34 `FakeUnidadDeTrabajo.cs`, +88
`AsientoEndpointsTests.cs`, +29 `AsientoEndpoints.cs`). This batch verified that code compiles +
passes (ServicioDeAsientosTests 20/20, SqlUnidadDeTrabajoSembradoTests 6/6) and then wrote 4.5/4.6.

### Completed this batch
- **`cuentaCodigo` override (design C1)** — verified already-present + green:
  - `IUnidadDeTrabajo.ObtenerCuentaContableAsync(string cuentaCodigo, ct) → Task<CuentaContable?>`
  - `SqlUnidadDeTrabajo`: in-tx `SELECT cuenta, descripcion, nivel, ctarefleja, ctapuente FROM dbo.CuentaContable WHERE cuenta=@cuenta` (grant in 008), maps to `CuentaContable`, null when absent.
  - `FakeUnidadDeTrabajo`: `CuentasContables` dict (`StringComparer.Ordinal`), empty ⇒ null.
  - `ServicioDeAsientos.RecomponerAsync`: `cuentaCodigo is not null` → `ObtenerCuentaContableAsync` → null ⇒ `ResultadoComando.CorreccionInvalida` (maps to 422 via `ProblemasDeNegocio`); resolved ⇒ `hechos = hechos with { CuentaSugerida = cuenta }` before `SembradoDeAsiento.Sembrar`. No new `CasoConflicto` value — `CorreccionInvalida` already exists and is the exact "body datum is malformed, not a business rule" 422.
  - Tests: `SqlUnidadDeTrabajoSembradoTests.ObtenerCuentaContableAsync_ReadsCuentaWithItsReflejoAndPuente_OrNullWhenAbsent`; `ServicioDeAsientosTests.RecomponerAsync_WithAnExplicitCuentaCodigo_SeedsThatAccountOnTheCargoLine_NoPlaceholder` + `_WithAnUnknownCuentaCodigo_ReturnsCorreccionInvalida_AndNeverReplacesLineas`.
- [x] 4.5 — `CatalogoTestDataHelper` (new, `SmartNet.Api.Tests`) + 3 REGLAS §10 E2E goldens in `FacturaEndpointsTests`. See tasks.md 4.5.
- [x] 4.6 — `PatchBaseIgv_UnbalancingASeededSplit_BlocksValidar_UntilRecomponer` E2E in `FacturaEndpointsTests`. See tasks.md 4.6.

### Files changed
| File | Action | What |
|---|---|---|
| `api/SmartNet.Api.Tests/CatalogoTestDataHelper.cs` | Created | `SeedMotivoAsync` / `SeedCuentaContableAsync` (nivel NULL ⇒ hoja; optional ctarefleja/ctapuente) / `SeedProveedorRelacionadoAsync` (`fact.ProveedorAtributo` EsRelacionada=1) / `AsignarMotivoAsync` (`UPDATE fact.Factura SET Motivo`) |
| `api/SmartNet.Api.Tests/FacturaEndpointsTests.cs` | Modified | +4 tests (`Reglas_10_1/10_2/10_3`, `PatchBaseIgv_UnbalancingASeededSplit_BlocksValidar_UntilRecomponer`) + `Linea()` / `AbrirYLeerAsientoAsync()` helpers + §10.4 deferral comment |
| `openspec/changes/cablear-composicion-asiento/{tasks.md,apply-progress.md}` | Modified | 4.5/4.6 `[x]`, this section |

Production code for the `cuentaCodigo` override (`ServicioDeAsientos.cs`, `SqlUnidadDeTrabajo.cs`,
`IUnidadDeTrabajo.cs`, `AsientoEndpoints.cs` + the Fake/tests) was authored by the prior dead
attempt; this batch left it byte-for-byte and only verified it.

### TDD Cycle Evidence
| Task | RED | GREEN | REFACTOR |
|---|---|---|---|
| `cuentaCodigo` override | (prior attempt) interface-break compile fail + Fake tests referencing absent members | `ServicioDeAsientosTests` 20/20, `SqlUnidadDeTrabajoSembradoTests` 6/6 | none |
| 4.5 | new `Reglas_10_*` tests fail to compile without `CatalogoTestDataHelper`; then real-DB run | `Reglas_10_1/10_2/10_3` green first real run (cascade Tier-3 resolves the single seeded candidate) | `AbrirYLeerAsientoAsync` extracted to share abrir+GET across the 3 goldens |
| 4.6 | first run: `/validar` 422 body assert on `"Principal"` FAILED (invariant serializes as `bloque-principal-invalido`) | assert corrected to the real problem `type` slug → green | — |

### Work Unit Evidence
| Evidence | Value |
|---|---|
| Focused test command + result | `dotnet test api/SmartNet.Api.Tests --no-build --filter "Reglas_10_1|Reglas_10_2|Reglas_10_3|PatchBaseIgv_UnbalancingASeededSplit"` → **4/4** |
| Full suites | `SmartNet.Api.Tests` **212/212** (was 208) · `SmartNet.Facturacion.Core.Tests` **186/186** (was 184) · `SmartNet.Facturacion.Infrastructure.Tests` **72/72** (was 71). All real SQL Server + `WebApplicationFactory<Program>`. `Contable.Core.Tests` (51/51) / `Inbox.Infrastructure.Tests` (75/75) unchanged — not re-run (no touch this batch). |
| Runtime harness | E2E `SmartNetApiFactory` (real migrated `fact_test_<guid>`): abrir→sugerencia cascade (`SqlSugerenciaCuentaRepository`/`SqlMotivoRepository` over the seeded `dbo.Motivo`/`dbo.CuentaContable`)→`SembradoDeAsiento.Sembrar`→`ComposicionDeAsiento.Componer`→persist→`GET /asiento`→`/validar`→`InvariantesDeConfirmacion.Evaluar`→correlativo. 4.6 also exercises #19 D4 PATCH + `POST /api/asientos/{id}/recomponer`. |
| Rollback boundary | Delete `CatalogoTestDataHelper.cs`; revert the 4 tests + helpers in `FacturaEndpointsTests.cs` + the tasks/apply-progress notes. The `cuentaCodigo` override production code is a separate rollback unit (prior attempt). `ComposicionDeAsiento` / `InvariantesDeConfirmacion` / `ProyeccionDeImportes` / `PatchAsync` D4 / `CargarAsientoAsync` untouched. |
| Full build | `dotnet build SmartNet.sln` → 0 warnings, 0 errors |

### Deviations from design
- 4.5 goldens rely on the sugerencia cascade's **Tier-3 "primera candidata"** path (no `fact.SugerenciaCuenta` usage rows seeded): each motivo's `cuenta` prefix is seeded to match exactly one `dbo.CuentaContable` hoja, so `ResolucionDePrefijos.ResolverCandidatas` returns a single account and `CascadaDeSugerencia.SugerirCuenta` picks it deterministically. Simpler and more robust than seeding `fact.SugerenciaCuenta` history rows (the Batch-7 hand-off suggestion) — same resolved account, fewer moving parts.
- §10.1 provider account asserted as `421211` (REGLAS §10.1 verbatim), §10.3 as `431212` — `CuentaDeProveedor.Codigo` confirmed. Batch-7 hand-off mentioned `421211` for §10.3 which was a typo.
- 4.6 uses `BaseImponible: 600 / Igv: 108` (the #19 D4 atomic pair) rather than `TotalOrig` to move the header — `PatchAsync` D4 re-derives from `TotalOrig`/`IgvOrig`, and the pair sets both (`TotalOrig = 708`, `IgvOrig = 108`). `PatchAsync` D4 / `ProyeccionDeImportes.Derivar` byte-for-byte unchanged.
- §10.4 (percepción) has a one-line deferral comment only — declared non-goal (no `fact.Factura.PercepcionOrig` column).

### What Phase 5 (SPA) needs — hand-off (`SmartNet/SmartNetWeb/**`, `npm test`, Vitest)
- **5.1** `detalle/data-access/asiento.service.ts`: `recomponer(asientoId: number, cuentaCodigo?: string)` → `POST /api/asientos/{asientoId}/recomponer`, body `{ cuentaCodigo }` (omit when undefined), `If-Match` header from the current asiento ETag, then `aplicar(respuesta)` — identical ETag-threading to the existing `actualizarLinea`. `asiento.service.spec.ts` with `HttpTestingController`: asserts the `If-Match` header goes out and the new ETag is stored.
- **5.2** `detalle/feature/detalle-page.ts` / `.html`: "Recomponer asiento" button, visible only when `asiento()?.estado === 'BORRADOR'` (hidden on CONFIRMADO), confirmation dialog warning that manual line edits are replaced → calls `asientoService.recomponer(...)` then refetches. `detalle-page.spec.ts`: button hidden on CONFIRMADO / visible on BORRADOR.
- **5.3** keep the existing "generar asiento" affordance calling `POST /api/facturas/{id}/abrir` when `asiento()` is null (foreign-currency-no-TC case — promotion auto-seed swallowed the `SinTipoCambio`).
- **5.4** descuadre marker: bind a read-only marker to the EXISTING `cuadre()` computed in `detalle-page.ts` (sum of PRINCIPAL cargos vs header `basePEN`/`netoPEN`). `cuadre.ts` UNCHANGED, NO new component.
- **5.5** `factura-form` / `asiento-lineas` need NO code change (already read `asiento()?.basePEN` / `igvPEN`; they simply stop being null). Add a regression spec asserting base/IGV populate from a seeded asiento.
- API endpoint contract for the SPA: `POST /api/asientos/{id}/recomponer` — `If-Match` required (428 without), optional `{ "cuentaCodigo": "..." }` body, returns `AsientoRespuesta` + new `ETag` header; 409 on CONFIRMADO, 412 stale If-Match, 422 (`CorreccionInvalida`) unknown `cuentaCodigo`, 404 unknown asiento.

### NON-GOALS still honoured
No SPA this batch. No `.sql` / `checksums.txt` (helper seeds via INSERT). No REGLAS rule change.
`InvariantesDeConfirmacion.cs` / `ComposicionDeAsiento.cs` / `ProyeccionDeImportes.cs` / `PatchAsync`
D4 / `CargarAsientoAsync` byte-for-byte unchanged. No NC / `HerenciaNotaCredito`. No percepción. No
new `AuditoriaCorreccion.Accion` value. No `BACKLOG.md` edit.
