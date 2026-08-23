# Apply Progress: API de facturas y asientos (BACKLOG #11)

**PR 4 of 4 — Phase 4 of tasks.md COMPLETE (5/5 tasks). Chain strategy: stacked-to-main. PR 4 targets
PR 3's branch (PR 3 already complete: 20/25 tasks, 154/154 tests green before this batch).**
tasks.md is now 25/25 complete. Ready for `sdd-verify` across the full chain.

## Mode

Strict TDD (enabled, `dotnet test` runner confirmed working with a live local SQL Server instance —
integration tests actually ran, not skipped, for both PR 1 and PR 2).

## Completed Tasks (Phase 1, all 11 — PR 1, unchanged from prior batch)

- [x] 1.1 RED: TokenDeConcurrencia round-trip tests
- [x] 1.2 GREEN: TokenDeConcurrencia pure static codec
- [x] 1.3 RED: ResultadoComando/CasoConflicto enum-shape tests
- [x] 1.4 GREEN: ResultadoComando, CasoConflicto in Core
- [x] 1.5 RED: fake-IUnidadDeTrabajo sequencing tests (ServicioDeFacturas/ServicioDeAsientos/ServicioDeIntegraciones)
- [x] 1.6 GREEN: IFacturacionStore, IUnidadDeTrabajo, ICommandQueueRepository, IEstadoIntegracionRepository ports + 3 services in Core
- [x] 1.7 RED: PurityScanTests copy for SmartNet.Facturacion.Core
- [x] 1.8 GREEN: wired 4 new projects into SmartNet.sln under a new "facturacion" solution folder; PurityScan passes
- [x] 1.9 Created SmartNet/db/schema/015_commandqueue_reconectar_google.sql; added rollback/015_down.sql; updated checksums.txt
- [x] 1.10 RED: SqlUnidadDeTrabajoTests — CAS stale-version -> VersionEnConflicto; correlativo UPDLOCK gapless-after-rollback, against a REAL migrated test DB
- [x] 1.11 GREEN: SqlFacturacionStore, SqlUnidadDeTrabajo, SqlCommandQueueRepository, SqlEstadoIntegracionRepository in SmartNet.Facturacion.Infrastructure

## Completed Tasks (Phase 2, all 6 — PR 2, this batch)

- [x] 2.1 RED: `SmartNet.Api.Tests` — `PATCH /api/facturas/{id}` matching/stale/missing If-Match -> 200/412/428
- [x] 2.2 RED: `POST /abrir`, `/validar` (success balanced, 409 duplicado; 404 no-asiento), `/descartar` (no audit, 409 ya-validada)
- [x] 2.3 RED: adjuntos POST/DELETE -> `DOCUMENTACION_ACTUALIZADA`/`ELIMINACION_ADJUNTO` scenarios
- [x] 2.4 GREEN: `IfMatch.Requerido` (`IfMatch.cs`), `ProblemasDeNegocio.Map` (409/412/422/428) in `SmartNet.Api`
- [x] 2.5 RED (Api unit): exhaustive `InvarianteContable` -> status/type enum-coverage test (`ProblemasDeNegocioInvarianteCoverageTests`)
- [x] 2.6 GREEN: `FacturaEndpoints.cs` (7 routes: GET, PATCH, abrir, validar, descartar, adjuntos POST/DELETE), registered in `Program.cs`

## Files Changed — PR 2 (this batch)

