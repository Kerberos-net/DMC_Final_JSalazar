```yaml
schema: gentle-ai.verify-result/v1
evidence_revision: sha256:3a29eea9d63256976134a08a5d742753f72379305d9323653311a37d20b7d99d
verdict: pass_with_warnings
blockers: 0
critical_findings: 0
requirements: 3/3
scenarios: 9/9
test_command: "cd SmartNet/SmartNetWeb && npm test"
test_exit_code: 0
test_output_hash: sha256:a244e284589fc98492562243ed25b6340dc625dda4779d8c6fe4e9cb30e23fc9
build_command: "cd SmartNet/SmartNetWeb && npm run build"
build_exit_code: 0
build_output_hash: sha256:dd04bae2fe83e254b1816d2f5afc7f465db75e2da1b6e843fccc0c90879cf9ad
```

# Verification Report - consultas-catalogos-spa

Envelope reflects the most recent slice verified (PR6). PR1-PR5 sections retained below.

# PR6 (slice 6: SPA proveedores catalogo screen) - PASS WITH WARNINGS

Verified at HEAD f4e7767, branch feat/consultas-catalogos-spa-22-pr6. Scope = slice 6 of 9 only.
Slices 1-5 verified separately (PASS WITH WARNINGS). Slices 7-9 not implemented and NOT gaps.
Validator sdd-verify-validate --requirements 3 --scenarios 9 -> valid:true.

## Scope

In-scope catalog-queries-spa surface: proveedores portions of the guarded-lazy-routes requirement
(3 scenarios), the proveedores-screen requirement (5 scenarios), and the query-only-inbox-pattern
requirement (1 scenario) = 3 requirements / 9 scenarios. Plan contable and tipo de cambio belong to
other slices.

## Completeness - tasks 6.1 to 6.6 all done, match commits

RED d74a0d3 test-only: app.routes.spec.ts, catalogo-proveedor.service.spec.ts,
proveedores-page.spec.ts, proveedores-tabla.spec.ts, sidebar.spec.ts - 5 spec files, 420 ins, zero
production. Then GREEN f526b2b (+404 prod / -2), then checkboxes f4e7767. git diff vs pr5 = 15 files
/ 823 ins / 4 del; production delta about 404 LOC.

## Build / test evidence, independent re-run

- npm test (ng test / Vitest) -> 49 files / 443 tests passed, exit 0 (baseline 425, +18 PR6). Re-run twice, identical.
- npm run lint (tsc --noEmit) -> clean, exit 0.
- npm run build (production) -> complete, exit 0. proveedores-page chunk 7.00 kB raw / 2.41 kB
  transfer. NO anyComponentStyle budget warning.
- No coverage tool configured - coverage skipped, not a failure.

## Spec compliance - 3 requirements / 9 scenarios, all runtime-proven

### Guarded lazy catalog routes, proveedores portion - 3/3
- Routes resolve: app.routes.ts adds catalogos/proveedores as a sibling ShellLayout child, lazy
  loadComponent to ProveedoresPage; app.routes.spec.ts asserts defined + canActivate over 0 + loadComponent fn.
- Unauthenticated blocked: route carries canActivate authGuard.
- Additive: existing arrayContaining route and per-child guard assertions still green.

### Proveedores screen - paginated browse-all with search, sort, export - 5/5
- Initial paginated list: rows P00000 then P00012, h1 Proveedores, footer pag-estado Pagina 1 de 2;
  service requests modo=catalogo, pagina=1, orden=proveedor, direccion=asc, tamanio=20; binds all
  PaginaBandeja fields.
- Pagination navigates server pages: Siguiente -> GET pagina=2; page-size change -> exactly 1 GET
  tamanio=50 pagina=1 (double-emit coalesced by the single-setTimeout scheduler).
- Sortable header re-queries server-side, NOT client: orden-codigo click -> GET orden=codigo,
  direccion=asc, pagina=1; test flushes an order no client collator would produce (P00012 before
  P00000) and asserts rendered rows follow it exactly. proveedores-tabla renders filas verbatim,
  no client reorder (design D7).
- Search filters server-side: onFiltro acme -> GET q=acme, orden=ruc, pagina=1; debounced, trimmed,
  page reset, sort kept.
- Export downloads full filtered set: boton-exportar -> DescargaXlsx.descargar with
  /api/catalogos/proveedores/exportacion and q + orden + direccion; PR5 endpoint calls
  ListarCatalogoCompletoAsync (full set, no paging).

### Screens query-only and follow the inbox pattern, proveedores portion - 1/1
- No mutation affordance: zero crear/editar/eliminar/guardar testids; only GET + export GET.
  CatalogoProveedorService is a SEPARATE providedIn-root singleton; picker ProveedorService signals
  stay pristine (design D4). Container owns filter/orden signals; ui table is input/output only;
  models typed to the PR5 contract.

## Design / ADR compliance

D4, D6, D7, D8 honored. One documented behaviour-neutral choice: the single-setTimeout scheduler
doubles as debounce + burst coalescer; irAPagina early-returns when target page equals the
already-requested page. Covered by dedicated tests.

## TDD compliance, Strict TDD active - PASS

- RED confirmed: d74a0d3 provably test-only (git show --stat: 5 spec files, 0 production); target
  modules did not exist there, so specs failed at import resolution. Genuine RED.
- GREEN confirmed: all 18 new tests pass on independent re-run (443/443).
- Triangulation strong: service 6 cases, page 8 cases, table 3 cases; distinct expected values.
- Safety net: sidebar.spec.ts and app.routes.spec.ts pre-existing green; full 49-file suite green
  post-change; picker specs untouched.
- Assertion quality: all assertions verify real behavior; the 500-case asserts error banner role
  AND 0 rows; no tautology, ghost loop, smoke-only, or mock-heavy file.

## Test layer distribution

Unit/component (jsdom + HttpTestingController): 18 new tests across 5 files (Vitest). Integration
browser: 0 not installed. E2E: 0 not installed.

## Issues

CRITICAL: none.

