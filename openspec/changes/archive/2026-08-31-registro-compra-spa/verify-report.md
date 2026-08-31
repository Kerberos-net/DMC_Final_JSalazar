```yaml
schema: gentle-ai.verify-result/v1
evidence_revision: sha256:77a3f20f4f3e6ec3183f606d0f4c13c3a2abc5eb7e898c2f84d68724b07390b7
verdict: pass_with_warnings
blockers: 0
critical_findings: 0
requirements: 13/13
scenarios: 36/36
test_command: "dotnet test api/SmartNet.Api.Tests + facturacion/SmartNet.Facturacion.Core.Tests + npm test"
test_exit_code: 0
test_output_hash: sha256:5e30115c418568b679a937cae69f94218a3e7fd69e30f412edef072232e02866
build_command: "npm run lint"
build_exit_code: 0
build_output_hash: sha256:d585f030e61a0a6a5e4957e3a987b42d6733910aacc7541d42fd2fcad891a8bd
```

# Verification Report -- registro-compra-spa (BACKLOG #23)

Mode: hybrid; full artifacts; Strict TDD active.
Verdict: PASS WITH WARNINGS

## Completeness
- Tasks: 40 / 40 complete; each sampled checkbox maps to real committed work.
- Requirements: 13 / 13 covered (api 5, spa 7, shell-nav 1). Scenarios: 36 / 36.
- Design D1-D8 all honored.

## Test evidence (re-run by verify)
- dotnet test api/SmartNet.Api.Tests => 241 passed / 0 failed (3m53s); RegistroCompraEndpointsTests + PermissionMatrixTests green.
- dotnet test facturacion/SmartNet.Facturacion.Core.Tests => 158 passed / 0 failed; PurityScanTests green (new port clean).
- npm test (ng test) => 499 passed / 0 failed (57 files).
- npm run lint (tsc --noEmit app + spec) => clean, exit 0.
- Counts strictly above pre-change => additive tests only; no existing test removed or weakened. Only deletion in an existing spec is the intended nav-registro removal from the sidebar inert loop.

## registro-compra-api
- periodo required, YYYY-MM only, rejects 2026-8 / agosto / absent -> 400 RFC 9457 (PeriodoContable.TryParse: length 7, dash at index 4, NumberStyles.None, month 1-12). PASS
- Half-open FechaContable month range via RangoMedioAbierto + SqlParameter Date. PASS
- Row predicate Factura VALIDADA AND asiento not ANULADO, shared const for list/export/detail. PASS
- Cabecera fields incl. origenLibro verbatim column (not literal 02) and nullable proveedorNombre via LEFT JOIN dbo.Proveedor. PASS
- Envelope PaginaRegistroCompra with 5 wire fields (items, pagina, tamanioPagina, totalRegistros, totalPaginas). PASS (WARNING-1)
- COUNT star OVER total + ORDER BY FechaContable, NumeroAsiento, AsientoContableId (stable tiebreak); tested across 2 pages. PASS
- Detail route re-applies the predicate in SQL -> filtered-out id returns 404, indistinguishable from nonexistent. PASS
- Empty period -> 200 with items empty and totalRegistros 0 (not 404). Qualifying asiento with 0 lines -> 200 lineas empty. PASS
- Malformed periodo -> 400 problem+json on list and export. PASS
- Export returns .xlsx, attachment Content-Disposition, filename rebuilt from parsed Anio/Mes ints; CRLF-injection periodo rejected at TryParse -> 400. PASS
- All 3 routes RequireAuthorization -> 401 tests. camelCase (JsonSerializerDefaults.Web). PASS
- Read-only IRegistroCompraRepository port, PurityScan-guarded; no new GRANT, no versioned SQL; GET /api/asientos/{id} untouched. PASS

