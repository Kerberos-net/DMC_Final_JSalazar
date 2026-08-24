# Tasks: Bandeja e incidencias (BACKLOG #13)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~520-650 (DB ~40, ADR ~30, Core ~120, Infra ~180, Api ~90, SPA ~180) |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 (DB/permisos + ADR) -> PR 2 (Core + Infrastructure) -> PR 3 (Api) -> PR 4 (SPA) |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | Permission grant + indexes + ADR 0003 amendment | PR 1 | `dotnet test SmartNet/db/runner/SmartNet.Db.Runner.Tests --filter PermissionMatrixTests` | Real `TestDatabaseFixture` against local SQL Server instance | Revert `018_*.sql` (restores DENY) + revert ADR edit |
| 2 | `origen`/policy pure Core + widened repository/SQL | PR 2 | `dotnet test SmartNet/inbox/SmartNet.Inbox.Core.Tests` then `SmartNet.Inbox.Infrastructure.Tests --filter SqlBandejaRepositoryTests` | Infra tests run as `usr_api` against real schema (ADR 0019 level 2) | Revert `SqlBandejaRepository.cs`/`OrigenBandeja.cs`; `IBandejaRepository.cs` additive |
| 3 | `BandejaEndpoints.cs` param binding/validation | PR 3 | `dotnet test SmartNet/api/SmartNet.Api.Tests --filter BandejaEndpointsTests` | `WebApplicationFactory` in-memory | Revert endpoint file only, depends on PR 2 merged |
| 4 | SPA filters, panel de errores, reprocesar confirm flow | PR 4 | `npx vitest run --dir SmartNet/spa/src/app/inbox` | vitest + jsdom (component/service specs) | Revert `inbox/` SPA files only, depends on PR 3 merged |

## Phase 1: DB permisos e índices (PR 1)

- [x] 1.1 RED: extend `SmartNet/db/runner/SmartNet.Db.Runner.Tests/PermissionMatrixTests.cs` with a test asserting `usr_api` (`fact_api`) can `SELECT` on `fact.ProcesamientoError` and still gets denied on `INSERT/UPDATE/DELETE`.
- [x] 1.2 GREEN: create `SmartNet/db/schema/018_permiso_lectura_procesamiento_error.sql` — `REVOKE DENY SELECT`, `GRANT SELECT` on `fact.ProcesamientoError` to `fact_api`; keep explicit `DENY INSERT, UPDATE, DELETE`; add `IX_ProcesamientoError_ProcesamientoId`, `IX_InboxEvent_CreadoEn`, `IX_CommandQueue_Referencia (Tipo, Estado)`.
- [x] 1.3 Run `PermissionMatrixTests` and confirm the new scenario passes and prior DENY assertions are untouched.
- [x] 1.4 Amend `adrs/0003-particion-de-propiedad-de-datos-entre-net-y-python.md` to reclassify `fact.ProcesamientoError` as asymmetric-read (Python writes, both read), citing the `fact.Configuracion` precedent.
- [x] 1.5 Update `openspec/specs/esquema-y-permisos/spec.md` — new scenario: `usr_api` can SELECT but not write `fact.ProcesamientoError`.

## Phase 2: Core puro (PR 2, depends on Phase 1 permission grant existing in dev DB)