WARNING, none blocking archive:
1. Pre-existing prettier 3.9.6 trailing-comma drift - flags new PR6 files AND untouched
   plan-contable files identically; effective style gate is tsc --noEmit (no format-check, no
   ESLint) and it passes. NOT a PR6 regression.
2. size:exception - 823 ins vs pr5 (about 404 prod LOC), over the 400-line budget; owner-accepted
   feature-level exception, consistent with PR3/PR4/PR5.

SUGGESTION:
1. GREEN commit f526b2b touched catalogo-proveedor.service.spec.ts with a one-line matcher fix
   (request.params.get to params.get) - harness-API correction, not an assertion softening.
2. Prompt export URL wording mentions modo=catalogo; the SPA sends only q/orden/direccion, which is
   correct because the PR5 /exportacion endpoint does not accept modo.
3. Sidebar still 7 entries / 7 glyphs, nav-proveedores and nav-plan-contable both anchor tags;
   Tipo de cambio deferred to PR8; canvas-replica docblock note intact.

## Verdict

PASS WITH WARNINGS. Slice 6 correct and complete against its 6 tasks; 3 in-scope requirements / 9
scenarios runtime-proven by 18 new strict-TDD tests; server-side sort genuinely proven; picker
contract untouched and non-regressed (443/443). Nothing blocks archiving slice 6.
Next: sdd-apply PR7 (API tipo de cambio history, base feat/consultas-catalogos-spa-22-pr6).

---
# Verification Report - consultas-catalogos-spa

Envelope above reflects the most recent slice verified (PR5). PR1-PR4 sections retained below.

# PR5 (slice 5: API proveedores modo=catalogo + /exportacion) - verified at HEAD 273e6e0, branch feat/consultas-catalogos-spa-22-pr5

Scope: slice 5 of 9 ONLY. Slices 1-4 verified separately (PASS WITH WARNINGS). Slices 6-9 are intentionally not implemented and are NOT gaps.

## Completeness - tasks 5.1-5.10 all [x], match commits

RED eaebb5b (test-only: 3 test files, +387 insertions, zero production code) then GREEN 0dc990c (+265 prod / -6) then checkboxes 273e6e0.

- 5.1/5.6 OrdenProveedor.cs: pure static, Valores = {proveedor,ruc,codigo}, EsValido(string?). Same shape as EstadoDerivadoBandeja. No DB/HTTP/clock.
- 5.2/5.7/5.8 SqlProveedorRepository: PaginaProveedores record + ListarCatalogoAsync / ListarCatalogoCompletoAsync port members added; BuscarAsync + TamanoPagina=20 picker constant byte-unchanged. FiltroCatalogo lists all incl P00000 on blank q (DBNull). OrdenSql is a closed switch mapping key to a compile-time constant column (ruc to rucpro, codigo to codpro, else proveedor) plus constant ASC/DESC plus a codpro ASC tiebreak, dropped only when the primary column is codpro.
- 5.3/5.9 CatalogoEndpoints.BuscarProveedoresAsync: gains modo/orden/direccion/tamanio. modo null or picker keeps the existing BuscarAsync path and the resultados/hayMas shape byte-frozen (sort params ignored); modo catalogo validates orden (OrdenProveedor.EsValido), direccion (asc/desc), tamanio (6/10/20/50) with 400 on any invalid, then ListarCatalogoAsync then CatalogoProveedoresRespuesta with items/pagina/tamanioPagina/totalRegistros/totalPaginas; any other modo returns BadRequest.
- 5.5 GET /api/catalogos/proveedores/exportacion: validates orden/direccion (400), ListarCatalogoCompletoAsync (no paging), ExportadorXlsx.Escribir, Results.File xlsx-mime with proveedores-DATE.xlsx where DATE comes from the injected TimeProvider - NO user input in Content-Disposition.
- 5.10 Acceptance: Core + Infra + Api + Exportacion suites green; PurityScanTests green; no dbo write/index, no versioned SQL, no GRANT.

git diff --stat vs feat/consultas-catalogos-spa-22-pr4 = 6 files, 632 insertions / 6 deletions. Authored production delta only 245 LOC; the rest is strict-TDD test coverage plus doc-comment blocks. Feature-level size:exception owner-accepted in design; the 5b export split was offered and declined. See WARNING 3.

## Build / Test evidence (independent re-run this session, local SQL Server available)

- dotnet test SmartNet/SmartNetApi/api/SmartNet.Api.Tests -> Superado 191, Con error 0, Omitido 0, Total 191 (3 m 50 s), exit 0. PR2 baseline was 172; PR5 adds 19 theory-expanded cases. Nothing else in the shared suite regressed - the picker path still green.
- dotnet build SmartNet/SmartNetApi/api/SmartNet.Api -> Compilacion correcta, 0 Advertencias, 0 Errores, exit 0.
- dotnet test SmartNet/SmartNetApi/catalogos/SmartNet.Catalogos.Core.Tests -> 41/41 green (incl PurityScanTests), exit 0.
- dotnet test SmartNet/SmartNetApi/catalogos/SmartNet.Catalogos.Infrastructure.Tests full suite not filtered -> 81/81 green (1 m 53 s), exit 0. The 13 pre-existing BuscarAsync/Obtener/BuscarPorRuc cases plus the new ListarCatalogo cases plus the partition/permission structural guards all pass.
- dotnet test SmartNet/SmartNetApi/exportacion/SmartNet.Exportacion.Infrastructure.Tests -> 4/4 green (incl NoCoreReferencesOpenXmlGuardTests), exit 0.
- No test recorded as not run (no DB) - every infra/API case ran against real fact_test databases.

## Spec compliance (slice 5 in-scope grain) - 6 requirements / 14 scenarios, all runtime-proven

