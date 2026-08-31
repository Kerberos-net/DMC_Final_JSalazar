# Tasks: Registro de compra en la SPA (solo lectura) — BACKLOG #23

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | 1,050–1,150 |
| 400-line budget risk | High |
| Chained PRs recommended | No (owner accepted size:exception) |
| Suggested split | Single PR |
| Delivery strategy | exception-ok |
| Chain strategy | n/a |

Decision needed before apply: No (resolved: size:exception)
Chained PRs recommended: No (owner accepted size:exception)
Chain strategy: size-exception
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | Core contracts + SQL adapter + endpoints + DI + contract tests | PR1 (size:exception) | `dotnet test SmartNet.Api.Tests`; `dotnet test SmartNet.Facturacion.Core.Tests` | `SmartNetApiFactory` + `TestDatabaseFixture` integration run | Remove `RegistroCompraEndpoints.cs`, `SqlRegistroCompraRepository.cs`, Core contracts, revert `Program.cs` DI line |
| 2 | SPA `registro-compra/` feature + route + specs | PR1 (size:exception) | `ng test --include='**/registro-compra/**/*.spec.ts'`; `npx tsc --noEmit` | `ng serve` manual period/expand/export flow | Remove `SmartNetWeb/src/app/registro-compra/**`, revert `app.routes.ts` + `formato.ts` `mesActual` |
| 3 | Shell-nav amendment | PR1 (size:exception) | `ng test --include='**/sidebar.spec.ts'` | `ng serve` manual sidebar nav | Revert `sidebar.ts`, `sidebar.spec.ts` |

## Phase 1: API Core Contracts (pure — no DB/HTTP/clock) — spec registro-compra-api req 4

- [x] 1.1 RED: `SmartNet.Facturacion.Core.Tests` — `PeriodoContable.TryParse` accepts `2026-08`, rejects `2026-13` / `agosto` / `2026-8` / null (spec assumption; D2)
- [x] 1.2 GREEN: CREATE `SmartNet.Facturacion.Core/RegistroCompra/PeriodoContable.cs` — `record(int Anio,int Mes)` + `static bool TryParse(string?, out PeriodoContable?)`, pure
- [x] 1.3 GREEN: CREATE response records in `SmartNet.Facturacion.Core/RegistroCompra/` — `RegistroCompraCabecera`, `LineaRegistro`, `RegistroCompraDetalle`, `PaginaRegistroCompra<T>` (5 wire fields, NOT reusing `PaginaBandeja<T>` — D1). Money/rate/numeroAsiento fields `decimal?`/`string?` (D4)
- [x] 1.4 GREEN: CREATE `SmartNet.Facturacion.Core/RegistroCompra/IRegistroCompraRepository.cs` — `ListarPeriodoAsync`, `ObtenerAsync(long)->RegistroCompraDetalle?`, `ListarPeriodoCompletoAsync`
- [x] 1.5 Verify: run existing `SmartNet.Facturacion.Core.Tests/PurityScanTests.cs` (no edit — assembly-wide scan, D-Purity) and confirm the new port is clean

## Phase 2: SQL Adapter (Infrastructure) — spec registro-compra-api req 1/2/4

- [x] 2.1 GREEN: CREATE `SmartNet.Facturacion.Infrastructure/RegistroCompra/SqlRegistroCompraRepository.cs` — ADO puro, connection-per-call, readonly connection-string field
- [x] 2.2 GREEN: `ListarPeriodoAsync` — SELECT joins `fact.AsientoContable` + `fact.Factura` + LEFT JOIN `dbo.Proveedor`; WHERE `f.Estado='VALIDADA' AND a.Estado<>'ANULADO'` + half-open `[@desde,@hasta)`; `COUNT(*) OVER()`; `ORDER BY FechaContable, NumeroAsiento, AsientoContableId`; `OFFSET/FETCH`; every filter a `SqlParameter`
- [x] 2.3 GREEN: `ObtenerAsync` — cabecera SELECT narrowed by `@id` re-applying the SAME predicate (404 side-channel guard, D3); lines SELECT `ORDER BY d.Orden`; both result sets via `ExecuteReaderAsync` + `NextResultAsync`; 0 cabecera -> null
- [x] 2.4 GREEN: `ListarPeriodoCompletoAsync` — unpaged variant of 2.2 for export (D7); `OrigenLibro` echoed verbatim (never `'02'`)