- [x] 2.1 RED: `SmartNet/inbox/SmartNet.Inbox.Core.Tests/OrigenBandejaTests.cs` — `origen` = `FACTURA` iff `EstadoConsumo=="PROMOVIDO" && FacturaId != null`, else `INCIDENCIA`.
- [x] 2.2 GREEN: create `SmartNet/inbox/SmartNet.Inbox.Core/OrigenBandeja.cs` with the pure derivation function.
- [x] 2.3 RED: extend `OrigenBandejaTests.cs` — default-view predicate (`EstadoConsumo='PENDIENTE'` OR >=1 error with `Clasificacion<>'OBSOLETO'`; `DESCARTADO`/error-free `PROMOVIDO` excluded).
- [x] 2.4 GREEN: add the default-view predicate function to `OrigenBandeja.cs`.
- [x] 2.5 RED: `OrigenBandejaTests.cs` — `PoliticaDeReprocesamiento.VentanaBloqueo` returns `null` when no pending `CommandQueue` row, else `MAX(CreadoEn)+5min` (pure, given inputs, no clock/DB).
- [x] 2.6 GREEN: add `PoliticaDeReprocesamiento` to `OrigenBandeja.cs` (or sibling file) per design D5.
- [x] 2.7 RED: `OrigenBandejaTests.cs` — `totalPaginas = ceil(totalRegistros/tamanioPagina)` envelope math, including `totalRegistros==0`.
- [x] 2.8 GREEN: add envelope math helper.
- [x] 2.9 Widen `SmartNet/inbox/SmartNet.Inbox.Core/IBandejaRepository.cs`: add `FiltrosBandeja`, `ErrorProcesamiento`, `PaginaBandeja<T>`, widen `BandejaItem`, change signature to `ListarAsync(FiltrosBandeja, ct)`.
- [x] 2.10 Run `PurityScanTests.cs` (ADR 0019 level 1) to confirm `OrigenBandeja.cs` stays infra-free.

## Phase 3: Infrastructure — SQL batch (PR 2)

- [x] 3.1 RED: extend `SmartNet/inbox/SmartNet.Inbox.Infrastructure.Tests/SqlBandejaRepositoryTests.cs` — `pagina<1`/non-numeric rejected by caller-level validation (covered again at API layer in Phase 4); repository-level: OFFSET/FETCH tiebreak stability with `InboxEventId` on duplicate `CreadoEn`.
- [x] 3.2 RED: `SqlBandejaRepositoryTests.cs` — `desde`/`hasta` filter, `hasta` inclusive boundary (`CreadoEn < hasta+1day`).
- [x] 3.3 RED: `SqlBandejaRepositoryTests.cs` — `proveedor` identity match on `FacturaCodigo`/`RucProveedor`, fallback to `JSON_VALUE(Payload,'$.comprobante.rucProveedor')` for non-promoted rows.
- [x] 3.4 RED: `SqlBandejaRepositoryTests.cs` — second result set returns N `ErrorProcesamiento` rows per `ProcesamientoId` with no row duplication of result set 1; row with no error history returns `errores: []`.
- [x] 3.5 RED: `SqlBandejaRepositoryTests.cs` — `reprocesarDisponibleEn` computed from `fact.CommandQueue` (`Tipo='REPROCESAR_DOCUMENTO'`, `Estado IN ('PENDIENTE','EN_PROCESO')`, within `@ventanaMinutos`); `null` when none pending.
- [x] 3.6 RED: `SqlBandejaRepositoryTests.cs` — empty page (`pagina>totalPaginas`) returns `items: []` with truthful `totalRegistros` via fallback `COUNT(*)`.
- [x] 3.7 RED: `SqlBandejaRepositoryTests.cs` — test runs `AS usr_api` proving the D1 grant via the engine, not mocked permissions.
- [x] 3.8 GREEN: rewrite `SmartNet/inbox/SmartNet.Inbox.Infrastructure/SqlBandejaRepository.cs` — one `SqlCommand` batch: `@pagina` table variable (filter+order+OFFSET/FETCH+`COUNT(*) OVER()`), result set 1 (bandeja rows), result set 2 (errors joined to `@pagina`), fallback `COUNT(*)` only when page empty and `pagina>1`.
- [x] 3.9 Run full `SqlBandejaRepositoryTests.cs` suite and confirm green. **Verified by orchestrator after agent crash: 40/40 passing, Core 49/49 passing.**

## Phase 4: Api — endpoint wiring (PR 3)