### Requirement: Proveedores endpoint serves both picker and browse-all modes - 5/5 scenarios PASS
- Catalogo mode lists every proveedor including P00000: PASS - ListsEveryProveedorInclP00000_WithPaginaBandejaEnvelope seeds P00000 plus 2 rows, asserts items contains P00000, envelope fields populated, null ruc surfaces as JSON null. Infra ListarCatalogoAsync_IncludesP00000 confirms at the SQL layer.
- Catalogo mode text filter: PASS - CatalogoMode_TextFilter_MatchesNameRucOrCode (only ACME PERU for q=ACME); filter is proveedor OR rucpro OR codpro LIKE.
- Picker mode is unchanged: PASS - PickerMode_Unchanged_ExcludesP00000_KeepsResultadosShape (Theory: modo absent AND modo=picker) asserts the resultados array excludes P00000, hayMas present, items property ABSENT. PickerMode_ShortQuery_StillEmpty_EvenWithSortParams proves q=a returns empty even with sort params supplied. The picker code path never forwards sort to BuscarAsync. SqlProveedorRepository.BuscarAsync source byte-unchanged.
- Unknown mode rejected: PASS - CatalogoMode_BadRequest_OnUnknownParams (Theory) covers modo=desconocido plus orden=nombre, direccion=arriba, tamanio=7, all 400.
- Unauthenticated: PASS - CatalogoMode_WithoutACookie_Returns401 (RequireAuthorization; 401 before any query).

### Requirement: Catalogo mode returns the PaginaBandeja envelope - 2/2 scenarios PASS
- Pagination envelope is accurate: PASS - CatalogoMode_PaginationEnvelopeIsAccurate (45 rows, page 2, size 20 gives 20 items, first item PAG 020, pagina 2, totalRegistros 45, totalPaginas 3). Infra TotalRegistros_IsFullFilteredCount_OnPage1AndPage3 asserts totalRegistros 45 on BOTH page 1 and page 3.
- Page past the end: PASS - infra PagePastTheEnd_ReturnsEmptyItems_WithCorrectTotals (10 rows, page 9 gives empty items, totalRegistros 10, totalPaginas 1). Exercises the conditional fallback count result set.
- totalRegistros source: VERIFIED by source inspection - single SELECT with CAST(COUNT(*) OVER() AS INT) AS TotalRegistros in the same paged pass. The only second statement is a guarded IF nroPagina greater than 1 AND NOT EXISTS then SELECT COUNT(*), read in C-sharp ONLY when the page came back empty - never a second scan on a populated page. No dbo.Proveedor name index added.

### Requirement: Catalogo mode supports server-side sort - 3/3 scenarios PASS
- Sort by RUC descending: PASS - endpoint CatalogoMode_ServerSort (Theory: codigo asc/desc, proveedor desc, ruc desc) plus infra ServerSort_PerKeyAndDirection (Theory: 3 keys x 2 directions). Sort applied across the full filtered set before OFFSET/FETCH.
- Invalid sort field rejected: PASS - orden=nombre gives 400 (BadRequest theory plus OrdenProveedorTests.EsValido_IsFalse_ForAnythingElse including an injection-style string).
- Picker mode ignores sort: PASS - see picker scenario above; picker handler does not pass sort to BuscarAsync.
- No user string reaches SQL text: VERIFIED - OrdenSql is a closed switch returning compile-time string constants; direccion resolves to literal ASC or DESC; the query is otherwise fully parameterised. OrdenProveedor.EsValido gates at the endpoint before the adapter is called.

### Requirement: Excel export endpoint per catalog - proveedores portion - 2/2 in-scope scenarios PASS
- Proveedores export reflects the active filter: PASS - Exportacion_Returns200_XlsxHeaders_WorkbookRows_HonorsQAndSort (3 seeded, q=EXPORT ACME gives workbook with 3 rows = 1 header + 2 filtered), Excel media type, attachment plus .xlsx Content-Disposition. Full set via ListarCatalogoCompletoAsync (no paging).
- Unauthenticated export: PASS - Exportacion_WithoutACookie_Returns401_AndNoFile (401, body is not the xlsx media type).
- Filename carries NO user input: PASS - Exportacion_HostileQuery_FilenameStaysConstantForm (hostile q with CRLF gives no injected token, no CR, no LF, matches filename proveedores-DATE.xlsx). Filename is a constant plus a date from the injected TimeProvider.
- Exportacion_BadRequest_OnUnknownSort (orden=nombre gives 400) also passes.
- TC export scenarios are out of scope for PR5 (slice 7/8).

### Requirement: Read-only, partition-respecting access - 1/1 scenario PASS
- No writes, no schema drift: PASS - source inspection: the two catalogo repo methods issue only SELECT over dbo.Proveedor; no fact tables touched; git diff adds no SQL script and no GRANT. Infrastructure partition/permission structural guards remain green (81/81). OrdenProveedor is pure - PurityScanTests green (41/41). NoCoreReferencesOpenXmlGuardTests green - export delegates from the API endpoint to SmartNet.Exportacion.Infrastructure, no Core project sees OpenXml.

### Requirement: Contract-test coverage - 1/1 scenario PASS
- Contract suite runs: PASS - every proveedores clause listed in the requirement is asserted in CatalogoEndpointsTests and passes (191/191). Harness re-run is slice 9.

## Design / ADR compliance

- ONE route, TWO modes selected by explicit modo (design D1): PASS.
- PaginaBandeja-shaped envelope, field names mirror the inbox envelope, Catalogos.Core does not reference Inbox.Core (design D6): PASS.
- COUNT(*) OVER() in the same pass, mirrors SqlBandejaRepository design D4, conditional fallback for the empty out-of-range page: PASS.
- Sort key to compile-time constant column, user text never an identifier (design D7): PASS.
- codpro ASC unique tiebreak on every ordering (design D7): PASS with one documented, behaviour-neutral deviation - the tiebreak is omitted when the primary sort column IS codpro (orden=codigo), because SQL Server rejects a column named twice in ORDER BY and codpro is already the unique key in that case. Verified safe: the codigo asc/desc sort theory passes.
- Cross-page-boundary stability: VERIFIED as a real boundary test - CodproTiebreak_IsStableAcrossAPageBoundary_WhenNameRepeats seeds 10 rows ALL with the same proveedor name and rucpro NULL (both the sort column and the nullable non-unique column fully degenerate), pages size 4, concatenates pages 1+2+3 and asserts exactly T00000 through T00009 with a distinct count of 10 (no drop, no dup). RucproNullsSortFirst_Ascending additionally pins NULLs-first ASC.
- No user input in Content-Disposition (ADR 0021 decision 4): PASS.
- ExportadorXlsx.Escribir reused unchanged (PR1 D9): PASS.
- Deviations from design: none material beyond the codpro-tiebreak omission above. Unspecified and not load-bearing: export column headers Codigo / Razon social / RUC.