## Phase 3: API Endpoints + DI — spec registro-compra-api req 1/2/3/5

- [x] 3.1 RED: `SmartNet.Api.Tests/RegistroCompraEndpointsTests.cs` (`CatalogoEndpointsTests` style, real DB + real cookie) — 401 x3 routes; all fields camelCase
- [x] 3.2 RED: listado — period includes first/last day, excludes adjacent-month edge days; `DESCARTADA`/`PENDIENTE_VALIDACION`/`ANULADO` rows excluded; `proveedorNombre` null -> code only; `origenLibro` verbatim
- [x] 3.3 RED: listado — `PaginaRegistroCompra` envelope + `totalRegistros` via `COUNT(*) OVER()` across 2 pages, stable order across pages; empty period -> 200 `items:[]` `totalRegistros:0` (not 404)
- [x] 3.4 RED: listado — malformed/missing `periodo` (`2026-13`,`agosto`,`2026-8`,absent) -> 400 RFC 9457; confirm `tamanioPagina` allow-list `{6,10,20,50}` default 20 vs #22 (Open Question), out-of-list -> 400
- [x] 3.5 RED: detalle — happy `lineas[]` ordered by `orden`; ANULADO/non-VALIDADA/inexistente -> 404; qualifying asiento with 0 lines -> 200 `lineas:[]`
- [x] 3.6 RED: export — 200 with xlsx Content-Type + attachment Content-Disposition, same rows as listado; malformed `periodo` -> 400; filename-injection `periodo=2026-08%0D%0AX` -> 400 (D5, threat matrix)
- [x] 3.7 GREEN: CREATE `SmartNet.Api/RegistroCompra/RegistroCompraEndpoints.cs` — `GET /api/registro-compra`, `/{asientoId}`, `/export`; all `.RequireAuthorization()`; validate `periodo` then use parsed ints
- [x] 3.8 GREEN: export handler — `ExportadorXlsx.Escribir` (ADR 0021), columns = cabecera set, cell formatting (money F2 / rate F6 / null "" / date yyyy-MM-dd InvariantCulture), `Results.File(buffer, xlsx-mime, "registro-compra-{Anio:D4}-{Mes:D2}.xlsx")` (D5)
- [x] 3.9 GREEN: MODIFY `SmartNet.Api/Program.cs` — `AddSingleton<IRegistroCompraRepository>` lazy factory after the `ICuentaContableRepository` block
- [x] 3.10 Verify: `PermissionMatrixTests` stays green (read-only SELECT under existing 008 grants; no new SQL/GRANT — ADR 0003)

## Phase 4: SPA data-access + models — spec registro-compra-spa req 7

- [x] 4.1 RED: `registro-compra.service.spec.ts` — request params (`periodo`/`pagina`/`tamanioPagina`), envelope mapping, `cargando`/`error` transitions, error keeps no stale-as-current
- [x] 4.2 GREEN: CREATE `SmartNetWeb/src/app/registro-compra/models/registro-compra.model.ts` — contract types, money = `number | null`
- [x] 4.3 GREEN: CREATE `registro-compra/data-access/registro-compra.service.ts` — `providedIn:'root'`, private signals + `asReadonly()`, `firstValueFrom(http.get)`, `programar(delay)` coalescing (clone `CatalogoProveedorService`)
- [x] 4.4 RED: `registro-compra-detalle.service.spec.ts` — per-`asientoId` fetch, memoised `Map`, re-expand issues no 2nd request, cache cleared on periodo/page change
- [x] 4.5 GREEN: CREATE `registro-compra/data-access/registro-compra-detalle.service.ts`
- [x] 4.6 RED: `formato.spec.ts` — `mesActual(hoy)` returns local `${y}-${MM}`, incl. 31 Dec 23:00 local not rolling to January (never `toISOString`)
- [x] 4.7 GREEN: MODIFY `SmartNetWeb/src/app/shared/formato.ts` — add pure `mesActual(hoy = new Date())`