- [x] 4.1 RED: `SmartNet/api/SmartNet.Api.Tests/BandejaEndpointsTests.cs` — `pagina<1` or non-numeric returns `400 ProblemDetails`.
- [x] 4.2 RED: `BandejaEndpointsTests.cs` — `desde>hasta` returns `400 ProblemDetails`.
- [x] 4.3 RED: `BandejaEndpointsTests.cs` — valid `estado/desde/hasta/proveedor/pagina/orden` bind correctly into `FiltrosBandeja` and the envelope shape (`items, pagina, tamanioPagina, totalRegistros, totalPaginas`) is returned.
- [x] 4.4 RED: `BandejaEndpointsTests.cs` — empty filters exclude terminal (promoted/discarded) rows (`GetBandeja_DefaultView_ExcludesPromotedAndDiscardedRows`); the PENDIENTE-inclusion half of the default-view rule is covered at the Infra layer instead (`SqlBandejaRepositoryTests.ListarAsync_DefaultView_ExcludesTerminalRows_WhenEstadoIsOmitted`) — see Deviations note below.
- [x] 4.5 GREEN: update `SmartNet/api/SmartNet.Api/BandejaEndpoints.cs` to bind/validate the new query params and delegate to `IBandejaRepository.ListarAsync(FiltrosBandeja, ct)`; keep it a thin delegator (no combining logic in the endpoint).
- [x] 4.6 `openspec/changes/item-13-bandeja-incidencias/specs/inbox-screen/spec.md` already has no `estado=VALIDADO` reference — confirmed already amended, no change needed.

## Phase 5: SPA — filters, panel de errores, reprocesar (PR 4)

- [x] 5.1 RED: `SmartNet/spa/src/app/inbox/models/bandeja-item.model.ts` consumers — covered through `inbox.service.spec.ts`/`inbox-list.spec.ts` fixtures typed as `BandejaItem`, which force the discriminant (`origen: 'FACTURA'` requires `facturaId: number`; `'INCIDENCIA'` requires `facturaId: null`) at compile time; no separate runtime guard function exists since narrowing is a plain `origen` field check.
- [x] 5.2 GREEN: `bandeja-item.model.ts` rewritten with the discriminated union (`BandejaItemBase` + `origen`-narrowed variants), `ErrorProcesamiento`, `PaginaBandeja<T>`, `FiltrosBandeja`.
- [x] 5.3 RED/GREEN: `inbox.service.spec.ts` — `cargar()` takes a `FiltrosBandeja` object arg, passes `estado/desde/hasta/proveedor/pagina/orden` as query params only when set; `ultimosFiltros()` signal caches the last-used filters.
- [x] 5.4 RED/GREEN: `inbox.service.spec.ts` — `reprocesar(procesamientoId)` posts to `POST /api/incidencias/{id}/reprocesar`.
- [x] 5.5 GREEN: `SmartNet/spa/src/app/inbox/data-access/inbox.service.ts` rewritten per 5.3/5.4; also exposes `pagina/tamanioPagina/totalRegistros/totalPaginas` signals from the envelope.
- [x] 5.6 RED: `inbox-filter.spec.ts` extended — `desde`/`hasta`/`proveedor` inputs emit only on `change`/Enter (verified `input` event does NOT emit, only `change`/`keydown.enter` do).
- [x] 5.7 GREEN: `SmartNet/spa/src/app/inbox/ui/inbox-filter/inbox-filter.ts` (+ template) — new `desde`/`hasta`/`proveedor` inputs/outputs.
- [x] 5.8 RED: `SmartNet/spa/src/app/inbox/ui/confirmar-reproceso/confirmar-reproceso.spec.ts` — dumb component wraps native `<dialog>`, emits `confirmar`/`cancelar`, closed until `open()` invoked.
- [x] 5.9 GREEN: `confirmar-reproceso.ts` (+ template) created per design D6. **Deviation**: toggles the `<dialog>`'s `open` IDL property directly instead of calling `showModal()`/`close()` — jsdom 28 (this repo's vitest environment) implements the `open` attribute reflection but not those two methods; behaviorally equivalent non-modal open/close in a real browser too. Documented in the component's doc comment.
- [x] 5.10 RED: `panel-errores.spec.ts` — renders `Mensaje`/`Clasificacion`/`OcurridoEn` for `errores.length>0`, nothing for `[]`.
- [x] 5.11 GREEN: `panel-errores.ts` (+ template) created per design D8. Embedding inside `<details>` lives in `inbox-list.html` (the caller), not in `panel-errores` itself, matching D8's "renders nothing when empty" contract.
- [x] 5.12 RED: `inbox-list.spec.ts` extended — `<details>`+`app-panel-errores` renders for any `origen` with error history; `reprocesar-{inboxEventId}` button renders only when `errores.length>0`, disabled when `reprocesarDisponibleEn` is in the future, enabled when `null`; emits `reprocesarSolicitado` with `procesamientoId`. "Never renders an approve/edit/re-trigger control" assertion narrowed to explicitly excluded actions (`aprobar`/`editar`/`descartar` test ids) since `reprocesar` is now a deliberately allowed action (proposal Success Criteria amendment).
- [x] 5.13 GREEN: `inbox-list.ts` (+ template) updated — `reprocesandoId` input, `reprocesarSolicitado` output, `<details>` panel, one new action only; doc comment documents the read-only contract amendment.
- [x] 5.14 RED/GREEN: `inbox-page.spec.ts` — filter change handlers (`onEstadoChange`/`onDesdeChange`/`onHastaChange`/`onProveedorChange`) reset `pagina` signal to `null` (server default = page 1) before setting the new filter value in the same handler (single signal write batch per handler, effect fires once).
- [x] 5.15 RED/GREEN: `inbox-page.spec.ts` — reprocesar flow: click opens `confirmar-reproceso` dialog; confirm calls `InboxService.reprocesar` then `cargar(ultimosFiltros())`; cancel sends no request.
- [x] 5.16 RED/GREEN: `inbox-page.spec.ts` — `reprocesandoId` signal (optimistic, container-owned) disables the action immediately after confirm, independent of the server's `reprocesarDisponibleEn` until the refetch completes.
- [x] 5.17 GREEN: `SmartNet/spa/src/app/inbox/feature/inbox-page/inbox-page.ts` (+ template) — new filter signals, `pagina`, confirmation dialog wiring, `reprocesandoId`, refetch `effect()` (ADR 0009).
- [x] 5.18 `npx ng test --watch=false --include "src/app/inbox/**/*.spec.ts"` → 40/40 green (this repo's actual test command; `npx vitest run` alone lacks the Angular TestBed/jsdom setup `ng test`'s `@angular/build:unit-test` builder wires — see Deviations). Full SPA suite (`ng test --watch=false`, no filter) → 162/162 green, no regressions. `ng build` (production) → compiles clean.

