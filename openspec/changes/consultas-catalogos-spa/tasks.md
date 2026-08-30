# Tasks: consultas-catalogos-spa (BACKLOG #22)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~2,570 authored (ADR 0021 excluded) |
| 400-line budget risk | Medium |
| Chained PRs recommended | Yes |
| Suggested split | PR1 -> PR2 -> ... -> PR9 (9 stacked) |
| Delivery strategy | exception-ok |
| Chain strategy | stacked-to-main |

Decision needed before apply: No
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
400-line budget risk: Medium

Per-slice estimates: PR1 ~250, PR2 ~230, PR3 ~320, PR4 ~330, PR5 ~380 (split export into PR5b if >400), PR6 ~330, PR7 ~330, PR8 ~360, PR9 ~40. Total ~2,570. No slice exceeds 400. `size:exception` ACCEPTED by owner. Each slice targets the previous slice's branch; PR1 targets `main`.

TDD is strict: every slice is RED (failing test) -> GREEN (implement) -> REFACTOR. No code before its test.

---

## PR1 — Export infrastructure (base: main) ~250 | satisfies api req 6,7

- [x] 1.1 RED: `SmartNet.Exportacion.Infrastructure.Tests/ExportadorXlsxTests.cs` — write via `ExportadorXlsx.Escribir(Stream,rows,columnas)`, reopen with `SpreadsheetDocument.Open`; assert header text, row count, date + decimal cells, empty-set still a valid workbook.
- [x] 1.2 RED: structural guard `NoRunnerReferenceGuardTests` pattern — assert no `*.Core` project references `DocumentFormat.OpenXml` (direct or transitive).
- [x] 1.3 GREEN: new `SmartNet/SmartNetApi/exportacion/SmartNet.Exportacion.Infrastructure` project + `ExportadorXlsx` (OpenXmlWriter, SAX rows, MemoryStream buffer, validation before first byte); pin `DocumentFormat.OpenXml` 3.x exact in the csproj (no `Directory.Packages.props`).
- [x] 1.4 GREEN: add both projects to the `.sln`; add `ProjectReference` from `SmartNet.Api.csproj`.
- [x] 1.5 Acceptance: `dotnet test` green for `SmartNet.Exportacion.Infrastructure.Tests`; `PurityScanTests` + structural guard green. Guardrail: no `dbo.*` write, no new SQL, no new GRANT.

## PR2 — API plan contable (base: PR1) ~230 | satisfies api req 4,6,8

- [x] 2.1 RED: `SmartNet.Api.Tests/CatalogoEndpointsTests.cs` — `GET /api/catalogos/plan-contable` 200 unpaged, camelCase `{cuenta,descripcion,nivel,esHojaImputable}`, `esHojaImputable` true iff `nivel IS NULL`, order `cuenta` asc, 401 unauth.
- [x] 2.2 RED: same test file — `GET /api/catalogos/plan-contable/exportacion` 200, `Content-Type` xlsx, `Content-Disposition: attachment` + `.xlsx`, body opens as workbook with rows = seeded filtered set + 1 header, honors `q`, 401 -> no file; hostile `?q=../..%0d%0aX:1` still yields constant filename.
- [x] 2.3 GREEN: `CuentaContableResultado` DTO beside `ProveedorResultado` (no new file); thin endpoint over existing `ListarPlanCompletoAsync`; `EsHojaImputable` projected not recomputed.
- [x] 2.4 GREEN: export endpoint — reuse port method, build rows, `Results.File(bytes, <xlsx mime>, fileDownloadName:$"plan-contable-{hoy:yyyy-MM-dd}.xlsx")`, `hoy` from `TimeProvider` singleton; server-side `q` predicate mirrors SPA contains-over-`cuenta|descripcion`.
- [x] 2.5 Acceptance: `dotnet test SmartNet.Api.Tests` green (172/172). Guardrail: no `dbo.*` write, no new versioned SQL, no new GRANT.

## PR3 — SPA shared chrome (base: PR2) ~320 | satisfies spa req 5, api req 6

- [x] 3.1 RED: `ui/tabla-paginador/tabla-paginador.spec.ts` — prev disabled on page 1, next disabled on last, `tamanio` change resets to page 1 and emits `tamanioChange`; renders `Página X de Y`.
- [x] 3.2 RED: `ui/orden.spec.ts` — pure toggle (asc<->desc, switch field resets asc) + arrow glyph selector.
- [x] 3.3 RED: `data-access/descarga-xlsx.spec.ts` — `http.get` with `responseType:'blob', observe:'response'`; reads `Content-Disposition` filename; calls `createObjectURL` then `revokeObjectURL`; `descargando` signal toggles.
- [x] 3.4 GREEN: `ui/tabla-paginador/` component (inputs `pagina/totalPaginas/tamanio/tamaniosDisponibles`, outputs `paginaChange/tamanioChange`; source-agnostic).
- [x] 3.5 GREEN: `ui/boton-exportar/` component (input `descargando`, output `exportar`, CSS-div green sheet glyph, no svg/img); `ui/orden.ts` pure module fns; `data-access/descarga-xlsx.ts` root service (anchor download, not `window.open`).
- [x] 3.6 GREEN: shared `.tabla-catalogo*` + `.tabla-catalogo__th--ordenable` CSS in `@layer primitives`, semantic tokens only; one module-level `Intl.Collator('es')`.
- [x] 3.7 Acceptance: SPA test runner green for new specs; `contraste.spec.ts` + `paleta.spec.ts` green; `angular.json` 6kB `anyComponentStyle` budget not breached.