## Phase 5: SPA feature + UI — spec registro-compra-spa req 2/3/4/5/6

- [x] 5.1 RED: `registro-compra-tabla.spec.ts` badge truth table — cabecera formula `r2(base+igv)!==r2(neto)`; detalle formula `r2(sum debe)!==r2(sum haber)`; boleta `igv=0` base==neto no badge; exact-to-cent boundary `100.00+18.00` vs `118.01` badge / vs `118.00` none; any null term -> no badge, render `—`; percepción both-sides cancels -> no badge
- [x] 5.2 RED: `registro-compra-page.spec.ts` — default current LOCAL month load; change period -> re-query + reset page 1; server pagination via `tabla-paginador`; API 400 -> non-blocking validation msg; empty state when `totalRegistros:0`; loading indicator
- [x] 5.3 GREEN: CREATE `registro-compra/ui/registro-compra-tabla/` — presentational, OnPush, columns (numeroComprobante, origenLibro, numeroAsiento, proveedor, fechaContable, basePEN, igvPEN, netoPEN), expand toggle, badge `computed()` (no core import, no epsilon — D6)
- [x] 5.4 GREEN: CREATE `registro-compra/ui/asiento-detalle/` — read-only lines by `orden`, no edit/anular/reactivar control, empty -> "sin lineas contables"
- [x] 5.5 GREEN: CREATE `registro-compra/feature/registro-compra-page/` — container OnPush, signals `periodo`/`pagina`/`tamanioPagina`, owns expand state, wires services + `ui/tabla-paginador` + `ui/boton-exportar` + `data-access/descarga-xlsx` (reused as-is), export calls `GET /api/registro-compra/export?periodo=`
- [x] 5.6 RED: `app.routes.spec.ts` additive — authed loads `/registro-compra`, no-session redirects
- [x] 5.7 GREEN: MODIFY `SmartNetWeb/src/app/app.routes.ts` — lazy `loadComponent` child of `ShellLayout`, path `registro-compra`, `canActivate:[authGuard]`, before `catalogos/*` (D8)

## Phase 6: Shell-nav amendment — spec spa-shell-nav (DELTA)

- [x] 6.1 RED: MODIFY `SmartNetWeb/src/app/.../sidebar/sidebar.spec.ts` — inert loop array -> `['nav-errores','nav-sincronizacion']`; add routed assertion for `nav-registro` (href / router-link contains `registro-compra`); update file-header comment; 8-label / 1-divider / 8-glyph tests unchanged
- [x] 6.2 GREEN: MODIFY `sidebar.ts` — add `ruta: '/registro-compra'` to the `nav-registro` entry in `primarios` (glyph + label already exist)
- [x] 6.3 Note: `openspec/specs/spa-shell-nav/spec.md` is regenerated from the delta at archive time — do NOT hand-edit here

## Phase 7: Docs / harness — spec registro-compra-api req 5

- [x] 7.1 MODIFY `.claude/skills/integration-spa-api/SKILL.md` — add the `/api/registro-compra` (listado + detalle + export) flow manually (#22 precedent; the harness runs and reports, it does not author tests)

## Phase 8: Verification

- [x] 8.1 Run `dotnet test` (Api.Tests + Facturacion.Core.Tests) — all Phase 1/2/3 RED tests green, `PurityScanTests` + `PermissionMatrixTests` green
- [x] 8.2 Run `ng test` (registro-compra + sidebar specs) + `npx tsc --noEmit` — all Phase 4/5/6 RED tests green
- [x] 8.3 Confirm no change to `GET /api/asientos/{id}` / `api-asientos` / `shell-layout.*` (Decision 3 / design); confirm no new versioned SQL or GRANT