## TDD Compliance (Strict TDD active)

| Check | Result | Details |
|-------|--------|---------|
| TDD Evidence reported | PASS | apply-progress #243 has a full TDD Cycle Evidence table for 5.1-5.10 with verbatim command results. |
| All tasks have tests | PASS | OrdenProveedorTests 9, SqlProveedorRepositoryTests +11 ListarCatalogo cases, CatalogoEndpointsTests +15 cases / +208 lines. |
| RED confirmed (test-only commit) | PASS | eaebb5b is test-only - git show --stat shows only the 3 test files, +387 insertions, no production file. New specs reference not-yet-existing OrdenProveedor (CS0103) and ListarCatalogoAsync / PaginaProveedores (CS1061), and expect items where the picker path returns resultados - genuine compile plus assertion RED. |
| GREEN confirmed | PASS | 191 API + 41 Core + 81 Infra + 4 Exportacion on independent re-run this session, all exit 0. |
| RED-before-GREEN ordering | PASS | eaebb5b test, then 0dc990c feat, then 273e6e0 checkboxes. |
| Triangulation adequate | PASS | OrdenProveedor: exact set + true-per-key + false for null/empty/uppercase/unknown/injection. Infra: totalRegistros on page 1 and 3, out-of-range page, P00000 inclusion, blank q, 3x2 sort matrix, cross-page tiebreak, NULLs-first, tamanio whitelist, unpaged parity. Endpoint: envelope, 45-row pagination, filter, sort theory, 4-way 400 theory, 401, picker regression, export. Distinct expected values throughout. |
| Safety net for modified files | PASS | CatalogoEndpointsTests.cs and SqlProveedorRepositoryTests.cs modified not new - full 191 and 81 suites green after. BuscarAsync and the picker constant byte-unchanged; the 13 pre-existing infra cases green. |

## Assertion quality

All assertions verify real behavior: exact HTTP status codes, exact codigo and nombre ordered arrays, envelope integer fields, JSON null for a null ruc, a check that the picker shape exposes no items property, Excel media-type string, regex on Content-Disposition, reopening the real workbook to count rows, distinct count for cross-page row identity. The empty-items assertions in the page-past-end tests are paired with totalRegistros companion assertions (not orphan empty checks). No tautologies, no ghost loops (theories iterate fixed inline data), no smoke-only tests, no CSS or implementation-detail coupling, no mocks. Assertion quality: 0 CRITICAL, 0 WARNING.

## Test Layer Distribution

| Layer | Tests (PR5) | Tools |
|-------|-------------|-------|
| Unit (pure) | 9 | xUnit |
| Infra (real DB, TestDatabaseFixture) | 14 ListarCatalogo cases (81 file total) | xUnit + SQL Server |
| API contract (WebApplicationFactory + real DB + cookie) | 19 theory-expanded (191 suite total) | xUnit + WebApplicationFactory |
| E2E | 0 | not applicable |

## Issues

CRITICAL: none.

WARNING (none blocking archive of slice 5):
1. codpro tiebreak omitted for orden=codigo. Behaviour-neutral (codpro is already the unique key so the ordering is fully deterministic) and covered by the passing codigo asc/desc sort theory, but it deviates from the design wording that every ordering appends codpro ASC. apply-progress documents it; flagged so a reviewer confirms the reasoning.
2. Export column headers Codigo / Razon social / RUC and the empty-string substitution for a null RUC are unspecified choices by apply. Not load-bearing, but the SPA proveedores export (PR6) should be checked against these labels for consistency.
3. size:exception magnitude - PR5 is +632 insertions / 6 files vs the ~380-line slice estimate. Owner-accepted at feature level; production delta is only 245 LOC and the 5b split was deliberately declined. Reviewer load is real; no coverage was cut.

SUGGESTION:
1. direccion is validated case-sensitively (asc/desc Ordinal) at the endpoint but OrdenSql compares desc with OrdinalIgnoreCase - harmless (validation gates first) but the two could share one comparison.
2. modo matching is case-sensitive (picker/catalogo Ordinal); modo=Catalogo yields 400. Consistent with the spec but worth an explicit note if the SPA ever sends a different case.
3. No coverage tool wired into dotnet test; changed-file coverage not reported. Consider XPlat Code Coverage in CI for the catalogos feature.
4. The catalogo-mode q is passed raw to the repository (trimmed internally). A shared trim helper across BuscarAsync and ListarCatalogoAsync would keep the two filter predicates aligned.

## Verdict

PASS WITH WARNINGS - slice 5 is correct and complete against its 10 tasks. All 6 in-scope requirements (14 scenarios) are runtime-proven by passing DB-backed tests: the dual-mode route keeps the BACKLOG #18 picker contract byte-frozen, catalogo mode returns the PaginaBandeja envelope with totalRegistros from COUNT(*) OVER() in a single pass (verified by source inspection), server-side sort maps through a closed compile-time switch with no user string reaching SQL text, the cross-page codpro tiebreak test genuinely seeds a fully degenerate sort key across 3 pages, and the exportacion route emits a real full-set xlsx with a user-input-free filename. Read-only and partition guardrails hold. Strict TDD genuinely followed: eaebb5b is a test-only RED commit with genuine compile failures that precedes GREEN 0dc990c. Build clean; API 191/191, Core 41/41, Infra 81/81, Exportacion 4/4 - nothing in the shared suite regressed. Warnings are a behaviour-neutral tiebreak deviation, unspecified export labels, and size-exception magnitude - none block archiving slice 5.

