# Tasks: API de facturas y asientos (BACKLOG #11)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~3200-3800 (2 new projects, 15 endpoints, 4 test layers, 1 schema file) |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 -> PR 2 -> PR 3 -> PR 4 |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending (ask user) |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | Core+Infrastructure scaffold, `TokenDeConcurrencia`, `ResultadoComando`, `CasoConflicto`, ports, adapters, `015_*.sql` | PR 1 | `dotnet test SmartNet.Facturacion.Core.Tests` | N/A — no HTTP surface yet, DB adapter tests use real test DB | Delete both new projects + revert `015_*.sql`; nothing else references them yet |
| 2 | `FacturaEndpoints` (PATCH/abrir/validar/descartar/adjuntos) + `ProblemasDeNegocio`/`IfMatch` | PR 2 | `dotnet test --filter Factura` | `SmartNet.Api.Tests` against real DB via `SmartNetApiFactory` | Remove `FacturaEndpoints.cs` + its `Map*` call in `Program.cs` |
| 3 | `AsientoEndpoints` (PATCH/líneas/reabrir/anular) | PR 3 | `dotnet test --filter Asiento` | `SmartNet.Api.Tests` against real DB | Remove `AsientoEndpoints.cs` + its `Map*` call |
| 4 | `TipoCambioEndpoints`, `IntegracionEndpoints`, `Program.cs` DI wiring, `SmartNet.sln`, `ci.yml` | PR 4 | `dotnet test --filter "TipoCambio|Integracion"` | `SmartNet.Api.Tests` against real DB | Remove both endpoint files + DI registrations; sln/CI entries are additive |

## Phase 1: Core scaffold (PR 1)

- [x] 1.1 RED: `TokenDeConcurrencia` round-trip tests (`Codificar`/`TryDecodificar`, malformed -> failure) in `SmartNet.Facturacion.Core.Tests`.
- [x] 1.2 GREEN: `TokenDeConcurrencia` pure static codec.
- [x] 1.3 RED: `ResultadoComando`/`CasoConflicto` enum-shape tests (one case per ADR 0008 409 row).
- [x] 1.4 GREEN: `ResultadoComando`, `CasoConflicto` in Core.
- [x] 1.5 RED: fake-`IUnidadDeTrabajo` tests for `ServicioDeFacturas`/`ServicioDeAsientos`/`ServicioDeIntegraciones` command sequencing (load -> compose -> invariants -> write -> commit order).
- [x] 1.6 GREEN: `IFacturacionStore`, `IUnidadDeTrabajo`, `ICommandQueueRepository` ports + services in Core.
- [x] 1.7 RED: `PurityScanTests` copy asserting `SmartNet.Facturacion.Core` has no `dbo.*`/Python/SQL dependency.
- [x] 1.8 GREEN: wire Core project into `SmartNet.sln`; confirm PurityScan passes.
- [x] 1.9 Create `SmartNet/db/schema/015_commandqueue_reconectar_google.sql` (`ALTER` `CK_CommandQueue_Tipo` adds `RECONECTAR_GOOGLE`, grants).
- [x] 1.10 RED: integration test — CAS write with stale version returns `ResultadoEscritura.VersionEnConflicto`; correlativo UPDLOCK increments once and never reuses on rollback.
- [x] 1.11 GREEN: `SqlFacturacionStore`, `SqlUnidadDeTrabajo`, `SqlCommandQueueRepository`, `SqlEstadoIntegracionRepository` in `SmartNet.Facturacion.Infrastructure`.

## Phase 2: Facturas endpoints (PR 2) — COMPLETE (6/6), see sdd/api-facturas-asientos/apply-progress

- [x] 2.1 RED: `SmartNet.Api.Tests` — `PATCH /api/facturas/{id}` matching/stale/missing If-Match -> 200/412/428.
- [x] 2.2 RED: `POST /abrir`, `/validar` (success, 422 descuadre, all 409 cases, gapless correlativo), `/descartar` (no audit) scenarios.
- [x] 2.3 RED: adjuntos POST/DELETE -> `DOCUMENTACION_ACTUALIZADA`/`ELIMINACION_ADJUNTO` scenarios.
- [x] 2.4 GREEN: `IfMatch.Requerido`, `ProblemasDeNegocio.Map(InvarianteIncumplida)` in `SmartNet.Api`.
- [x] 2.5 RED (Api unit): exhaustive `InvarianteContable` -> status/type enum-coverage test.
- [x] 2.6 GREEN: `FacturaEndpoints.cs` (7 routes), register in `Program.cs`.

## Phase 3: Asientos endpoints (PR 3) — COMPLETE (3/3), see sdd/api-facturas-asientos/apply-progress

- [x] 3.1 RED: `PATCH /api/asientos/{id}` If-Match cases; edit-without-reabrir -> 409.
- [x] 3.2 RED: líneas by `LineaId` survive reorder/delete; `reabrir` motivo-required/BORRADOR-409; `anular` terminal/already-anulado-409; `REPARTO_MANUAL` audit.
- [x] 3.3 GREEN: `AsientoEndpoints.cs` (6 routes), register in `Program.cs`.

## Phase 4: TipoCambio + Incidencias/Integraciones (PR 4)

- [x] 4.1 RED: `POST /api/tipos-cambio` MANUAL insert success/409 dup/400 malformed/SBS-independent.
- [x] 4.2 RED: `reprocesar`/`sincronizar`/`reconectar` enqueue-only, no audit, unknown name -> 404; `GET /integraciones/estado` derives pill.
- [x] 4.3 GREEN: `TipoCambioEndpoints.cs`, `IntegracionEndpoints.cs` (2 routes), register in `Program.cs`.
- [x] 4.4 Update `.github/workflows/ci.yml` for both new test projects; verify `SmartNet.sln` builds clean.
- [x] 4.5 Concurrency integration test: two clients same ETag, second `PATCH` -> 412, row untouched.

## Phase 5: SinTipoCambio gap closure (PR 5, post-verify follow-up)

Found by `sdd-verify` on the PR1-4 chain (see `verify-report.md`): `specs/api-facturas/spec.md:38-42`
requires `POST /api/facturas/{id}/abrir` to reject (409, `CasoConflicto.SinTipoCambio`) opening a
foreign-currency factura with no tipo de cambio for its fecha contable. `SqlUnidadDeTrabajo.CargarAsientoAsync`
hardcodes `SinTipoCambio = false` unconditionally (deviation 4/8 in `apply-progress.md`, carried
unresolved through PR2/PR3/PR4).

- [x] 5.1 RED: `SqlUnidadDeTrabajoTests`/`ServicioDeFacturasTests` (or equivalent) — factura en moneda
  extranjera, fecha contable sin fila en `fact.TipoCambio` (o `ITipoCambioRepository`) -> `CargarAsientoAsync`
  reporta `SinTipoCambio = true`.
- [x] 5.2 RED: `FacturaEndpointsTests` — `POST /api/facturas/{id}/abrir` en moneda extranjera sin tipo de
  cambio disponible -> `409` con `CasoConflicto.SinTipoCambio`; con tipo de cambio disponible o moneda
  local -> sigue succeeding idempotently (regression, no debe romper los tests PR2 existentes).
- [x] 5.3 GREEN: wire `ITipoCambioRepository` en `SqlUnidadDeTrabajo.CargarAsientoAsync` (o el punto que
  corresponda) para resolver `SinTipoCambio` real en vez del `false` hardcodeado; DI en `Program.cs` si
  falta.
- [x] 5.4 Update `apply-progress.md` (nueva sección PR5) y `verify-report.md` marcando el hallazgo CRITICAL
  como resuelto.
