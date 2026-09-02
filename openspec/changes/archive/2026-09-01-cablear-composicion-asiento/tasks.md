# Tasks: Wire ComposicionDeAsiento into the productive asiento lifecycle (BACKLOG #24)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~1,750–1,950 (authored) |
| 400-line budget risk | High |
| Chained PRs recommended | No (owner rejected 3-PR chain, obs 309) |
| Suggested split | Single PR with `size:exception` |
| Delivery strategy | single-pr |
| Chain strategy | size-exception |

Decision needed before apply: No
Chained PRs recommended: No
Chain strategy: size-exception
400-line budget risk: High

`size:exception` is ALREADY approved by the owner (Engram obs 309, decision 5). No further stop before apply. One worktree (`item-24-cablear-composicion-asiento`), one PR.

### Suggested Work Units (internal commits, not separate PRs)

| Unit | Goal | Focused test command | Runtime harness | Rollback boundary |
|------|------|----------------------|-----------------|-------------------|
| 1 | Pure Core seed (`HechosDeComposicion` + `SembradoDeAsiento`) | `dotnet test --filter SembradoDeAsientoTests` | N/A — pure ADR 0019 nivel 1 | new files in `SmartNet.Facturacion.Core` |
| 2 | Infra: resolver + persist/replace lines | `dotnet test --filter SqlUnidadDeTrabajoSembradoTests` | real versioned schema | new uow members + adapter |
| 3 | `AbrirAsync` composes + §7 de-vacuuming | `dotnet test --filter "ServicioDeFacturasPhase2Tests|InvariantesDeConfirmacionTests"` | N/A — fake uow | `ServicioDeFacturas`/`ServicioDeAsientos` |
| 4 | Sugerencia DI + `recomponer` endpoint + promotion auto-seed | `dotnet test --filter "FacturaEndpointsTests|RecomponerTests"` | `dotnet test SmartNet.Api.Tests` E2E | `Program.cs` + `ISembradorDeAsiento` adapter |
| 5 | SPA recomponer + generar-asiento + descuadre marker | `npm test` | Angular Vitest | `asiento.service.ts`, `detalle-page` |

## Phase 1: Pure Core seed (SmartNet.Facturacion.Core — ADR 0019 nivel 1)