Validator: gentle-ai sdd-verify-validate --input <report> --requirements 6 --scenarios 14.

---

# Verification Report - consultas-catalogos-spa

Envelope above reflects the most recent slices verified (PR3 + PR4). PR1 and PR2 sections retained below.

# PR3 + PR4 (slices 3-4: SPA shared chrome + SPA Plan Contable screen) - verified at HEAD bb25f63, branch feat/consultas-catalogos-spa-22-pr4

Scope: slices 3 and 4 of 9 ONLY. Slices 5-9 are intentionally not implemented and are NOT gaps. Slices 1-2 verified separately (PASS WITH WARNINGS).

## Completeness - tasks 3.1-3.7 and 4.1-4.6 all [x], match commits

PR3 (RED 5680df6 test-only / GREEN 1f11368 / checkboxes 25ca063):
- 3.1 tabla-paginador: source-agnostic pagination chrome, inputs pagina/totalPaginas/tamanio, outputs paginaChange/tamanioChange, no HttpClient. Rows-per-page change emits tamanioChange then paginaChange(1).
- 3.2 orden.ts: pure module functions alternarOrden / flechaOrden / ordenarPor; ONE module-level Intl.Collator es numeric base.
- 3.3 descarga-xlsx.ts: DescargaXlsx root service; blob GET with observe response, filename from Content-Disposition (RFC 5987 extended + quoted + export.xlsx fallback), descargando cleared in finally.
- 3.4 boton-exportar: presentational, descargando input + exportar output, CSS-glyph (no svg/img).
- 3.5-3.7 styles.css @layer primitives .tabla-catalogo additions, acceptance, guardrails.

PR4 (RED 55a0bf6 test-only / GREEN e36fb39 / checkboxes bb25f63):
- 4.1 PlanContableService: root signal service, cargar() GETs /api/catalogos/plan-contable once via a private cargado flag, a failed load leaves the guard OPEN (retryable), error text fixed.
- 4.2 PlanContablePage: container owns filtro/orden(null)/pagina/tamanio(20) signals; computed chain filtradas -> ordenadas -> totalPaginas -> paginaActual (clamped) -> visibles (slice); handlers reset page to 1 on filter/sort/tamanio; exportar() calls descarga.descargar(/api/catalogos/plan-contable/exportacion, q=filtro.trim()).
- 4.3 route + nav: app.routes.ts additive sibling ShellLayout child catalogos/plan-contable with canActivate authGuard lazy loadComponent; sidebar.ts nav-plan-contable gains ruta so it renders as a link.
- 4.4 PlanContableTabla: presentational, filas/orden inputs, ordenar output, .tabla-catalogo primitive, 2 sortable headers with aria-sort + flechaOrden arrow.
- 4.5 models/cuenta-contable.model.ts: CuentaContable (cuenta, descripcion, nivel|null, esHojaImputable), PlanContableRespuesta (items), ClavePlanContable.
- 4.6 acceptance: full suite + lint + build.

git diff --stat pr2..HEAD = 29 files, 1266 insertions / 17 deletions. Feature-level size:exception already owner-accepted; cause is strict-TDD spec coverage + heavy doc-comment blocks, not scope creep.

## Build / Test evidence (independent re-run)
- cd SmartNet/SmartNetWeb && npm test -> 46 files / 425 tests passed, 0 failed (exit 0). hash sha256:71a9ac2e...
- cd SmartNet/SmartNetWeb && npm run build -> bundle complete, exit 0, NO anyComponentStyle budget warning/error. hash sha256:aae27a7c...
  - plan-contable-page lazy chunk 10.92 kB; styles.css 8.92 kB (global stylesheet, not a component style; contraste.spec.ts / paleta.spec.ts parse it and pass).
- npm run lint (tsc --noEmit app + spec) -> clean.
- jsdom "Not implemented: navigation to another Document" x2 - console noise from an unrelated existing spec, no failure.

## Spec compliance (slices 3-4 in-scope grain)

### Requirement: Plan contable screen - full list with client-side filter and sort - 3/3 scenarios PASS (runtime-proven)
- Full plan renders: PASS - plan-contable-page.spec.ts asserts all 4 rows by codigo [10,101,104,42] and h1 "Plan contable"; the only footer is the client-side paginador, no server pagination control.
- Client-side filter and sort: PASS - onFiltro(caja) -> [101] with http.expectNone; header click -> asc [101,104,42,10], second click -> desc [10,42,104,101] with http.expectNone; page resets to 1 on filter change. Sort delegates to pure orden.ts ordenarPor (Spanish collator, null keys last, no source mutation - proven by orden.spec.ts).
- Export downloads the plan: PASS - asserts descargar(/api/catalogos/plan-contable/exportacion, q=caja); descarga-xlsx.spec.ts proves the GET is a blob with observe response, forwards q, clears descargando on 401.

### Requirement: Screens are query-only and follow the inbox pattern - 1/1 in-scope scenario PASS
- No mutation affordance (shipped plan-contable screen): PASS - asserts 0 crear/editar/eliminar/guardar testids; only GET requests (list GET + export GET). One data-access signal service (PlanContableService, providedIn root, private writable signal + asReadonly, ADR 0009), container owns filter/paging/sort signals, presentational ui/ tables (input/output, no data-service injection), models/ typed to contract. Full "any of the three screens" coverage completes in PR6/PR8.