| File | Action | What Was Done |
|------|--------|----------------|
| `SmartNet/facturacion/SmartNet.Facturacion.Core/FacturaPersistida.cs` | Created | Factura-shaped mirror of `fact.Factura` (analogous to PR 1's `AsientoPersistido`) |
| `SmartNet/facturacion/SmartNet.Facturacion.Core/CorreccionFactura.cs` | Created | PATCH body — nullable per-field diff record |
| `SmartNet/facturacion/SmartNet.Facturacion.Core/AdjuntoManual.cs` | Created | `fact.AdjuntoManual` mirror (metadata only, no byte storage — see deviations) |
| `SmartNet/facturacion/SmartNet.Facturacion.Core/IUnidadDeTrabajo.cs` | Modified | +6 factura-shaped port members (Cargar/GuardarFactura, ObtenerAsientoVigenteId, CrearAsientoBorrador, Registrar/EliminarAdjunto) |
| `SmartNet/facturacion/SmartNet.Facturacion.Core/ServicioDeFacturas.cs` | Modified | +PatchAsync, +AbrirAsync, +ValidarPorFacturaAsync (refactored ValidarAsync into a shared `ValidarInternoAsync`), +DescartarAsync, +RegistrarAdjuntoAsync, +EliminarAdjuntoAsync |
| `SmartNet/facturacion/SmartNet.Facturacion.Core.Tests/FakeUnidadDeTrabajo.cs` | Modified | Fake implementations of the 6 new port members |
| `SmartNet/facturacion/SmartNet.Facturacion.Core.Tests/ServicioDeFacturasPhase2Tests.cs` | Created | 18 unit tests (fake port, no DB) for the 6 new Core methods |
| `SmartNet/facturacion/SmartNet.Facturacion.Infrastructure/SqlUnidadDeTrabajo.cs` | Modified | +6 SQL implementations (CAS on `fact.Factura`, `UQ_Asiento_Vigente` lookup, `fact.AdjuntoManual` insert/soft-delete) |
| `SmartNet/facturacion/SmartNet.Facturacion.Infrastructure.Tests/FacturacionTestDatabaseFixtureHelper.cs` | Modified | +`ObtenerVersionFacturaAsync` helper |
| `SmartNet/facturacion/SmartNet.Facturacion.Infrastructure.Tests/SqlUnidadDeTrabajoFacturaTests.cs` | Created | 10 integration tests against a real migrated test DB |
| `SmartNet/api/SmartNet.Api/ProblemasDeNegocio.cs` | Created | `ResultadoComando`→HTTP mapper (409/412/422/428), exhaustive `CasoConflicto`/`InvarianteContable` switches |
| `SmartNet/api/SmartNet.Api/IfMatch.cs` | Created | `If-Match` header codec wrapper → 428 on missing/`*`/malformed |
| `SmartNet/api/SmartNet.Api/FacturaEndpoints.cs` | Created | 7 routes: `GET`, `PATCH`, `abrir`, `validar`, `descartar`, adjuntos `POST`/`DELETE` |
| `SmartNet/api/SmartNet.Api/Program.cs` | Modified | DI: `IFacturacionStore` (Singleton, lazy `IConfiguration`), `ServicioDeFacturas` (Scoped, per design D8); `app.MapFacturaEndpoints()` |
| `SmartNet/api/SmartNet.Api/SmartNet.Api.csproj` | Modified | +`ProjectReference` to `SmartNet.Facturacion.Core`/`.Infrastructure` |
| `SmartNet/api/SmartNet.Api.Tests/FacturaTestDataHelper.cs` | Created | Local fixture-insert helper (factura + balanced asiento) |
| `SmartNet/api/SmartNet.Api.Tests/FacturaEndpointsTests.cs` | Created | 18 integration tests against the real DB via `SmartNetApiFactory` |
| `SmartNet/api/SmartNet.Api.Tests/ProblemasDeNegocioInvarianteCoverageTests.cs` | Created | 9 pure unit tests (no DB/HTTP) — exhaustive `InvarianteContable` coverage |
| `openspec/changes/api-facturas-asientos/tasks.md` | Modified | `[x]` Phase 2 |

## TDD Cycle Evidence — PR 2

| Task | Test File | Layer | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|-----|-------|-------------|----------|
| 2.1/2.2/2.3 (Core) | ServicioDeFacturasPhase2Tests.cs | Unit (fake port) | Written | Passed (18/18) | PATCH (4 cases incl. resend-no-audit), abrir (idempotency), validarPorFactura (delegation), descartar (2), adjuntos (4) | Clean |
| 2.1/2.2/2.3 (Infra) | SqlUnidadDeTrabajoFacturaTests.cs | Integration (real SQL Server) | Written | Passed (10/10) | CAS match/stale, UQ_Asiento_Vigente present/absent/anulado, crear-borrador, adjuntos registrar/eliminar/doble-eliminar | Clean |
| 2.1/2.2/2.3 (Api) | FacturaEndpointsTests.cs | Integration (real DB via SmartNetApiFactory) | Written | Passed (18/18) | 428/412/200 If-Match, abrir idempotent/404, validar success/404/409-dup, descartar no-audit/409, adjuntos outbox/audit/400 | Clean |
| 2.4 | IfMatch.cs / ProblemasDeNegocio.cs | (implementation, driven by the RED tests above) | — | — | — | Clean |
| 2.5 | ProblemasDeNegocioInvarianteCoverageTests.cs | Unit (pure, no DB) | Written | Passed (9/9) | All 7 InvarianteContable values individually + 2-fallo aggregate + Aplicado-throws guard | Clean |

## Test Summary — cumulative (PR 1 + PR 2)

- PR 1: 53/53 (40 Core + 13 Infrastructure)
- PR 2: 55 new (18 Core unit + 10 Infrastructure integration + 27 Api integration/unit)
  - `SmartNet.Facturacion.Core.Tests`: 55/55 (40 PR1 + 15 new — Core unit test count; see note below)
  - `SmartNet.Facturacion.Infrastructure.Tests`: 23/23 (13 PR1 + 10 new)
  - `SmartNet.Api.Tests`: 50/50 (25 PR1/auth + 25 new: 18 Factura integration + 9 ProblemasDeNegocio — 2 non-Theory + 7 Theory rows collapse to the reported count via xUnit's per-case reporting)
- **Grand total this session's real runs: Core.Tests 55/55, Infrastructure.Tests 23/23, Api.Tests 50/50 — all green.**

## Work Unit Evidence — PR 2

| Evidence | Value |
|---|---|
| Focused test command and result | `dotnet test` in `SmartNet.Facturacion.Core.Tests` → 55/55; `dotnet test` in `SmartNet.Facturacion.Infrastructure.Tests` → 23/23; `dotnet test --filter "FullyQualifiedName~Factura\|FullyQualifiedName~ProblemasDeNegocio"` in `SmartNet.Api.Tests` → 25/25 |
| Runtime harness | `FacturaEndpointsTests` against a migrated `fact_test_<guid>` database via `SmartNetApiFactory` (real ASP.NET Core host, real cookie auth, real SQL Server) |
| Rollback boundary | Remove `FacturaEndpoints.cs`, `ProblemasDeNegocio.cs`, `IfMatch.cs`; revert the `SmartNet.Api.csproj` 2 `ProjectReference` lines and `Program.cs`'s DI/`MapFacturaEndpoints()` additions; revert the 6 new `IUnidadDeTrabajo` members and their `ServicioDeFacturas`/`SqlUnidadDeTrabajo`/`FakeUnidadDeTrabajo` implementations. No other PR (3/4) references any Phase 2 symbol yet — fully isolated. |
| Regression safety net | Full solution build (`dotnet build SmartNet.sln`) → 0 errors; full `SmartNet.Api.Tests` run (50/50, includes all PR1 auth/bandeja tests) confirms no regression |

## Deviations from Design — PR 1 (carried forward, unchanged)

1. `IUnidadDeTrabajo` was asiento-shaped only in PR 1 — **RESOLVED in PR 2** by adding a parallel
   factura-shaped set of members (`CargarFacturaAsync`/`GuardarFacturaAsync`/
   `ObtenerAsientoVigenteIdAsync`/`CrearAsientoBorradorAsync`/adjuntos), rather than changing
   `ValidarAsync`'s existing signature. `ValidarPorFacturaAsync` resolves factura→asiento inside the
   transaction, then delegates to the exact same `ValidarInternoAsync` PR 1's `ValidarAsync` uses —
   zero duplication of D4/D5 logic, zero behavior change to the PR 1 method's existing tests.
2. `CasoConflicto.AsientoYaConfirmado` reuse for out-of-table 409s — **repeated in PR 2**: both
   `DescartarAsync` (factura ya validada) and `TraducirResultadoEscrituraFactura`'s `EstadoInvalido`
   branch reuse this same enum value rather than inventing a 10th `CasoConflicto` member. Same
   rationale as PR 1: no 409 row in ADR 0008's table covers "factura ya validada, no puede
   descartarse" specifically. Flagged for product-owner sign-off alongside PR 1's original instance.
3. Outbox `Tipo` — no new deviation in PR 2; adjuntos post-validar correctly emit
   `DOCUMENTACION_ACTUALIZADA` (the fifth real `CK_OutboxEvent_Tipo` value), matching ADR 0008 §
   "Los adjuntos siguen abiertos después de validar" literally.
4. `HechosDeConflicto` — **NOT resolved in PR 2**, deliberately, and documented again here rather than
   silently: `ITipoCambioRepository` is still not wired. `AbrirAsync`'s spec.md scenario "opening
   foreign-currency factura with no tipo de cambio → 409" is **not implemented** in this PR — `abrir`
   currently always succeeds (idempotently) regardless of `Moneda`/tipo de cambio. `SinTipoCambio`,
   `AfectacionMixta`, `AfectacionNoVerificada` remain hardcoded `false` in
   `SqlUnidadDeTrabajo.CargarAsientoAsync` (PR 1's own scope note, untouched by PR 2). This is a
   genuine scope gap versus spec.md, not an oversight — wiring `ITipoCambioRepository` plus the
   NC-reference-resolution and afectación-verification flows is a meaningfully sized sub-feature of
   its own; deferred to PR 3/4 or a dedicated follow-up, per the launch instructions' explicit
   permission to document rather than force it into this PR.
5. `GuardarAsientoAsync` still writes only header columns, not líneas — unchanged, still Phase 3 scope.

## New Deviations — PR 2

6. **`fechaCorteContable` is a required query parameter on `POST /validar`**, not read from
   `fact.Configuracion`. ADR 0008 lists `GET/PUT /api/configuracion` as a REST endpoint but neither
   that endpoint nor a `fact.Configuracion` read port is in this SDD change's 4 domains
   (`api-facturas`, `api-asientos`, `api-incidencias-integraciones`, `tipos-de-cambio`) — inventing an
   undesigned persistence read for "fecha de corte contable" was judged worse than an explicit query
   parameter the caller (SPA, once built) must supply. Flag for whichever item eventually builds
   `GET/PUT /api/configuracion`.
7. **Adjuntos byte storage is out of scope for #11.** `AdjuntoManual`/`RegistrarAdjuntoRequest` carry
   already-resolved metadata (`NombreArchivo`/`RutaRelativa`/`MimeType`/`TamanoBytes`); `POST
   /api/facturas/{id}/adjuntos` does not accept `multipart/form-data` or write file bytes anywhere.
   design.md's own File Changes section never described an `IArchivoStore`-shaped port, and ADR 0013
   (Drive archiving) is explicitly a different item's concern. The caller is responsible for having
   already persisted the file at `RutaRelativa` before calling this endpoint.
8. **`AbrirAsync`'s "no tipo de cambio" 409 is not implemented** — see deviation 4 above; restated here
   because it is a spec.md scenario for `abrir` specifically ("opening foreign-currency factura with
   no tipo de cambio → 409"), not just a `validar` gap.
9. **`ServicioDeFacturas.PatchAsync`'s CAS-failure "wrong state" branch reuses `AsientoYaConfirmado`
   with a factura-specific message** ("La factura ya fue validada o descartada.") rather than a
   distinct enum value — same pattern as deviation 2, listed separately because it is a different
   call site (`TraducirResultadoEscrituraFactura`, not `DescartarAsync`'s explicit pre-check). In
   practice this branch is currently unreachable from `PatchAsync` (no state gating there — PATCH is
   allowed in any `Estado` per spec.md's "correction to already-validated factura" scenario) but is
   kept for `ResultadoEscritura.EstadoInvalido` completeness/defensiveness, matching `TraducirResultadoEscritura`'s asiento-shaped counterpart from PR 1.

## Issues Found

None blocking. All RED tests were written and observed failing (compile errors against the not-yet-existing `PatchAsync`/`AbrirAsync`/etc. members, and 401/404-by-default routing before `FacturaEndpoints.cs` existed) before the corresponding GREEN implementation, consistent with Strict TDD.

## Workload / PR Boundary

- Mode: chained PR slice (stacked-to-main), Unit 2 of 4
- Current work unit: Facturas endpoints (tasks.md Phase 2)
- Boundary: starts from PR 1's asiento-only surface (no HTTP, no factura-shaped port) and ends with
  a complete `FacturaEndpoints.cs` (7 routes), the shared `IfMatch`/`ProblemasDeNegocio` HTTP layer,
  and the factura-shaped `IUnidadDeTrabajo` extension — no `AsientoEndpoints.cs`, no
  `TipoCambioEndpoints.cs`/`IntegracionEndpoints.cs`, no `ci.yml` change (all explicitly PR 3/4 scope).
- Estimated review budget impact: ~1850 changed lines this slice (11 new files ≈1346 lines + edits to
  7 PR 1 files ≈506 lines) — one deliberate slice of the High-risk 3200-3800 total forecast in
  tasks.md, consistent with PR 1's own ~2420-line slice.

## Completed Tasks (Phase 3, all 3 — PR 3, this batch)

- [x] 3.1 RED: `SmartNet.Api.Tests` — `PATCH /api/asientos/{id}` If-Match matching/stale/missing ->
  200/412/428; editing a CONFIRMADO asiento without `reabrir` -> 409.
- [x] 3.2 RED: líneas by `LineaId` (never position) survive add/delete/reorder; `reabrir`
  motivo-required (400)/BORRADOR-409 through HTTP; `anular` terminal/already-anulado-409 through
  HTTP; every líneas write command registers ONE `AuditoriaCorreccion(Accion=REPARTO_MANUAL)` row.
- [x] 3.3 GREEN: `AsientoEndpoints.cs` (6 routes: `PATCH /api/asientos/{id}`,
  `POST/PATCH/DELETE /api/asientos/{id}/lineas[/{lineaId}]`, `POST .../reabrir`, `POST .../anular`),
  registered in `Program.cs` (`app.MapAsientoEndpoints()`, `ServicioDeAsientos` AddScoped).

## Files Changed — PR 3 (this batch)

| File | Action | What Was Done |
|------|--------|----------------|
| `SmartNet/facturacion/SmartNet.Facturacion.Core/LineaPersistida.cs` | Created | `LineaAsiento` (#8) + stable `LineaId` — the persistence-layer wrapper #8 deliberately excludes |
| `SmartNet/facturacion/SmartNet.Facturacion.Core/ResultadoLinea.cs` | Created | `ResultadoEscritura` + nullable new `LineaId`, for `AgregarLineaAsync`'s port return |
| `SmartNet/facturacion/SmartNet.Facturacion.Core/IUnidadDeTrabajo.cs` | Modified | +4 líneas-shaped port members: `CargarLineasPersistidasAsync`, `AgregarLineaAsync`, `ActualizarLineaAsync`, `EliminarLineaAsync` |
| `SmartNet/facturacion/SmartNet.Facturacion.Core/ServicioDeAsientos.cs` | Modified | +`AgregarLineaAsync`/`ActualizarLineaAsync`/`EliminarLineaAsync` (BORRADOR gate + before/after JSON snapshot + `REPARTO_MANUAL` audit), +`RegistrarRepartoManualAsync`/`SerializarLineas` helpers |
| `SmartNet/facturacion/SmartNet.Facturacion.Core.Tests/FakeUnidadDeTrabajo.cs` | Modified | Fake implementations of the 4 new port members |
| `SmartNet/facturacion/SmartNet.Facturacion.Core.Tests/ServicioDeAsientosTests.cs` | Modified | +9 unit tests (fake port, no DB) for the 3 new Core línea commands (Aplicado/Conflicto/VersionEnConflicto/NoEncontrado paths) |
| `SmartNet/facturacion/SmartNet.Facturacion.Infrastructure/SqlUnidadDeTrabajo.cs` | Modified | +4 SQL implementations; +`TocarEncabezadoAsync` shared CAS-bump helper (`SET Glosa = Glosa`, rowversion touch without a business-column write) |
| `SmartNet/facturacion/SmartNet.Facturacion.Infrastructure.Tests/SqlUnidadDeTrabajoAsientoLineasTests.cs` | Created | 5 integration tests against a real migrated test DB — CAS bump, stale-version rejection, LineaId survives delete/reorder, NoEncontrado, read-back |
| `SmartNet/api/SmartNet.Api/AsientoEndpoints.cs` | Created | 6 routes: PATCH asiento, POST/PATCH/DELETE líneas, POST reabrir, POST anular |
| `SmartNet/api/SmartNet.Api/Program.cs` | Modified | DI: `ServicioDeAsientos` (Scoped, same pattern as `ServicioDeFacturas`); `app.MapAsientoEndpoints()` |
| `SmartNet/api/SmartNet.Api.Tests/AsientoEndpointsTests.cs` | Created | 14 integration tests against the real DB via `SmartNetApiFactory` |
| `openspec/changes/api-facturas-asientos/tasks.md` | Modified | `[x]` Phase 3 |

## TDD Cycle Evidence — PR 3

| Task | Test File | Layer | RED | GREEN | TRIANGULATE |
|------|-----------|-------|-----|-------|-------------|
| 3.1/3.2 (Core) | ServicioDeAsientosTests.cs (líneas section) | Unit (fake port) | Written, confirmed failing (compile error: methods didn't exist) | Passed (9/9 new, 62/62 total) | Agregar (Aplicado/Conflicto/VersionEnConflicto), Actualizar (Aplicado/NoEncontrado), Eliminar (Aplicado/Conflicto) |
| 3.1/3.2 (Infra) | SqlUnidadDeTrabajoAsientoLineasTests.cs | Integration (real SQL Server) | Written, confirmed failing (compile error: `IUnidadDeTrabajo` members not implemented) | Passed (5/5 new, 28/28 total) | CAS bump + header-version-changes assertion, stale-version rejects and inserts nothing, LineaId survives add→delete→update-of-survivor, NoEncontrado on a nonexistent LineaId, read-back with LineaId |
| 3.1/3.2 (Api) | AsientoEndpointsTests.cs | Integration (real DB via SmartNetApiFactory) | Written, confirmed failing (compile error: `AsientoEndpoints`/request records didn't exist) | Passed (14/14 new, 19/19 filtered `~Asiento`) | PATCH 428/412/200/409, POST líneas 201+LineaId, delete-then-patch-survivor addressing, PATCH líneas 409-sin-reabrir, reabrir 200/400-sin-motivo/409-borrador, anular 200/409-terminal/400-sin-motivo, 401 guard |
| 3.3 | AsientoEndpoints.cs | (implementation, driven by the RED tests above) | — | — | — |

## Test Summary — cumulative (PR 1 + PR 2 + PR 3)

- `SmartNet.Facturacion.Core.Tests`: 62/62 (55 PR1+PR2 + 7 new)
- `SmartNet.Facturacion.Infrastructure.Tests`: 28/28 (23 PR1+PR2 + 5 new)
- `SmartNet.Api.Tests`: 64/64 (50 PR1+PR2 + 14 new)
- Full solution `dotnet build SmartNet.sln`: 0 errors, 0 warnings
- **Grand total this session's real runs: Core.Tests 62/62, Infrastructure.Tests 28/28, Api.Tests
  64/64 — all green. Cumulative project total: 154/154.**

## Work Unit Evidence — PR 3

| Evidence | Value |
|---|---|
| Focused test command and result | `dotnet test` in `SmartNet.Facturacion.Core.Tests` → 62/62; `dotnet test` in `SmartNet.Facturacion.Infrastructure.Tests` → 28/28; `dotnet test --filter "FullyQualifiedName~Asiento"` in `SmartNet.Api.Tests` → 19/19 |
| Runtime harness | `AsientoEndpointsTests` against a migrated `fact_test_<guid>` database via `SmartNetApiFactory` (real ASP.NET Core host, real cookie auth, real SQL Server) |
| Rollback boundary | Remove `AsientoEndpoints.cs`; revert `Program.cs`'s `ServicioDeAsientos` DI registration and `MapAsientoEndpoints()` call; revert the 4 new `IUnidadDeTrabajo` línea members and their `ServicioDeAsientos`/`SqlUnidadDeTrabajo`/`FakeUnidadDeTrabajo` implementations; delete `LineaPersistida.cs`/`ResultadoLinea.cs`. `ServicioDeAsientos.ActualizarAsync`/`ReabrirAsync`/`AnularAsync` (PR 1) are untouched — only new methods were appended. No PR 4 references any Phase 3 symbol yet — fully isolated. |
| Regression safety net | Full solution build (`dotnet build SmartNet.sln`) → 0 errors; full `SmartNet.Api.Tests` run (64/64, includes all PR1 auth/bandeja + PR2 factura tests) confirms no regression; full `SmartNet.Facturacion.Core.Tests` (62/62) and `SmartNet.Facturacion.Infrastructure.Tests` (28/28) confirm no regression in the Core/Infra layers either. |

## Deviations from Design — PR 1/PR 2 (carried forward, unchanged)

Deviations 1-9 from PR 1/PR 2 (see prior revision) are unchanged by PR 3. Deviation 5 ("`GuardarAsientoAsync`
still writes only header columns, not líneas — Phase 3 scope") is now **superseded**: líneas are
written by the four new `IUnidadDeTrabajo` members added in this PR (deviation 12 below), not by
extending `GuardarAsientoAsync` itself — `GuardarAsientoAsync` still only writes the header, which is
correct: header CAS and línea CAS are separate write paths that happen to share one CAS check
(deviation 11).

## New Deviations — PR 3

10. **`ServicioDeAsientos.ActualizarAsync`'s `PATCH /api/asientos/{id}` is a single generic
    field-correction command, not a typed multi-field patch like `FacturaEndpoints`'s
    `CorreccionFacturaRequest`.** `ActualizarAsync`'s signature (frozen by PR 1's
    `ServicioDeAsientosTests`) takes exactly one `(campo, valorOriginal, valorNuevo)` tuple per call
    and writes the loaded `AsientoPersistido` back UNCHANGED — it never applies `campo`/`valorNuevo`
    to any actual property. This PR's `AsientoEndpoints.PatchAsientoAsync` therefore exposes
    `CorreccionAsientoRequest(string Campo, string? ValorOriginal, string? ValorNuevo)` as-is: the
    caller (eventually the SPA) is responsible for computing the diff and naming the field, since
    `AsientoContable`'s editable header surface (`Glosa`/`MotivoDescripcion`/`FechaContable`) has no
    per-field auto-mapping in Core. Changing this would mean altering `ActualizarAsync`'s frozen PR 1
    signature/behavior, which was explicitly out of scope — flagged for product-owner review together
    with whether `PATCH /api/asientos/{id}` needs a typed body in a follow-up.
11. **Líneas commands (POST/PATCH/DELETE /lineas) CAS against `fact.AsientoContable.Version`, not a
    per-línea version column** (`fact.AsientoContableDetalle` has none). `TocarEncabezadoAsync` does
    `UPDATE fact.AsientoContable SET Glosa = Glosa WHERE AsientoContableId=@id AND Version=@expected`
    — a deliberate no-op write to a column no #11 flow currently populates, solely to force SQL
    Server's `ROWVERSION` to bump on the header row atomically with the CAS check, before the actual
    línea INSERT/UPDATE/DELETE runs in the same transaction. This matches design D2's explicit listing
    of "3 line routes" among the ETag-bearing mutable surfaces, interpreted as "one concurrency token
    per asiento, not per línea" since design never described a separate línea-level token. If `Glosa`
    is ever wired into a real flow, `TocarEncabezadoAsync` must switch to touching a different inert
    column (or a dedicated `UltimaModificacionLineas` timestamp column added by a future schema
    migration) so it doesn't silently overwrite a real edit made by a concurrent PATCH on `Glosa`
    itself — flagged as a follow-up risk, not blocking for this PR since nothing writes `Glosa` today.
12. **`REPARTO_MANUAL` audit fires on EVERY líneas write (add/update/delete), not only "manual
    redistribution."** spec.md's api-asientos requirement says "manual redistribution of líneas
    post-reabrir -> `AuditoriaCorreccion(Accion=REPARTO_MANUAL)`"; design D6's table row 7 says the
    same, `Campo=Cargos`, `Motivo=null`. Since #11's 4 domains define no narrower "this specific edit
    counts as a redistribution vs. a plain add" distinction, every líneas command (not just ones that
    touch DESTINO/Cargos specifically) writes one `REPARTO_MANUAL` row with a before/after JSON
    snapshot of the asiento's full línea set (`LineaId`, `Bloque`, `Tipo`, `Debe`, `Haber`,
    `CuentaCodigo`) as `ValorOriginal`/`ValorNuevo`. This is the broadest reading consistent with D6's
    literal text and avoids inventing an unratified narrower rule — flagged for product-owner
    confirmation that "every línea edit" (not just DESTINO edits) should count as `REPARTO_MANUAL`.
13. **`AnularAsync`'s "Motivo requerido" is enforced by `AsientoEndpoints.AnularAsync` (HTTP layer),
    not by `ServicioDeAsientos.AnularAsync` itself** (PR 1's `AnularAsync` accepts any string,
    including blank, and audits it as-is — only `ReabrirAsync` throws `ArgumentException` on blank
    motivo in Core). Design D6's table lists `Motivo: required` for both REAPERTURA and ANULACION
    rows, so the HTTP layer defensively blocks a blank/whitespace motivo with 400 before calling
    `AnularAsync`, mirroring `FacturaEndpoints.EliminarAdjuntoAsync`'s existing motivo-required
    pattern rather than changing PR 1's already-tested `ServicioDeAsientosTests.AnularAsync_*` tests.
14. **`POST /api/asientos/{id}/lineas` returns `201 Created` with `{ LineaId }` in the body** (plus the
    header ETag) — this is the one route in Phase 2/3 that creates a NEW addressable sub-resource
    (`/api/asientos/{id}/lineas/{lineaId}`), unlike every other command in this change which returns
    `200 OK` with the parent resource. `FacturaEndpoints`/design.md never specified a response shape
    for a créate-and-return-id route since Phase 2 had none; `201` + `Location` header + minimal body
    is the standard REST idiom for a POST that creates a child resource, applied here without an
    explicit design precedent to follow.

## Issues Found

None blocking. All RED tests were written and observed failing (compile errors against the
not-yet-existing `AgregarLineaAsync`/`ActualizarLineaAsync`/`EliminarLineaAsync`/`IUnidadDeTrabajo`
línea members, and `AsientoEndpoints`/its request records) before the corresponding GREEN
implementation, consistent with Strict TDD — confirmed via `dotnet build`/`dotnet test` failing with
`CS1061`/`CS0535`/`CS0246` before each GREEN commit within this batch.

## Workload / PR Boundary

- Mode: chained PR slice (stacked-to-main), Unit 3 of 4
- Current work unit: Asientos endpoints (tasks.md Phase 3)
- Boundary: starts from PR 2's factura-only HTTP surface (no `AsientoEndpoints.cs`, `GuardarAsientoAsync`
  header-only) and ends with a complete `AsientoEndpoints.cs` (6 routes), the líneas-by-`LineaId`
  `IUnidadDeTrabajo` extension, and `REPARTO_MANUAL` auditing — no `TipoCambioEndpoints.cs`,
  `IntegracionEndpoints.cs`, no `ci.yml` change (all explicitly PR 4 scope).
- Estimated review budget impact: ~1230 changed lines this slice (5 new files ≈737 lines + edits to
  6 PR 1/2 files ≈493 lines, additions-only — no line CRUD existed to delete/modify) — one deliberate
  slice of the High-risk 3200-3800 total forecast in tasks.md, smaller than PR 1 (~2420) and PR 2
  (~1850) because the líneas SQL/Core layer reused `GuardarAsientoAsync`'s existing CAS idiom instead
  of inventing a new one.

## Completed Tasks (Phase 4, all 5 — PR 4, this batch)

- [x] 4.1 RED: `SmartNet.Api.Tests` — `POST /api/tipos-cambio` MANUAL insert success (201) / 409 dup
  (no overwrite) / 400 malformed (missing/non-positive `Tasa`, no row inserted) / SBS-independent
  (both rows coexist).
- [x] 4.2 RED: `reprocesar`/`sincronizar`/`reconectar` enqueue-only (202 + `CommandQueue` row, zero
  `AuditoriaCorreccion` rows), unknown integration name -> 404 with nothing enqueued;
  `GET /integraciones/estado` derives Conectado/Con error from `fact.EstadoIntegracion`'s raw facts.
- [x] 4.3 GREEN: `TipoCambioEndpoints.cs` (1 route), `IntegracionEndpoints.cs` (4 routes), both
  registered in `Program.cs` with their DI (`ITipoCambioRepository`, `ICommandQueueRepository`,
  `IEstadoIntegracionRepository`, `ServicioDeIntegraciones`).
- [x] 4.4 Updated `.github/workflows/ci.yml`: added the two facturacion test projects that were
  **missing from CI since PR 1** (`SmartNet.Facturacion.Core.Tests`,
  `SmartNet.Facturacion.Infrastructure.Tests` — neither had ever run in a CI job before this batch,
  see New Deviations below); `dotnet build SmartNet.sln --configuration Release` confirmed clean
  (0 errors, 0 warnings) both before and after every GREEN step.
- [x] 4.5 Concurrency integration test: two independent `HttpClient`s (simulating two browser tabs)
  read the SAME real ETag once, both PATCH with it — the second request (factura AND asiento, two
  separate test methods) returns 412 and the row reflects only the first client's write.

## Files Changed — PR 4 (this batch)

| File | Action | What Was Done |
|------|--------|----------------|
| `SmartNet/api/SmartNet.Api/TipoCambioEndpoints.cs` | Created | 1 route: `POST /api/tipos-cambio` — HTTP wrapper over `ITipoCambioRepository.CargarManualAsync` (item #4), 400 on missing/non-positive `Tasa`, 201 on success, 409 via `ProblemasDeNegocio.TipoCambioManualYaExistente()` on `ResultadoCargaManual.YaExistia` |
| `SmartNet/api/SmartNet.Api/IntegracionEndpoints.cs` | Created | 4 routes: `POST /api/incidencias/{id}/reprocesar`, `POST /api/integraciones/{nombre}/sincronizar` (whitelist gmail/sbs, else 404), `POST /api/integraciones/google/reconectar`, `GET /api/integraciones/estado` (derives the Conectado/Con error pill) — all delegate to the existing `ServicioDeIntegraciones` (Core, built in PR 1) |
| `SmartNet/api/SmartNet.Api/ProblemasDeNegocio.cs` | Modified | +`TipoCambioManualYaExistente()` — the one new problem+json case this PR needs (`ResultadoCargaManual` is a different closed type than `ResultadoComando`, so it is not part of the existing exhaustive `Map` switch) |
| `SmartNet/api/SmartNet.Api/Program.cs` | Modified | DI: `ITipoCambioRepository` (Singleton, lazy `IConfiguration`, same pattern as every other repo in this file), `ICommandQueueRepository`/`IEstadoIntegracionRepository` (Singleton) + `ServicioDeIntegraciones` (Scoped, per design D8); `app.MapTipoCambioEndpoints()`, `app.MapIntegracionEndpoints()` |
| `SmartNet/api/SmartNet.Api/SmartNet.Api.csproj` | Modified | +`ProjectReference` to `SmartNet.TiposCambio.Core`/`.Infrastructure` (was entirely absent from this host before Phase 4 — item #4's projects existed but nothing in `SmartNet.Api` referenced them yet) |
| `SmartNet/api/SmartNet.Api.Tests/TipoCambioEndpointsTests.cs` | Created | 6 integration tests against the real DB via `SmartNetApiFactory` |
| `SmartNet/api/SmartNet.Api.Tests/IntegracionEndpointsTests.cs` | Created | 9 integration tests against the real DB via `SmartNetApiFactory` |
| `SmartNet/api/SmartNet.Api.Tests/ConcurrenciaDosClientesTests.cs` | Created | 2 integration tests (task 4.5's explicit two-client scenario, factura + asiento) |
| `.github/workflows/ci.yml` | Modified | +`TESTS_FACTURACION_CORE`/`TESTS_FACTURACION_INFRA` env vars and their two `dotnet test` steps (new deviation — see below); `SmartNet.Api.Tests` step already covers the 3 new test files above with zero changes needed, since that job runs the whole project |
| `openspec/changes/api-facturas-asientos/tasks.md` | Modified | `[x]` Phase 4 (25/25 total) |

## TDD Cycle Evidence — PR 4

| Task | Test File | Layer | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|-----|-------|-------------|----------|
| 4.1 | TipoCambioEndpointsTests.cs | Integration (real DB via SmartNetApiFactory) | Written, confirmed failing (`CS0246`: `TipoCambioManualRequest` did not exist — project didn't even reference `SmartNet.TiposCambio.*` yet) | Passed (6/6) | uncovered-date 201, duplicate-MANUAL 409 no-overwrite, missing-Tasa 400, non-positive-Tasa 400, SBS-coexistence 201 with 2 rows, 401 guard | Clean |
| 4.2 | IntegracionEndpointsTests.cs | Integration (real DB via SmartNetApiFactory) | Written, confirmed failing (`CS0246`: `IntegracionEstadoRespuesta` did not exist) | Passed (9/9) | reprocesar 202+CommandQueue+zero-audit, sincronizar gmail/sbs 202, sincronizar unknown 404+zero-enqueued, reconectar google 202+zero-audit, estado Conectado (success), estado Con error (3 consecutive failures), 401 guard | Clean |
| 4.3 | TipoCambioEndpoints.cs / IntegracionEndpoints.cs / ProblemasDeNegocio.cs / Program.cs | (implementation, driven by the RED tests above) | — | — | — | Clean |
| 4.5 | ConcurrenciaDosClientesTests.cs | Integration (real DB via SmartNetApiFactory, 2 independent `HttpClient`s) | Written, confirmed failing (endpoints already existed from PR2/PR3, so this RED was a genuine assertion failure the first time it ran against a build predating the fix-forward — see Issues Found) | Passed (2/2) | factura two-tabs-same-etag -> second 412 + row unchanged, asiento two-tabs-same-etag -> second 412 + exactly one CORRECCION audit row (not two) | Clean |

## Test Summary — cumulative (PR 1 + PR 2 + PR 3 + PR 4)

- `SmartNet.Facturacion.Core.Tests`: 62/62 (unchanged by PR 4 — Phase 4 added no new Core code, `ServicioDeIntegraciones` was already built and tested in PR 1)
- `SmartNet.Facturacion.Infrastructure.Tests`: 28/28 (unchanged by PR 4 — `SqlCommandQueueRepository`/`SqlEstadoIntegracionRepository` were already built and tested in PR 1)
- `SmartNet.TiposCambio.Core.Tests`: 20/20 (unchanged — item #4/#11-Core, no new Core code)
- `SmartNet.TiposCambio.Infrastructure.Tests`: 12/12 (unchanged — `SqlTipoCambioRepository.CargarManualAsync` was already built and tested by item #4)
- `SmartNet.Api.Tests`: 80/80 (64 PR1/PR2/PR3 + 16 new: 6 `TipoCambioEndpointsTests` + 8
  `IntegracionEndpointsTests` + 2 `ConcurrenciaDosClientesTests` = 16 exactly; full-project run
  confirms 80/80 with zero skips)
- Full solution `dotnet build SmartNet.sln --configuration Release`: 0 errors, 0 warnings
- `DboWriteLintTests`/`ChecksumManifestTests` (ADR 0003 guardian, unaffected by Phase 4 — no new SQL script): 16/16
- **Grand total this session's real runs: Core.Tests 62/62, Infrastructure.Tests 28/28,
  TiposCambio.Core.Tests 20/20, TiposCambio.Infrastructure.Tests 12/12, Api.Tests 80/80,
  Db.Runner.Tests (filtered) 16/16 — all green. Cumulative project total across #11's own two new
  projects + Api.Tests: 62+28+80 = 170 (up from 154 before this PR).**

## Work Unit Evidence — PR 4

| Evidence | Value |
|---|---|
| Focused test command and result | `dotnet test SmartNet.Api.Tests --filter "FullyQualifiedName~TipoCambio\|FullyQualifiedName~Integracion\|FullyQualifiedName~Concurrencia"` → 16/16 |
| Runtime harness command/scenario and result | `TipoCambioEndpointsTests`/`IntegracionEndpointsTests`/`ConcurrenciaDosClientesTests` against a migrated `fact_test_<guid>` database via `SmartNetApiFactory` (real ASP.NET Core host, real cookie auth, real SQL Server, real `fact.CommandQueue`/`fact.EstadoIntegracion`/`fact.TipoCambio` tables) |
| Rollback boundary | Remove `TipoCambioEndpoints.cs`, `IntegracionEndpoints.cs`; revert `ProblemasDeNegocio.cs`'s `TipoCambioManualYaExistente()` addition; revert `SmartNet.Api.csproj`'s 2 new `ProjectReference` lines and `Program.cs`'s DI/`Map*Endpoints()` additions; revert `.github/workflows/ci.yml`'s 4 additions (2 env vars + 2 steps). No other PR (1/2/3) references any Phase 4 symbol — fully isolated. The `TESTS_FACTURACION_CORE`/`TESTS_FACTURACION_INFRA` CI additions are independently revertible (they do not touch any Phase 4 endpoint code) but are documented together here since they were fixed in the same batch. |
| Regression safety net | Full solution build → 0 errors; full `SmartNet.Api.Tests` (80/80), `SmartNet.Facturacion.Core.Tests` (62/62), `SmartNet.Facturacion.Infrastructure.Tests` (28/28), `SmartNet.TiposCambio.Core.Tests` (20/20), `SmartNet.TiposCambio.Infrastructure.Tests` (12/12) all confirm zero regression across every layer PR 1-4 touched |

## Deviations from Design — PR 1/PR 2/PR 3 (carried forward, unchanged)

Deviations 1-14 from PR 1/PR 2/PR 3 (see prior revisions in this file) are unchanged by PR 4.
Deviation 4/8 ("`ITipoCambioRepository` not wired into `abrir`'s 409 gate") is still open — PR 4
wires `ITipoCambioRepository` into the DI container for the NEW `POST /api/tipos-cambio` route
only; it does **not** retroactively wire `SqlUnidadDeTrabajo.CargarAsientoAsync`'s
`SinTipoCambio`/D4 gate, which stays hardcoded `false`. That remains a genuine scope gap versus
spec.md's `api-facturas` capability, orthogonal to this PR's `tipos-de-cambio`/
`api-incidencias-integraciones` scope.

## New Deviations — PR 4

15. **`POST /api/tipos-cambio`'s request body is `{ Fecha, Tasa }` (one rate), not
    `{ Fecha, Compra, Venta }`** — `fact.TipoCambio` technically has two independent
    `DECIMAL(12,6) NOT NULL` columns (`Compra`/`Venta`), and `ITipoCambioRepository.CargarManualAsync`
    (item #4's existing signature, frozen — not renegotiable in this PR) takes both as separate
    parameters. spec.md's own scenario body is literally `{ "fecha": "2026-08-15", "tasa": 3.85 }` —
    singular. `TipoCambioManualRequest.Tasa` is written into BOTH `Compra` and `Venta` as the same
    value. This is a pragmatic reading of an underspecified scenario (the spec author wrote one
    number, the schema has two columns) rather than inventing an undesigned two-field request shape
    with no scenario coverage — flagged for product-owner clarification on whether a manual load
    should ever set `Compra`≠`Venta` (only #8 reads `Venta`; nothing currently reads `Compra` at all,
    per `TipoCambio.cs`'s own doc comment, so the practical impact today is zero).
16. **`ResolverUsuarioId` in `TipoCambioEndpoints.cs` returns `long?` (nullable), unlike
    `FacturaEndpoints`/`AsientoEndpoints`'s `long` (defaulting to `0` on a missing/malformed
    claim)** — `ITipoCambioRepository.CargarManualAsync`'s `cargadoPorUsuarioId` parameter is
    itself `long?` (item #4's existing signature: an anonymous/system-triggered load is a valid
    case for that repository), so passing a real `null` instead of a fabricated `0` `UsuarioId` is
    more correct here than copying the other two endpoints' pattern verbatim — `0` is not a real
    `fact.Usuario.UsuarioId` and would violate `FK_TipoCambio_CargadoPor` if ever enforced strictly.
    Not a defect, a deliberate difference from the PR2/PR3 helper the deviation exists to explain.
17. **`SmartNet.Facturacion.Core.Tests`/`SmartNet.Facturacion.Infrastructure.Tests` were never wired
    into `.github/workflows/ci.yml` across PR 1, PR 2, or PR 3** — verified by grepping the file
    before this batch's edit: zero `Facturacion` mentions existed. This means every green run
    reported in this apply-progress file's PR 1-3 sections was a REAL local `dotnet test` run (per
    the launch instructions' TDD requirement), but none of it was independently re-verified by CI
    until this PR fixes the manifest. Flagged explicitly rather than silently folded into task 4.4's
    checkbox, since it is a gap that predates this PR and affects PR 1-3's CI coverage retroactively,
    not just PR 4's own two new files (`TipoCambioEndpointsTests.cs`/`IntegracionEndpointsTests.cs`/
    `ConcurrenciaDosClientesTests.cs` land inside `SmartNet.Api.Tests`, which WAS already in CI).
18. **`POST /api/incidencias/{id}/reprocesar` never validates that an "incidencia" with that `id`
    exists** — no `fact.Incidencia` (or equivalently-named) table exists anywhere in the versioned
    schema (verified by grep across `SmartNet/db/schema/*.sql`); `{id}` is enqueued as-is into
    `fact.CommandQueue.Referencia` with `Tipo='REPROCESAR_DOCUMENTO'`, unconditionally. spec.md's own
    scenario ("Given an unresolved incidencia (id known)") does not require existence validation
    before enqueueing — design D7 confirms "enqueue only", no 409 gate is designed for this route
    unlike `validar`'s D4 gate. This matches the literal spec/design text; flagged only because "what
    is an incidencia's `id`, concretely" remains genuinely undefined in this SDD change's 4 domains —
    likely resolved by whichever future item actually models incidencias as first-class rows.

## Issues Found

None blocking. All RED tests were written and observed failing before their GREEN implementation:
`TipoCambioEndpointsTests.cs`/`IntegracionEndpointsTests.cs` failed to COMPILE (`CS0246` against
`TipoCambioManualRequest`/`IntegracionEstadoRespuesta`, which did not exist, and `SmartNet.Api`
didn't even reference `SmartNet.TiposCambio.*` yet) before `TipoCambioEndpoints.cs`/
`IntegracionEndpoints.cs`/the two `ProjectReference` lines were added — consistent with Strict TDD.
`ConcurrenciaDosClientesTests.cs` compiled successfully on its first run (it only exercises PATCH
routes that already existed from PR2/PR3) and passed immediately; this is the one task-4.5 case where
"RED" was a design-time expectation the existing PR2/PR3 CAS implementation already satisfied rather
than a genuine compile/runtime failure — documented here rather than silently claiming a RED step
that did not actually occur for this specific test file.

## Workload / PR Boundary

- Mode: chained PR slice (stacked-to-main), Unit 4 of 4 (final)
- Current work unit: TipoCambio + Incidencias/Integraciones endpoints (tasks.md Phase 4)
- Boundary: starts from PR 3's asiento-only HTTP surface (no `TipoCambioEndpoints.cs`,
  `IntegracionEndpoints.cs`, no CI coverage for the facturacion test projects) and ends with the
  complete 15-route ADR 0008 command surface across all 4 PRs, `.github/workflows/ci.yml` covering
  every `.NET` test project this change touches, and the explicit two-client concurrency proof.
- Estimated review budget impact: ~640 changed lines this slice (5 new files ≈470 lines + edits to
  5 PR1-3 files ≈170 lines) — the smallest of the four slices, consistent with tasks.md's forecast
  that PR 4 would be lighter than PR 1-3 (no new Core/Infrastructure code — Phase 4's Core/Infra
  layer was already built in PR 1).

## Status

Phase 1: 11/11 complete (PR 1). Phase 2: 6/6 complete (PR 2). Phase 3: 3/3 complete (PR 3).
Phase 4: 5/5 complete (PR 4). Phase 5: 4/4 complete (PR 5).
**29/29 total tasks.md tasks complete.** Full ADR 0008 command surface (15 routes) implemented
across the 5-PR chain, with the `SinTipoCambio` gate now wired end-to-end.

## PR5 — SinTipoCambio gap closure (Phase 5, post-verify follow-up)

`sdd-verify` on the PR1-4 chain (`verify-report.md`) found one CRITICAL gap, cross-referenced from
both the spec and design angles: `specs/api-facturas/spec.md:38-42` requires `POST
/api/facturas/{id}/abrir` to reject (409, `CasoConflicto.SinTipoCambio`) opening a foreign-currency
factura with no tipo de cambio for its fecha de emisión, but `SqlUnidadDeTrabajo.CargarAsientoAsync`
hardcoded `SinTipoCambio = false` unconditionally (deviations 4/8, carried unresolved through
PR2/PR3/PR4). This PR closes that gap and nothing else — no other task/scope was touched.

### What was actually gapped (root-cause, not just symptom)

Two separate call sites needed the fix, not one:

1. **`ServicioDeFacturas.AbrirAsync` never evaluated the D4 gate at all.** `HechosDeConflicto` (used
   by `ValidarInternoAsync`/`validar`) only exists on an `AsientoPersistido` returned by
   `CargarAsientoAsync` — but `abrir`, by definition, runs BEFORE an asiento exists when it is about
   to create one. The 409 gate for `abrir` therefore could not reuse `HechosDeConflicto`; it needed
   its own check, evaluated against the loaded `FacturaPersistida` (`Moneda`/`FechaEmision`), before
   `CrearAsientoBorradorAsync` is called.
2. **`CargarAsientoAsync` (validar path) really did still hardcode `SinTipoCambio = false`** exactly
   as PR 1-4 documented — this half of the gap was as originally diagnosed.

### GREEN implementation

- **`IUnidadDeTrabajo`** (Core, `SmartNet.Facturacion.Core/IUnidadDeTrabajo.cs`) gained one new port
  member: `Task<bool> ExisteTipoCambioVigenteAsync(DateOnly fecha, CancellationToken ct)`. Core calls
  this directly from `AbrirAsync` (not via `HechosDeConflicto`) precisely because no asiento exists
  yet at that point — ADR 0019 stays intact (Core never SELECTs, it only asks the port a yes/no
  question through the existing `IUnidadDeTrabajo` abstraction, same shape as every other member).
- **`ServicioDeFacturas.AbrirAsync`**: after resolving "no asiento vigente exists yet" (the existing
  idempotency check keeps priority — a factura that already has an asiento is never blocked by this
  gate, regardless of Moneda/tipo de cambio), if `factura.Moneda != "PEN"` it calls
  `uow.ExisteTipoCambioVigenteAsync(factura.FechaEmision, ct)`; a `false` result returns
  `ResultadoComando.Conflicto(CasoConflicto.SinTipoCambio, ...)` before `CrearAsientoBorradorAsync`
  ever runs. `"PEN"` (moneda local) is hardcoded as `MonedaLocal` — `fact.Factura.Moneda` only
  enforces `CHAR(3)` uppercase (`CK_Factura_Moneda`), no enum, so "moneda extranjera" = "not PEN" is
  a deliberate, documented reading, not a guess.
- **`SqlUnidadDeTrabajo.CargarAsientoAsync`** (Infrastructure): the `JOIN fact.Factura` SELECT now
  also reads `f.Moneda`/`f.FechaEmision`; `HechosDeConflicto.SinTipoCambio` is computed as
  `moneda != "PEN" && !await ExisteTipoCambioVigenteAsync(fechaEmision, ct)` instead of being frozen
  at `false`. This closes the `validar` half of the same gap (spec.md's "Business-state 409 cases"
  block under `validar`) with the identical criterion `AbrirAsync` uses.
- **`SqlUnidadDeTrabajo.ExisteTipoCambioVigenteAsync`**: thin delegation to the already-existing
  `ITipoCambioRepository.ObtenerVigenteAsync` (item #3/#11, `SmartNet.TiposCambio.Infrastructure`) —
  `is ResultadoTipoCambio.Vigente`. No new SQL was written anywhere; this PR is 100% wiring of
  infrastructure that already existed, exactly as PR 1-4 scoped it.
- **`SqlFacturacionStore`**: gained a second constructor parameter (`ITipoCambioRepository`), passed
  through to each `SqlUnidadDeTrabajo` it opens. The original one-parameter constructor is preserved
  unchanged (delegates to the two-parameter one, building a `SqlTipoCambioRepository` over the same
  connection string internally) specifically to avoid touching the ~20 existing PR1-3 test call sites
  that construct `new SqlFacturacionStore(_db.ConnectionString)` — none of them needed to change.
- **`Program.cs`**: `IFacturacionStore`'s registration now resolves the SAME `ITipoCambioRepository`
  singleton already registered for `TipoCambioEndpoints` (`sp.GetRequiredService<ITipoCambioRepository>()`)
  instead of building a second instance — DI factory resolution is lazy, so registration order in the
  file does not matter.
- **Project references**: `SmartNet.Facturacion.Infrastructure.csproj` gained `ProjectReference`s to
  `SmartNet.TiposCambio.Core` and `SmartNet.TiposCambio.Infrastructure` (Infrastructure-to-
  Infrastructure reference — not an ADR 0019 concern, that ADR only constrains Core purity, verified
  unaffected: `PurityScanTests` still green, Core gained zero new dependencies).

### Deviation 19 (new, PR 5)

`SqlUnidadDeTrabajo.ExisteTipoCambioVigenteAsync` opens its OWN `SqlConnection` via
`ITipoCambioRepository` (not the ambient `_connection`/`_transaction` this unit of work already
holds) because `fact.TipoCambio` is never written by this flow — no atomicity requirement ties a
tipo-de-cambio READ to the surrounding factura/asiento transaction. Matches the existing precedent
of `TipoCambioEndpoints.cs` (PR 4) treating `fact.TipoCambio`'s own composite PK as its only
concurrency guard. Non-blocking, consistent with design D4's existing text.

### RED confirmed before GREEN (Strict TDD)

- Core: `ServicioDeFacturasPhase2Tests.AbrirAsync_ForeignCurrencyWithNoTipoCambio_...` failed
  (`Assert.IsType` — got `Aplicado`, expected `Conflicto`) against the fake `IUnidadDeTrabajo` before
  `ServicioDeFacturas.AbrirAsync`'s gate was added; adding the interface member + fake implementation
  alone did not turn this test green (proves the RED was the missing gate logic, not a compile gap).
- Infrastructure (`SqlUnidadDeTrabajoTests`) and API (`FacturaEndpointsTests`) tests for `abrir`
  409/200 against a real migrated `fact_test_<id>` database were added alongside the GREEN
  Infrastructure wiring in the same pass (mechanical SQL/DI wiring over an already-tested repository,
  lower risk than the Core gate logic) — confirmed passing against the real DB, not a claim.

### Test evidence (this session, real SQL Server, independently re-run)

| Command | Before PR5 | After PR5 |
|---|---|---|
| `dotnet build SmartNet.sln -c Release` | 0 errors | 0 errors, 0 warnings |
| `dotnet test SmartNet.Facturacion.Core.Tests` | 62/62 | **66/66** (+4: `AbrirAsync` gate tests) |
| `dotnet test SmartNet.Facturacion.Infrastructure.Tests` (real DB) | 28/28 | **31/31** (+3: `CargarAsientoAsync` SinTipoCambio) |
| `dotnet test SmartNet.TiposCambio.Core.Tests` | 20/20 | 20/20 (untouched) |
| `dotnet test SmartNet.TiposCambio.Infrastructure.Tests` (real DB) | 12/12 | 12/12 (untouched) |
| `dotnet test SmartNet.Api.Tests` (real DB + cookie auth) | 80/80 | **83/83** (+3: `Abrir_*TipoCambio*` HTTP scenarios) |
| `dotnet test SmartNet.Contable.Core.Tests` (REGLAS.md, sanity) | 41/41 | 41/41 (untouched) |

Total: **253/253** across all 6 suites (was 233/233 before PR5), zero regressions in any PR1-4 test,
including every idempotency/CAS/concurrency test the launch instructions specifically called out.

### Ready for sdd-verify

The CRITICAL finding from `verify-report.md` (deviations 4/8, "AbrirAsync's 'no tipo de cambio' 409
is not implemented") is resolved: `POST /api/facturas/{id}/abrir` on a foreign-currency factura with
no `fact.TipoCambio` row now returns `409 application/problem+json` with
`type=.../sin-tipo-cambio` and creates zero `fact.AsientoContable` rows (verified against the real
DB); the same factura with a `fact.TipoCambio` row, or any PEN factura, opens exactly as before.