## Phase 6: Verification

- [x] 6.1 `dotnet test` — `SmartNet.Inbox.Core.Tests` 49/49, `SmartNet.Inbox.Infrastructure.Tests` 41/41, `SmartNet.Api.Tests` 132/132, `SmartNet.Db.Runner.Tests` 134/134 (confirmed via a second full standalone run after fixing a real gap this caught: `018_permiso_lectura_procesamiento_error.sql`'s checksum was missing from `checksums.txt` — Phase 1 created the script but never ran `generate-checksums.ps1`; fixed by running it, one additive line). All four suites green, no regressions.
- [x] 6.2 `PurityScanTests.cs` — passes as part of the 49/49 `SmartNet.Inbox.Core.Tests` green run; `OrigenBandeja.cs`/`EnvelopeBandeja`/`PoliticaDeReprocesamiento` stayed infra-free (no DB/HTTP/clock imports) throughout.
- [x] 6.3 Manual/spec cross-check — combined filters (proved at Infra layer, `SqlBandejaRepositoryTests`), empty filters/default-view (Infra `ListarAsync_DefaultView_ExcludesTerminalRows_WhenEstadoIsOmitted` + Api `GetBandeja_DefaultView_ExcludesPromotedAndDiscardedRows`), proveedor no-match (Infra), out-of-range page (Infra `EmptyPage_ReturnsTruthfulTotalRegistros`), no-error-history row (SPA `panel-errores` + `inbox-list`), multi-error row (SPA `panel-errores`), pending-vs-expired timeout (SPA `inbox-list` enabled/disabled reprocesar button tests), cancel sends no request (SPA `inbox-page` cancel test). All covered by an automated test somewhere in the suite; no remaining manual-only gap identified.