### Partial credit (completed by later slices, not gaps here)
- Three guarded lazy catalog routes: 1 of 3 routes shipped. catalogos/plan-contable is an additive lazy ShellLayout child under authGuard; app.routes.spec.ts new it asserts presence + canActivate.length>0 + typeof loadComponent function, and the pre-existing arrayContaining([bandeja, detalle/:id, configuracion]) still passes. proveedores = PR6, tipo-cambio = PR8.
- Proveedores screen / Tipo de cambio screen: out of scope (PR6 / PR8).
- spa-shell-nav MODIFIED "Sidebar mirrors the handoff navigation": PARTIAL. Spec-as-written wants 8 destinations / 5 links. Current state is the canvas-replica 7 destinations / 3 links (nav-bandeja, nav-plan-contable, nav-configuracion). PR4 correctly flips nav-plan-contable inert->link only; nav-proveedores link = PR6, Tipo de cambio entry + 8th glyph = PR8. Intentional staged state documented in memory shell-nav-canvas-replica, the sidebar docblock, and sidebar.spec.ts (glyph count stays 7, inert loop shrank to nav-registro/nav-proveedores/nav-errores/nav-sincronizacion). See WARNING 1.
- spa-shell-nav MODIFIED "Shell CSS stays layout-only and within budget": PASS for this slice - no new nav glyph added in PR4 (plan glyph pre-existed), production build reports no anyComponentStyle error/warning.

## Design / ADR compliance
- Container/presentational + signals inbox pattern (spec req 5, ADR 0009): PASS.
- Client-side filter + sort + pagination over a single fetch (design D7/D8): PASS - service fetches once (cargado guard, proven by "does not issue a second request once loaded"); every narrowing is a computed().
- ONE shared Intl.Collator es (design D8): PASS - single module-level colador in orden.ts.
- tabla-paginador source-agnostic, PaginaBandeja-shaped, no HttpClient (D8): PASS.
- descarga-xlsx blob GET not window.open (401 in a new tab bypasses httpErrorInterceptor): PASS.
- Plan-contable export takes q only, no sort param (D9): PASS.
- q-filter parity, both sides asserted (D9, PR2 carry-forward): RESOLVED - server predicate: filtro.Length==0 OR Cuenta.Contains(filtro,OrdinalIgnoreCase) OR Descripcion.Contains(filtro,OrdinalIgnoreCase), filtro=(q ?? "").Trim(). SPA predicate: termino.length===0 OR cuenta.toLowerCase().includes(termino) OR descripcion.toLowerCase().includes(termino), termino=filtro().trim().toLowerCase(). Same shape: contains over cuenta|descripcion, case-insensitive, trimmed. SPA sends the un-lowercased trimmed term (server does its own fold) - correct. Minor: toLowerCase+includes vs OrdinalIgnoreCase diverge only on non-ASCII casing pairs; negligible for a Spanish/ASCII account catalog. See SUGGESTION 1.
- xlsx label / leaf-value cross-check (PR2 carry-forward): RESOLVED, no conflict - the SPA screen shows only 2 columns (codigo, denominacion) and never renders nivel or esHojaImputable, so no on-screen value can contradict the export Si/No leaf cells or its [Cuenta, Descripcion, Nivel, Es hoja imputable] headers. The export carries 2 more columns than the screen (WARNING 2).
- Route grouped catalogos/ prefix, additive (spec v2.1): PASS.
- Column label "denominacion" maps API field descripcion (spec risk note): PASS - model keeps descripcion; header reads Denominacion.
- Deviations from design: none material. Unspecified-and-not-load-bearing extras: subtitle copy, search placeholder, aria-sort on headers, host overflow-x auto wrapper.

## TDD Compliance (Strict TDD active)
| Check | Result | Details |
|-------|--------|---------|
| TDD Evidence reported | PASS | apply-progress #243 has a full TDD Cycle Evidence table for 4.1-4.6 (and its PR3 section for 3.1-3.7). |
| All tasks have tests | PASS | orden.spec, tabla-paginador.spec, descarga-xlsx.spec, boton-exportar.spec (PR3); plan-contable.service.spec (4), plan-contable-tabla.spec (3), plan-contable-page.spec (9), app.routes.spec (+1 it), sidebar.spec (assertion edits) (PR4). |
| RED confirmed (test-only commits) | PASS | 5680df6 and 55a0bf6 are BOTH test-only (git show --stat: no production ts under src/app except spec files). New specs import modules that do not yet exist -> esbuild "Could not resolve" + TS2307 -> genuine RED; app.routes.spec / sidebar.spec edits assert routes/links not yet present -> genuine assertion failures. |
| GREEN confirmed | PASS | 425/425 on independent re-run this session (exit 0). |
| RED-before-GREEN ordering | PASS | 5680df6 -> 1f11368, 55a0bf6 -> e36fb39. |
| Triangulation adequate | PASS | orden 9 cases (asc/desc/switch/purity/numeric/null-last); paginador 6 (indicator, first/last disable, prev/next emit [2,4], rows-per-page emit+reset, default options); page 9 with distinct expected values; service 4 (fetch-once, no-2nd-request, cargando toggle, error+retryable). |
| Safety net for modified files | PASS | app.routes.spec and sidebar.spec modified (not new) - pre-existing suites green before edits; full 425-test suite green after. styles.css modified - contraste/paleta specs green. |

## Assertion quality
All assertions verify real behavior: rendered td text arrays, "Pagina X de Y" text, role=alert, aria-sort, button disabled state, exact emitted-value arrays, exact spy call arguments, http.expectNone / http.verify proving no accidental server calls on client-side ops, flush(null,{status}) for real error paths, URL.createObjectURL/revokeObjectURL call assertions (genuine observable side effect of a jsdom download). plan().toEqual([]) in the service error test has a companion non-empty assertion in the SAME test (plan().length -> 2 after retry) - not an orphan empty check. No tautologies, no ghost loops (sidebar inert loop iterates a fixed testid array, not a queryAll result), no smoke-only tests, no CSS-class coupling. One vi.spyOn in one test, well under the mock-heavy threshold. Assertion quality: 0 CRITICAL, 0 WARNING.

## Test Layer Distribution
| Layer | Tests | Files | Tools |
|-------|-------|-------|-------|
| Unit | 43 new (PR3+PR4) | 9 | Vitest + jsdom + Angular TestBed / HttpTestingController |
| Integration | 0 | - | no runtime boundary added in an SPA unit slice |
| E2E | 0 | - | not installed |
| Whole suite | 425 | 46 | |

The plan-contable list + exportacion endpoints are contract-tested in PR2 (dotnet 172/172). The integration-spa-api harness re-run is PR9.

## Issues

CRITICAL: none.