## registro-compra-spa
- Lazy loadComponent child of ShellLayout, canActivate authGuard, path registro-compra, additive app.routes.spec. PASS
- periodo default = current month from LOCAL date (formato.mesActual uses getFullYear/getMonth, never toISOString; 31-Dec-23:00 test). PASS
- Server-side pagination consuming the envelope; change period re-queries and resets to page 1. PASS
- Row expand -> read-only asiento lines; no edit / anular / reactivar control. PASS
- Badge: pure computed(), r2 = Math.round(n*100)/100, NO epsilon, no core import. Lights only on r2(base+igv) != r2(neto) OR r2(sum debe) != r2(sum haber). Any null term -> no badge and em dash. Unit tests cover both formulas, boleta igv 0, 118.01 lights vs 118.00 does not, null terms, percepcion cancels. PASS
- Export button downloads the period .xlsx from the API endpoint (reuses descarga-xlsx). PASS
- Loading / non-blocking error / explicit empty state; no stale-as-current. PASS
- One data-access signal service (asReadonly + cargando/error), container owns period/paging/expand, presentational ui typed to contract; only GET. PASS

## spa-shell-nav (DELTA)
- nav-registro now routed to /registro-compra in the primary group. PASS
- sidebar.spec inert loop reduced to nav-errores + nav-sincronizacion; routed assertion for nav-registro added; 8-label / 1-divider / 8-glyph tests unchanged. PASS
- openspec/specs/spa-shell-nav/spec.md NOT hand-edited (task 6.3: regenerated at archive). PASS (archive action pending)

## ADR / constraints
- ADR 0016: git status shows zero new .sql files and zero new GRANT. PASS
- ADR 0019: no SmartNet.Contable.Core import in the 4 new backend files (only AsientoContable/FechaContable token matches); PurityScan 158 green; SPA badge is a pure computed() with no core import. PASS
- ADR 0003: only SELECT plus LEFT JOIN dbo.Proveedor read; no dbo writes; no SmartNetWorker/Python file touched. PASS
- Money/rate fields decimal nullable; SQL GetDecimal; export formats F2/F6 InvariantCulture; no float/double. PASS
- Identifiers: accounting domain Spanish, scaffolding English, no accents/enye in identifiers. PASS
- api-asientos and shell-layout untouched; modified files limited to sidebar, app.routes, formato, Program.cs (additive DI), FacturaTestDataHelper.cs (additive overload). PASS

## Badge vs REGLAS.md 6 / 7.1 / 10
- Exact to the centimo, no epsilon: strict not-equal on rounded values; boundary test 118.01 vs 118.00. PASS
- Percepcion excluded from the header formula and cancels on both sides in debe/haber: dedicated test. PASS
- Boleta / no gravada (igv 0, base = neto): no false positive, dedicated test. PASS
- Null term -> no badge, renders em dash: dedicated tests. PASS
- Both header and detalle formulas each have a test case. PASS

## Issues
CRITICAL: none.

WARNING-1: registro-compra-api/spec.md says the body SHALL use the project PaginaBandeja envelope; the implementation uses a new PaginaRegistroCompra record. Design D1 ratified this correction (PaginaBandeja carries a mandatory ResumenBandeja and would make facturacion depend on inbox; #22 set the PaginaProveedores precedent). Wire contract is byte-identical and the SPA consumes it correctly. No behavioral defect. Reword the spec at archive.

SUGGESTION-1: spec lists the row id field as asientoId; design, API record, and SPA model all use asientoContableId on the wire, consistently end-to-end. Align the spec text at archive.
SUGGESTION-2: detail cabecera exposes the same field set as a listado row plus lineas; spec req 2 phrasing is satisfied.

## Notes for archive
- Regenerate openspec/specs/spa-shell-nav/spec.md from the delta (6 routed / 2 inert), and sync the two registro-compra specs; fold in WARNING-1 and SUGGESTION-1 wording.
- Harness SmartNet/harnesses/integration-spa-api/SKILL.md already updated (flow 4); gitignored local .claude/skills copy also updated. Untracked README.md and root harnesses/ predate this change.
- Single size:exception PR (~1,150 lines) already owner-accepted.
- Local SmartNet.Api.exe (PID 5416) was killed during apply; restart if needed.

## Final verdict
PASS WITH WARNINGS. All 40 tasks complete and independently verified; 241 / 158 / 499 tests green; every requirement and scenario has covering evidence; ADR 0003 / 0016 / 0019 / 0021 and money-type / identifier-language conventions all hold; the inconsistency badge matches REGLAS.md 6 / 7.1 / 10. The single WARNING is spec-prose drift with a byte-identical wire contract, a doc sync for archive, not an implementation defect. No CRITICAL issue blocks archive.