- [x] 1.1 RED `SembradoDeAsientoTests` (Facturacion.Core.Tests): field map from `FacturaPersistida` (ProveedorCodigo/Moneda/FechaContable=FechaEmision/Comprobante/Afectacion; `IgvOrig ?? 0`; `BaseOrig = TotalOrig - IgvOrig`; `PercepcionOrig = 0`; `Herencia = null`). Sat: nucleo-contable ADDED "Componer produce la semilla PRINCIPAL + DESTINO".
- [x] 1.2 RED cases: §10.1 gravada soles + destino, §10.2 boleta IGV al costo, §10.3 dólares (TC venta 3.7895 → 3789.50/682.11/4471.61) — goldens from REGLAS §10.
- [x] 1.3 RED placeholder case (design A2): `CuentaSugerida = null` → Componer with ZERO cargos + append one line PRINCIPAL/Debe=importePEN/`CuentaCodigo=null`/`SinCuenta=1`/Orden=max+1. Assert Global-1 balances, Global-2 + PRINCIPAL fail.
- [x] 1.4 GREEN: add `HechosDeComposicion(bool EsRelacionada, string? MotivoDescripcion, TipoCambioCongelado? TipoCambio, CuentaContable? CuentaSugerida)` + `SembradoDeAsiento.Construir/.Sembrar` static. Default cargo `ImportePEN = ProyeccionDeImportes.Derivar(...).BasePEN` (same pure fn #19/D4 uses). `ComposicionDeAsiento.Componer` UNTOUCHED.
- [x] 1.5 Confirm `PurityScanTests` still passes (builder must not read catálogo).

## Phase 2: Infrastructure — resolve facts + persist (Facturacion.Infrastructure)

- [x] 2.1 RED `SqlUnidadDeTrabajoSembradoTests` (Facturacion.Infrastructure.Tests, real schema): `ResolverHechosDeComposicionAsync` reads `fact.ProveedorAtributo` (EsRelacionada) + `dbo.Motivo` (descripción) inside tx via `CrearComando`.
- [x] 2.2 RED: `CrearAsientoBorradorAsync(long facturaId, AsientoContable, ct)` persists header (7 cols) + N line INSERTs reusing `AgregarParametrosDeLinea`; returns new id. NOT via `AgregarLineaAsync`.
- [x] 2.3 RED: `ReemplazarLineasAsync(asientoContableId, versionEsperada, AsientoContable, ct)` under `TocarEncabezadoAsync` CAS → DELETE detalle → UPDATE header scalars → re-INSERT; stale ETag → `VersionEnConflicto`; BORRADOR-only guard in Core.
- [x] 2.4 GREEN: add the 3 members to `IUnidadDeTrabajo` + `SqlUnidadDeTrabajo` impl. Add `ProjectReference` Catalogos.Core to Facturacion.Infrastructure (PR5 TiposCambio pattern; Sugerencia.Core deferred to Phase 4 per Batch-3 A3 ruling). `CargarAsientoAsync` UNCHANGED. NO schema script / checksum (all grants in `008`).
- [x] 2.5 Extend `FakeUnidadDeTrabajo` with `HechosACargar` + records `CrearAsientoBorradorAsync` / `ReemplazarLineasAsync`.

## Phase 3: Service orchestration + §7 de-vacuuming (Facturacion.Core / .Application)

- [x] 3.1 De-vacuuming sentinels added in `InvariantesDeConfirmacionTests` (Contable.Core.Tests): empty-lines asiento and credit-only asiento now proven to fail §7 PRINCIPAL/Global-1. (`AbrirAsync` compose+persist behaviour is already covered by `AbrirAsync_WhenNoAsientoVigenteExists_CreatesOneAndCommits` / idempotency / foreign-currency 409 tests from Batch 4; `SembradoDeAsiento` cannot be referenced from Contable.Core.Tests — dependency direction.)
- [x] 3.2 GREEN `ServicioDeFacturas.AbrirAsync`: load factura → `ResolverHechosDeComposicionAsync` → `SembradoDeAsiento.Sembrar` → `Componer` → `CrearAsientoBorradorAsync`. Seed writes NO `AuditoriaCorreccion` (design B3).
- [x] 3.3 `RecomponerAsync` tests added to `ServicioDeAsientosTests` (Fake-based): borrador → replaces líneas + writes 1 `REPARTO_MANUAL` audit (EntidadTipo=ASIENTO, Campo="Cargos") + commits; confirmado → `Conflicto(AsientoYaConfirmado)`, never replaces; stale version → `VersionEnConflicto`, no commit.
- [x] 3.4 GREEN `ServicioDeAsientos.RecomponerAsync(asientoId, versionEsperada, string? cuentaCodigo, ct)` via `ReemplazarLineasAsync` + `SembradoDeAsiento.Sembrar` + reused `RegistrarRepartoManualAsync`. `cuentaCodigo` plumbed on the signature; resolution to a `CuentaContable` deferred to Phase 4 (always the design-A2 placeholder path this batch).
- [x] 3.5 De-vacuuming assertions added to `InvariantesDeConfirmacionTests`: `DeVacuuming_AsientoSinLineas_YaNoEsConfirmable_FallaPrincipal` (exact descuadre message) + `DeVacuuming_AsientoConSoloElCreditoDelProveedor_FallaPrincipalYGlobal1`. `InvariantesDeConfirmacion.cs` NOT modified. NOTE: the pre-existing fixtures in this file were already hand-built real 5-line asientos (mirror of golden §10.1), never vacuous — nothing to "invert"; and `SembradoDeAsiento` is unreferenceable here (Facturacion → Contable dependency direction).
- [x] 3.6 No-op — verified `ServicioDeFacturasPhase2Tests` has no vacuous confirm fixture: `ValidarPorFacturaAsync_ResolvesTheAsientoId_...` and the `AfterARealValidarAsync` tests already seed a real balanced 3-line asiento (cargo 639915 sums 100 == BasePEN, 401111 = 18 == IgvPEN) that passes §7 for real. `AsientoBorrador()` (Array.Empty líneas) is used only by PATCH-projection tests that never call `validar`. Full suite green.
- [x] 3.7 `ValidarAsync_WhenAManualSplitNoLongerSumsToTheMovedHeaderBase_...` added to `ServicioDeFacturasPhase2Tests`: header BasePEN moved to 1000 by a #19 D4 PATCH, persisted cargos still sum 100 → `ResultadoComando.InvariantesIncumplidas` with `InvarianteContable.Principal` and message `"Los cargos 6x/1x suman 100, se esperaba 1000."`, not remapped to 409, no commit. `ProyeccionDeImportes.Derivar` / `PatchAsync` D4 UNCHANGED.

## Phase 4: API wiring (SmartNet.Api)

- [x] 4.1 (Batch 7) sugerencia DI in `Program.cs`: `ISugerenciaCuentaRepository`/`IMotivoRepository`/`IMotivoAtributoRepository` (Sql* adapters, conn-string ctor) + `ServicioDeSugerencia` all registered `AddSingleton` (compose-time consumer only — NO endpoint, NO SPA UI). `SqlFacturacionStore` 3rd optional ctor arg `ServicioDeSugerencia?` (2-arg kept for ~60 infra-test call sites) → threaded to `SqlUnidadDeTrabajo` → `ResolverHechosDeComposicionAsync` replaces the `// TODO Phase 4` null: `SugerirParaFacturaAsync(f.ProveedorCodigo, f.Motivo, ct)` (both added to the SELECT) → `CuentaSugerida = sugerencia.CandidatasVigentes.FirstOrDefault(c => c.Cuenta == sugerencia.Cuenta?.CuentaCodigo)`; no suggestion / service not injected → null → design-A2 placeholder path unchanged. `Sugerencia.Core` ProjectRef added to `SmartNet.Facturacion.Infrastructure.csproj`. **`cuentaCodigo` override in `RecomponerAsync` NOT wired this batch — still discarded; needs a new `IUnidadDeTrabajo.ObtenerCuentaContableAsync` member + Fake + `ServicioDeAsientos` change.**
- [x] 4.2 RED/GREEN `POST /api/asientos/{id}/recomponer` (Batch 6): `If-Match` required (428 without), optional body `{cuentaCodigo}` (nullable record param → empty body allowed), returns `AsientoRespuesta` + new ETag via `ResponderConAsientoActualizadoAsync` → `ServicioDeAsientos.RecomponerAsync(id, version, cuerpo?.CuentaCodigo, ResolverUsuarioId(http), tiempo.GetUtcNow(), ct)`. `.RequireAuthorization()`. Route added in `MapAsientoEndpoints`. 5 E2E tests in `AsientoEndpointsTests` (borrador→regenerate+bump ETag+1 REPARTO_MANUAL audit; CONFIRMADO→409; stale If-Match→412; no If-Match→428; no cookie→401).
- [x] 4.3 GREEN promotion auto-seed (Batch 6): NEW port `ISembradorDeAsiento { SembrarAsync(long facturaId, ct) }` in `Inbox.Core` + `SembradorDeAsientoAdapter` in `SmartNet.Api` (opens its own DI scope per call for the AddScoped `ServicioDeFacturas`; SWALLOWS `Conflicto(SinTipoCambio)` + `NoEncontrado` + any unexpected result, logs, never throws). `PromocionBackgroundService`: +ctor param `ISembradorDeAsiento`; `PromoverAsync` now returns `resultado.FacturaId`; call site is the `DecisionPromocion.Promueve` branch of `ProcesarPendientesAsync`, after `PromoverAsync` (SqlPromocionRepository commits its own tx). `Program.cs`: `AddSingleton<ISembradorDeAsiento, SembradorDeAsientoAdapter>()`.
- [x] 4.4 RED `Inbox.Infrastructure.Tests` (Batch 6): `PromocionBackgroundServiceTests` +`FakeSembradorDeAsiento` (records `FacturasSembradas`), `BuildSut` updated. 4 new tests: promote→seeds once with the facturaId; discard→never seeds; XML-then-associated-PDF-merge→seeds only for the XML promotion (merge branch `ProcesarDocumentoAsociadoAsync` untouched, zero seed calls); PDF-first-defer→never seeds. Full suite 75/75 (was 71/71).
- [x] 4.5 (Batch 8) E2E `FacturaEndpointsTests` (SmartNet.Api.Tests): new `CatalogoTestDataHelper` seeds `dbo.Motivo`(+cuenta prefijo) / `dbo.CuentaContable` hojas (nivel NULL ⇒ hoja; ctarefleja/ctapuente for DESTINO) / `fact.ProveedorAtributo`. 3 goldens: `Reglas_10_1_FacturaGravadaEnSolesConDestino_ComposesAndValidates` (5 líneas 631111/401111/421211 + DESTINO 946311/791111, base 1000/IGV 180/neto 1180 → `/validar` 200 VALIDADA + CONFIRMADO + NumeroAsiento), `Reglas_10_2_BoletaIgvAlCosto_HasNo401111` (`TipoComprobante='03'`, `Afectacion='INAFECTA'`, IGV 0, 656111=1180, no 401111 → CONFIRMADO), `Reglas_10_3_FacturaEnDolaresRelacionada_ConvertsWithDerivedRounding` (USD, TC venta 3.7895, 3789.50/682.11/4471.61, cuenta 431212, TipoCambioVenta 3.7895 → CONFIRMADO). Suggested account resolved via the real sugerencia cascade (Tier-3 first-candidate, no usage history needed). §10.4 percepción explicitly not covered (declared non-goal).
- [x] 4.6 (Batch 8) E2E `PatchBaseIgv_UnbalancingASeededSplit_BlocksValidar_UntilRecomponer`: seed §10.1-style gravada → `/abrir` (real 5-line asiento, base 1000) → `PATCH /api/facturas/{id}` `BaseImponible: 600 / Igv: 108` (#19 D4 moves header scalars, cargo líneas still sum 1000) → `/validar` **422** body contains `"Los cargos 6x/1x suman"` + `bloque-principal-invalido` (`InvarianteContable.Principal`, not remapped to 409) → `POST /api/asientos/{asientoId}/recomponer` (If-Match) 200 (631111 cargo now sums 600) → `/validar` 200 CONFIRMADO. `cuentaCodigo` override in `ServicioDeAsientos.RecomponerAsync` (design C1) also landed this batch: new `IUnidadDeTrabajo.ObtenerCuentaContableAsync(string, ct)` + `SqlUnidadDeTrabajo` (`SELECT ... FROM dbo.CuentaContable`) + `FakeUnidadDeTrabajo.CuentasContables` + `RecomponerAsync` resolves a non-null `cuentaCodigo` → `hechos with { CuentaSugerida = cuenta }`, unresolvable → `ResultadoComando.CorreccionInvalida` (422). Covered by `SqlUnidadDeTrabajoSembradoTests.ObtenerCuentaContableAsync_*` + `ServicioDeAsientosTests.RecomponerAsync_WithAnExplicitCuentaCodigo_*` / `_WithAnUnknownCuentaCodigo_*` + `AsientoEndpointsTests` recomponer suite.

### Batch 6 side-fix (pre-existing regression, un-broke `main`)
`FacturaTestDataHelper.InsertarFacturaAsync` produced an incoherent GRAVADA factura (`TotalOrig 118`, no `IgvOrig`). Since Batch 4 wired `AbrirAsync` to seed a composed asiento, that fixture drove `ComposicionDeAsiento.Componer` to emit a `401111` IGV line with `Debe = 0`, which `CK_Linea_Tipo` (`Tipo='D' AND Debe>0`) rejects → 500. **3 `FacturaEndpointsTests.Abrir_*` tests were already RED on `main` before Batch 6.** Fix: `InsertarFacturaAsync` now seeds `IgvOrig = 18.00` by default (params `afectacion`/`totalOrig`/`igvOrig` added; boleta/no-gravada callers pass `igvOrig: null`). Full `SmartNet.Api.Tests` 208/208 green after the fix. `ComposicionDeAsiento.Componer` byte-for-byte unchanged (the GRAVADA-without-IGV quirk is the Batch-1 carried-forward risk, still owned by a future decision).

### Batch 7 additions
- **Zero-line seed guard** (`SembradoDeAsiento.Sembrar`, pure Core): after `Componer` returns, drop any `LineaAsiento` with `Debe == 0 && Haber == 0` (a GRAVADA factura with `IgvOrig = 0` makes `Componer` emit a `401111` line with `Debe = 0` that `CK_Linea_Tipo` rejects → seed INSERT throws → 500 at `abrir`/`recomponer`), then renumber `Orden` 1..n. `ComposicionDeAsiento.Componer` byte-for-byte unchanged. §7 (`InvariantesDeConfirmacion`) still gates `validar`: a GRAVADA asiento missing its `401111` line fails PRINCIPAL; the no-suggestion path fails Global-2 via the placeholder. 2 new `SembradoDeAsientoTests` (8/8). `FacturaTestDataHelper` Batch-6 `IgvOrig = 18.00` default LEFT AS-IS — it is a faithful GRAVADA (18 = 18% of 100), not a mask.
- **4.5 / 4.6 STILL NOT DONE** — E2E §10.1–§10.3 goldens + PATCH-descuadre-recomponer. 4.1 is now wired so a seeded factura whose `dbo.Motivo` has prefijos + matching `dbo.CuentaContable` hojas gets a real cargo account → `validar` can reach CONFIRMADO. Remaining work: a `CatalogoTestDataHelper` in `SmartNet.Api.Tests` seeding `dbo.Motivo`(+prefijos) / `dbo.CuentaContable`(hojas 60x/63x/401111/421211/reflejo-puente) / `fact.SugerenciaCuenta` (to pin the cascade winner) / `fact.TipoCambio` venta 3.7895 for §10.3; then the abrir→GET asiento→assert §10.x numbers→validar 200 flow, and PATCH base/IGV→validar 422 `"Los cargos 6x/1x suman"`→recomponer→validar 200.

## Phase 5: SPA (Angular — npm test)

- [x] 5.1 (Batch 9) `asiento.service.ts` NEW `recomponer(asientoId, cuentaCodigo?)`: `POST /api/asientos/{id}/recomponer` with `If-Match` (`etagRequerido()`), body `cuentaCodigo ? { cuentaCodigo } : null`, `observe: 'response'` → `aplicar(respuesta)` (same ETag-threading pattern as `actualizarLinea`). +2 `asiento.service.spec` tests (If-Match sent, new ETag + asiento threaded; optional `{ cuentaCodigo }` body).
- [x] 5.2 (Batch 9) `detalle-page.ts/.html`: `onRecomponer()` (try/`manejarError` pattern) → `asientoService.recomponer(asiento().asientoContableId)`. Two-step in-component confirm (`confirmandoRecomponer` signal + `pedirRecomponer()`/`cancelarRecomponer()`, mirrors `asiento-lineas` delete-confirm — project avoids `window.confirm`). Button `data-testid="recomponer-asiento"` shown only when `a.estado !== 'CONFIRMADO'`. +3 `detalle-page.spec` tests (visible BORRADOR / hidden CONFIRMADO / confirm→POST+If-Match).
- [x] 5.3 (Batch 9) `detalle-page`: `puedeGenerarAsiento = computed(() => !!factura() && asiento() === null)` → `data-testid="generar-asiento"` block; `generarAsiento()` → `facturaService.abrir(facturaId)` (`POST /api/facturas/{id}/abrir`, no body/If-Match) → `cargarTodo()`. +3 spec tests.
- [x] 5.4 (Batch 9) descuadre marker: `descuadreAsiento = computed(() => !!asiento() && estado !== 'CONFIRMADO' && !cuadre().cuadrado)` bound to the EXISTING `cuadre()` computed; `<p class="alerta alerta--informativa" data-testid="descuadre-asiento">` primitive class, NO new component, `cuadre.ts` UNCHANGED. +3 spec tests.
- [x] 5.5 (Batch 9) `factura-form` / `asiento-lineas` NOT modified. Regression test in `detalle-page.spec` asserts seeded `asiento().basePEN`/`igvPEN` render as `valor-base`/`valor-igv` (100.00 / 18.00) in `factura-form`.

## Phase 6: Documentation (not a test task)

- [x] 6.1 Append to `DEUDA-TECNICA.md` the REGLAS §12 points 1 & 5 note (owner obs 309): §24 wires §5–§7 into production but ratifies no rule; point 5 (NC hereda TC) unreachable this cycle (NC non-goal); point 1 (TC venta) already executes via #19. Not a ratification gate.

## NON-GOALS to guard (do NOT touch)

- No NC / `HerenciaNotaCredito` composition wiring; `Herencia = null`.
- No percepción / `fact.Factura.PercepcionOrig` column; `PercepcionOrig = 0`. §10.4 stays unreachable.
- No `.sql` schema script, no checksum regeneration (all grants exist in `008`).
- No change to any REGLAS rule; `InvariantesDeConfirmacion.cs`, `ComposicionDeAsiento.Componer`, `ProyeccionDeImportes.Derivar`, `PatchAsync` D4, `CargarAsientoAsync` all byte-for-byte unchanged.
- No `sugerencia` endpoints or SPA suggestion UI (compose-time consumer only).
- No new `AuditoriaCorreccion.Accion` enum value.

## Implementation order

Phase 1 → 2 → 3 (Core/Infra/Service, RED→GREEN each production file) → 4 (API + promotion + E2E goldens) → 5 (SPA) → 6 (doc). Each phase's tests must be green before the next. Full `dotnet test` + `npm test` before candidate freeze.