WARNING (none blocking archive of slices 3-4):
1. spa-shell-nav "Sidebar mirrors the handoff navigation" not yet fully satisfied - the MODIFIED requirement text describes an 8-destination / 5-link sidebar; current state is 7 / 3. Intentional staged rollout (PR6 activates nav-proveedores, PR8 adds Tipo de cambio + 8th glyph) documented in memory and the sidebar docblock, but the requirement text reads as unmet until PR8. apply-progress "Deviations from design: None" is accurate against design D5 (which stages it) but not against the spec delta text.
2. Excel export carries more columns than the screen shows - ExportarPlanContableAsync emits [Cuenta, Descripcion, Nivel, Es hoja imputable] with Si/No leaf cells, while the screen renders only codigo + denominacion. Screen is spec-correct (req 3); the wider export was PR2 choice. A user exporting from a 2-column screen gets a 4-column file - confirm intended before archive (not a defect, a UX surprise).
3. size:exception magnitude - PR3 (+601) and PR4 (+652) each exceed the ~330-line estimate; combined slice-3-4 diff is 1266 insertions / 29 files. Owner-accepted at feature level; reviewer load is real. No coverage was cut.

SUGGESTION:
1. SPA client filter uses toLowerCase+includes while server export uses Contains OrdinalIgnoreCase; align on one case-fold strategy (or a shared note) when proveedores/TC filters arrive so the "expressed twice" predicates stay byte-equivalent.
2. descarga-xlsx nombreDesdeContentDisposition extended-filename regex does not strip an RFC 5987 language tag; harmless today (constant ASCII server filenames) but worth hardening if a localized filename is ever sent.
3. No coverage tool wired into npm test; changed-file line/branch coverage could not be reported. Consider vitest --coverage in CI for the catalogos feature.
4. plan-contable-tabla host overflow-x auto is the only component-local CSS; the horizontal-scroll affordance is untested.

## Verdict
PASS WITH WARNINGS - slices 3 and 4 are correct and complete against their 13 tasks. The Plan Contable screen satisfies its spec requirement 3/3 scenarios with runtime-proven tests (client-side filter/sort/pagination over a single fetch, http.expectNone guarding against accidental server calls, export via the shared blob helper), and the query-only requirement is proven for the shipped screen (no mutation control, GET-only). The shared chrome (orden.ts pure + collator-correct, tabla-paginador source-agnostic, descarga-xlsx 401-safe, boton-exportar presentational) is well-triangulated. Strict TDD genuinely followed: both RED commits are test-only and precede their GREEN. Build + lint + full 425-test suite green, no CSS budget breach. Both PR2 carry-forward items (q-filter parity, xlsx label cross-check) are RESOLVED. Warnings are a staged-rollout spec-text gap (sidebar reaches its final 8-entry shape in PR8), an export-wider-than-screen UX note, and size-exception magnitude - none block archiving slices 3-4. Whole-change totals for reference: 15 requirements / 45 scenarios across the 3 spec files, closed progressively by PR1-PR9.

Validator: gentle-ai sdd-verify-validate --input <report> --requirements 2 --scenarios 4.

---
# PR2 (slice 2: API plan contable) - verified at HEAD 8227a9f, branch feat/consultas-catalogos-spa-22-pr2

Scope: slice 2 of 9 ONLY. Slices PR3-PR9 are intentionally not implemented and are not gaps. Slice 1 verified separately (PASS WITH WARNINGS).

## Completeness (slice 2) - tasks 2.1-2.5 all [x], match commits
- 2.1 RED list route [x]: commit 7fc9bd6 adds PlanContable_Returns200 and _WithoutACookie_Returns401 before any impl.
- 2.2 RED export route [x]: commit 7fc9bd6 adds PlanContableExportacion_Returns200_XlsxHeaders, _WithoutACookie_Returns401_AndNoFile, _HostileQuery_FilenameStaysConstantForm.
- 2.3 GREEN list endpoint + DTO [x]: commit 711fb4c - CuentaContableResultado + PlanContableRespuesta records in CatalogoEndpoints.cs (no new file); ListarPlanContableAsync thin over ListarPlanCompletoAsync; EsHojaImputable projected from the domain record.
- 2.4 GREEN export endpoint [x]: commit 711fb4c - ExportarPlanContableAsync; q predicate contains-over Cuenta or Descripcion OrdinalIgnoreCase; ExportadorXlsx.Escribir reused; filename constant + server date from injected TimeProvider.
- 2.5 Acceptance [x]: dotnet test SmartNet.Api.Tests 172/172 green; guardrails honored.

ICuentaContableRepository singleton registered in Program.cs (was previously unregistered) using the same lazy ApiConnectionOptions.Resolve factory pattern as IProveedorRepository directly above it. DocumentFormat.OpenXml 3.3.0 added to SmartNet.Api.Tests.csproj (test read-back only).

## Build / Test evidence (this verification, local SQL Server available)
- dotnet test SmartNet/SmartNetApi/api/SmartNet.Api.Tests -> 172 passed / 0 failed / 0 skipped (exit 0). All ran for real against local SQL Server.
- dotnet build SmartNet/SmartNetApi/api/SmartNet.Api -> Compilacion correcta, 0 warnings, 0 errors (exit 0).
- dotnet test SmartNet.Exportacion.Infrastructure.Tests -> 4/4 green (incl NoCoreReferencesOpenXmlGuard).
- dotnet test SmartNet.Catalogos.Core.Tests -> 32/32 green (PurityScanTests).

## Spec compliance (slice 2 in-scope grain)

Requirement: Plan contable endpoint returns the full chart in one response - 3/3 scenarios covered by passing tests:
- Full plan returned (all accounts, ordered by cuenta, camelCase, no paging fields): PASS - PlanContable_Returns200_Unpaged_CamelCase_OrderedByCuenta_EsHojaImputableIffNivelNull asserts 3 items, order 10/101/40, descripcion + nivel values; response record exposes only items.
- Leaf accounts flagged (esHojaImputable true iff nivel null): PASS - same test: false for nivel=2, true + JsonValueKind.Null for nivel=null.
- Unauthenticated -> 401: PASS - PlanContable_WithoutACookie_Returns401.