## PR4 — SPA plan contable screen (base: PR3) ~330 | satisfies spa req 1,3,5; nav req

- [x] 4.1 RED: `data-access/*plan-contable*.service.spec.ts` — GETs full plan once; no request on filter/sort.
- [x] 4.2 RED: `feature/*plan-contable*` container spec — client-side filter + client-side column sort (no new request); `Exportar a Excel` calls `descarga-xlsx` with current `q`+sort.
- [x] 4.3 RED: `app.routes.spec.ts` — `catalogos/plan-contable` present (extend `arrayContaining` additively); `sidebar.spec.ts` — `nav-plan-contable` `<a>` active to `/catalogos/plan-contable`.
- [x] 4.4 GREEN: data-access signal service, presentational `ui/` table (`codigo`/`denominacion` <- API `descripcion`), typed `models/`, container owns filter/sort signals.
- [x] 4.5 GREEN: register `catalogos/plan-contable` as a sibling ShellLayout child with `authGuard`; activate sidebar `nav-plan-contable` link.
- [x] 4.6 Acceptance: SPA runner green incl `app.routes.spec.ts` + `sidebar.spec.ts`; no create/edit/delete control.

## PR5 — API proveedores catalogo mode (base: PR4) ~380 | satisfies api req 1,2,3,6,8