Partial contributions to other requirements (completed by later slices):
- Excel export endpoint per catalog - plan-contable portion: real .xlsx (workbook reopened, row count asserted), Excel Content-Type, attachment Content-Disposition, .xlsx extension, full q-filtered set, 401 -> no file. Unauthenticated export PASS for this route.
- Read-only, partition-respecting access - No writes/no schema drift: PASS for plan contable. SqlCuentaContableRepository issues only SELECT (verified source); diff touches only CatalogoEndpoints.cs, Program.cs, tests; no new SQL script, no GRANT, no dbo star write; usr_api already holds SELECT on dbo.CuentaContable.
- Contract-test coverage - plan contable clause (full unpaged list + esHojaImputable flag) satisfied.

## Design / ADR compliance
- Thin endpoint over ListarPlanCompletoAsync, no accounting logic: PASS.
- esHojaImputable projected, not recomputed (design D3): PASS - c.EsHojaImputable (domain: Nivel is null).
- q predicate parity with SPA client filter (design D9 expressed-twice rule): PARTIAL - server side is contains, OrdinalIgnoreCase, over cuenta OR descripcion, trimmed; matches D9 wording. SPA side lands in PR4; both-sides assertion completes then.
- Export sub-path exportacion (D9): PASS.
- No user input in Content-Disposition (ADR 0021 decision 4): PASS - filename plan-contable-YYYY-MM-DD.xlsx from injected TimeProvider; hostile-q test asserts constant-form filename, no CR/LF, no injected token.
- ExportadorXlsx.Escribir(Stream, filas, columnas) reused unchanged (PR1 D9): PASS - MemoryStream buffer + 4 header labels.
- List route param surface: WARNING - design params table (approx line 356) lists q/orden/direccion params + 400 for the LIST route; impl takes no params. Consistent with spec req 4 and design D8 (client-side filter/sort); the design params table contradicts its own D8/spec. Non-blocking - impl follows the spec. apply-progress "Deviations from design: None" is slightly inaccurate.
- Partition guardrails (ADR 0003 / 0016): PASS - no dbo star write, no dbo star index, no versioned SQL, no GRANT.

## TDD Compliance (Strict TDD active)
- TDD Cycle Evidence table in apply-progress: PASS - apply-progress #243 has a RED/GREEN command-to-result table for 2.1, 2.2, 2.5.
- All tasks have tests: PASS - 5 new tests cover both routes + 401 + hostile filename.
- RED confirmed: PASS - separate RED commit 7fc9bd6 (tests + OpenXml csproj ref only, no impl); routes absent so they return 404 and all 5 assertions genuinely failed (the 401 tests see 404, not 401). Matches apply-progress "5 FAIL, Actual NotFound".
- GREEN confirmed: PASS - 172/172 on independent re-run this session.
- RED-before-GREEN ordering: PASS - provable from history: 7fc9bd6 (test) precedes 711fb4c (feat). Improvement over the PR1 single-commit case.
- Triangulation: PASS - list: nivel=null vs nivel=2 rows, 3-account ordering; export: q-match vs 2 non-matches (row count 2), hostile q, missing cookie.
- Safety net for modified file: PASS - CatalogoEndpointsTests.cs was modified (not new); full 172-test suite re-run green.

## Assertion quality
All assertions verify real behavior: exact status codes, exact cuenta array ordering, descripcion string, nivel int, esHojaImputable booleans, JsonValueKind.Null, Excel media-type string, regex on Content-Disposition, workbook row count via SpreadsheetDocument.Open + SheetData element count. Assert.NotEmpty(bytes) is paired with Assert.Equal(2, ContarFilasHoja(bytes)). No tautologies, ghost loops, smoke-only, or implementation-detail coupling.

## Issues
CRITICAL: none.

WARNING:
1. Design params table (list route q/orden/direccion params + 400s) not implemented; impl returns the whole plan unparameterized. Follows spec req 4 and design D8, so non-blocking, but apply-progress claims zero design deviations.
2. SPA-side q filter parity (design D9 expressed-twice, asserted both sides) is not fully verifiable in slice 2 - the SPA filter lands in PR4. Server predicate matches the D9 wording.
3. xlsx leaf-cell values (Si/No) and header labels were unspecified; chosen ASCII-safe by apply. Confirm against SPA export expectations in PR4.

SUGGESTION:
1. Export q predicate is duplicated inline in the endpoint; when proveedores/TC exports arrive consider a shared filter helper.
2. Export test asserts row count but not cell contents of the filtered data row; a value assertion would harden it.
3. Confirm the SPA model types nivel as number-or-null in PR4.

## Verdict
PASS WITH WARNINGS - slice 2 is correct and complete against its 5 tasks, satisfies the plan-contable spec requirement (3/3 scenarios via passing DB-backed contract tests) plus the plan-contable portions of the export, read-only-partition, and contract-coverage requirements. Build and all relevant suites green (172/172 API, 4/4 export guard, 32/32 core purity). Strict TDD genuinely followed with a provable RED-before-GREEN commit split. Warnings are a design-table-vs-spec discrepancy (impl correctly follows the spec) and parity checks deferred to the SPA slice; none block archive of this slice. Whole-change totals for reference: 15 requirements / 45 scenarios across the 3 spec files, closed progressively by PR2-PR9.

# PR1 (slice 1: export infrastructure) - retained from prior verification
Verdict: PASS WITH WARNINGS. Scope slice 1 of 9. ExportadorXlsx.Escribir(Stream, filas, columnas) matches design D9; valid reopenable .xlsx; OpenXmlWriter + MemoryStream buffer per ADR 0021 decision 3; validation before first byte; OpenXml 3.3.0 exact pin in the new infra csproj + Tests csproj only; Core-purity guard meaningful; no SQL/schema/GRANT touched. Warnings: code+tests in single commit a1c97bc, no formal TDD evidence table at the time, full API suite not run (no DB then).