- [x] 5.1 RED: `SmartNet.Catalogos.Core` tests — `OrdenProveedor.Valores` = {proveedor,ruc,codigo}, `EsValido`.
- [x] 5.2 RED: infra tests (`TestDatabaseFixture`) — `totalRegistros` correct on page 1 AND page 3; out-of-range page -> `items []` + correct totals; 3 sort keys x 2 directions; `codpro` tiebreak stable ACROSS a page boundary; `rucpro` NULLs first ASC; `tamanio` whitelist {6,10,20,50}; `ListarCatalogoCompletoAsync` unpaged same order.
- [x] 5.3 RED: API tests — `modo=catalogo` lists all incl `P00000`, `PaginaBandeja` envelope `{items,pagina,tamanioPagina,totalRegistros,totalPaginas}`, server sort per field+direction, 400 x unknown `modo`/`orden`/`direccion`/`tamanio`, 401.
- [x] 5.4 RED: REGRESSION — `modo` absent/`picker` still `{resultados,hayMas}`, still excludes `P00000`, still empty for `q=a` (byte-frozen #18, zero-line diff in picker files).
- [x] 5.5 RED: `GET /api/catalogos/proveedores/exportacion` — headers, workbook rows = filtered set + header, honors `q`/`orden`/`direccion`, 401, hostile-`q` filename. (If PR5 > 400 lines, move 5.5 + 5.9 into PR5b, base PR5.)
- [x] 5.6 GREEN: `SmartNet.Catalogos.Core/OrdenProveedor.cs` (pure, `EstadoDerivadoBandeja` shape).
- [x] 5.7 GREEN: Core port members `ListarCatalogoAsync(...) -> PaginaProveedores` + `ListarCatalogoCompletoAsync(...) -> IReadOnlyList<Proveedor>`.
- [x] 5.8 GREEN: SQL adapter — `CAST(COUNT(*) OVER() AS INT)` in the paged SELECT, conditional fallback `COUNT(*)` for empty page; key -> compile-time constant column switch (`ruc->rucpro`, `codigo->codpro`); every ordering appends `, codpro ASC`. `TamanoPagina=20` picker constant untouched.
- [x] 5.9 GREEN: `modo` gate in endpoint (`picker`->`BuscarAsync` frozen, `catalogo`->new path, else 400); export route via `ExportadorXlsx`.
- [x] 5.10 Acceptance: `dotnet test` green (Core + Infra + Api); `PurityScanTests` green; no `dbo.*` index/write, no new SQL, no new GRANT (ADR 0003).

## PR6 — SPA proveedores screen (base: PR5) ~330 | satisfies spa req 1,2,5; nav req

- [x] 6.1 RED: `data-access/*catalogo-proveedor*.service.spec.ts` — sends `q/pagina/modo=catalogo/orden/direccion/tamanio`; consumes `PaginaBandeja<T>` fields; NEW service, does not touch picker `ProveedorService` state.
- [x] 6.2 RED: container spec — sortable headers -> server re-query + reset page 1; search -> server `q` + reset page 1, keeps sort; rows-per-page bound to `tamanioPagina`; footer `Anterior/Siguiente · Página X de Y`; `Exportar` sends current search+sort.
- [x] 6.3 RED: `app.routes.spec.ts` `catalogos/proveedores` additive; `sidebar.spec.ts` `nav-proveedores` `<a>` -> `/catalogos/proveedores`.
- [x] 6.4 GREEN: `CatalogoProveedorService` signal service; presentational table (`codigo`/`razón social`/`RUC`); container owns filter/paging/sort signals; wire `tabla-paginador` + `boton-exportar`.
- [x] 6.5 GREEN: register `catalogos/proveedores` sibling child + `authGuard`; activate `nav-proveedores`.
- [x] 6.6 Acceptance: SPA runner green incl routes + sidebar specs; query-only, no mutate control.

## PR7 — API tipo de cambio history (base: PR6) ~330 | satisfies api req 5,6,7,8

- [x] 7.1 RED: infra tests — `ListarHistoricoAsync(DateOnly,DateOnly,ct)` inclusive bounds, BOTH origins per date, unknown `Origen` filtered (`AND Origen IN ('SBS','MANUAL')`), empty -> `[]`; PK `(Fecha,Origen)` seek.
- [x] 7.2 RED: API tests — `GET /api/tipos-cambio?desde=&hasta=` both REQUIRED, 200 rows `{fecha,origen,compra,venta,fechaConsulta}` camelCase, `origen` serialized as string "SBS"/"MANUAL", order `fecha` then `origen`; 400 x5 (missing / unparseable / inverted / span > 366d); 401.
- [x] 7.3 RED: `GET /api/tipos-cambio/exportacion` — `desde`/`hasta` required, headers, workbook rows, shares range validation (400s), 401, hostile-`q` filename.
- [x] 7.4 GREEN: `ListarHistoricoAsync` read-only clock-pure port method on `ITipoCambioRepository`; private map returns `(OrigenTipoCambio)(-1)` for unknowns; PurityScanTests green.
- [x] 7.5 GREEN: SQL adapter; endpoint in `TipoCambioEndpoints` with range validation in the ENDPOINT not Core; explicit string mapper for `origen`; no #8 Venta-freeze; export route via `ExportadorXlsx`.
- [x] 7.6 Acceptance: `dotnet test` green (Infra + Api); `PurityScanTests` green; no `dbo.*` write, no new SQL, no new GRANT.

## PR8 — SPA tipo de cambio screen + sidebar 7->8 delta (base: PR7) ~360 | satisfies spa req 1,4,5; shell-nav delta

- [x] 8.1 RED: `data-access/*tipo-cambio*.service.spec.ts` — GET `desde/hasta`; default range = first-of-month -> today, LOCAL not UTC (via `shared/formato.ts` helper).
- [x] 8.2 RED: container spec — columns `fecha/origen/compra/venta`, both origins, no origin selector; client-side sort + slice; API 400 -> non-blocking validation message, no stale-as-current; `Exportar` for current range.
- [x] 8.3 RED: `sidebar.spec.ts` — exact ordered 8-entry list, exactly 5 `<a>`, 8 glyphs; `nav-tipo-cambio` in PRIMARY group after Plan contable -> `/catalogos/tipo-cambio`; docblock note "canvas has no TC entry; owner decision — do not restore 7". `app.routes.spec.ts` `catalogos/tipo-cambio` additive.
- [x] 8.4 GREEN: `shared/formato.ts` local-date helper; data-access signal service; presentational table; container owns range + sort signals.
- [x] 8.5 GREEN: register `catalogos/tipo-cambio` sibling child + `authGuard`; add `nav-tipo-cambio` link + 8th hand-built glyph folded into existing `.glifo--registro,.glifo--plan` bar rules.
- [x] 8.6 Acceptance: SPA runner green incl `sidebar.spec.ts` + `app.routes.spec.ts` + `contraste`/`paleta`; sidebar CSS under 6kB warn (refactor glyph CSS to shared rules if breached — do NOT raise budget); `angular.json` 6kB budget holds.

## PR9 — Integration harness (base: PR8) ~40 | satisfies api req 8

- [ ] 9.1 Re-run the `integration-spa-api` harness against the 3 new route families.
- [ ] 9.2 Manually append the 3 new routes (`/api/catalogos/plan-contable`, `/api/catalogos/proveedores?modo=catalogo`, `/api/tipos-cambio`) + their `/exportacion` variants to the harness report.
- [ ] 9.3 Acceptance: harness report shows the 3 route families PASS; no code change in this slice.

---

## Parallel vs sequential

All 9 slices are SEQUENTIAL (stacked). Within a slice, RED tasks may be authored in parallel; GREEN tasks follow their RED. PR3 (shared chrome) is the bottleneck for PR4/PR6/PR8; PR1 is the bottleneck for every API export (PR2/PR5/PR7). PR5 has the least line headroom — split `exportacion` into PR5b if the diff crosses 400.
