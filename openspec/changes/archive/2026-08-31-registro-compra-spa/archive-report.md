# Archive Report: registro-compra-spa (BACKLOG #23)

**Date**: 2026-08-31  
**Change**: `registro-compra-spa` — Read-only SPA screen for libro de compras (registro de compras)  
**Status**: ARCHIVED  
**Verdict**: PASS WITH WARNINGS (verify obs #256)

## What Shipped

Single size:exception PR (~1,150 changed lines), all 40 tasks complete. Delivered:

1. **API**: `IRegistroCompraRepository` Core port + `SqlRegistroCompraRepository` adapter (ADO, no new SQL/GRANT; ADR 0019/0003 clean)
   - `GET /api/registro-compra?periodo=YYYY-MM` → listado (PaginaRegistroCompra envelope, 5 wire fields)
   - `GET /api/registro-compra/{asientoContableId}` → detalle (cabecera + lineas[])
   - `GET /api/registro-compra/export?periodo=YYYY-MM` → .xlsx (API-side generation, ADR 0021)
   - All 3 routes `.RequireAuthorization()`, RFC 9457 on malformed input

2. **SPA**: New `registro-compra/` feature (container + ui/ + data-access + models)
   - Period filter (YYYY-MM), default current LOCAL month
   - Server-side paginated table with row expand
   - Read-only line detail (asiento-detalle)
   - Inconsistency badge: pure `computed()`, exact-to-cent, no epsilon (REGLAS.md §6/§7.1/§10)
   - Export button (reuses descarga-xlsx)
   - Loading/error/empty states; no stale-as-current

3. **Shell Navigation**: `Registro de compra` inert → routed link
   - sidebar.ts: route added to `nav-registro` in primarios
   - sidebar.spec.ts: inert loop reduced to 2 (nav-errores, nav-sincronizacion)
   - Spec delta applied to `spa-shell-nav` main spec

## Spec Syncs & Corrections

All 3 delta specs have been synced to `openspec/specs/` with WARNING-1 and SUGGESTION-1 fixes applied:

### New Specs Created

1. **`openspec/specs/registro-compra-api/spec.md`** (5 requirements, 16 scenarios)
   - WARNING-1 FIXED: Changed prose from "PaginaBandeja<T>" to "PaginaRegistroCompra<T>"
     - Explanation added: avoids mandatory ResumenBandeja (inbox dependency); #22 precedent
   - SUGGESTION-1 FIXED: All row ID field references changed from `asientoId` to `asientoContableId`
     - Matches implementation and wire contract end-to-end

2. **`openspec/specs/registro-compra-spa/spec.md`** (7 requirements, 15 scenarios)
   - Uses corrected `PaginaRegistroCompra<T>` in requirement prose
   - All asientoContableId references consistent

### Modified Specs

3. **`openspec/specs/spa-shell-nav/spec.md`** — UPDATED (inert → routed pattern applied)
   - Requirement "Sidebar mirrors the handoff navigation" now lists 6 routed links, 2 inert
   - Scenario "Routed destinations are links": added Registro de compra to routed list
   - Scenario "sidebar.spec.ts asserts the new exact list": updated to 6 routed, 2 inert
   - Pattern mirrors #22 (Proveedores/Plan contable amendment)

## Test Evidence

All tests re-run and green per verify-report (obs #256):

- **`dotnet test api/SmartNet.Api.Tests`**: 241 passed / 0 failed
  - RegistroCompraEndpointsTests (20 methods/38 cases): 401 per route, camelCase, period filtering, pagination envelope, empty period, malformed periodo, proveedorNombre null, origenLibro verbatim, detail 404 outside libro, export xlsx + filename injection guard
  - PermissionMatrixTests: green (no new GRANT, existing fact_api SELECT grants sufficient)
- **`dotnet test facturacion/SmartNet.Facturacion.Core.Tests`**: 158 passed / 0 failed
  - PurityScanTests: green (new IRegistroCompraRepository port + records in Core verified as infra-free)
- **`npm test` (ng test)**:  499 passed / 0 failed (57 files)
  - Badge computed() truth table (both formulas, boleta igv=0, exact-to-cent boundary 118.01 vs 118.00, null terms, percepcion cancels)
  - Service params/envelope/error/detail cache
  - Sidebar routed/inert split
- **`npm run lint` (tsc --noEmit)**: clean, exit 0

All counts strictly above pre-change baseline; no existing test weakened; only intentional deletion in spec = nav-registro removal from sidebar inert loop.

## Compliance & Constraints

✓ **ADR 0016** (SQL versionado): Zero new .sql files, zero new GRANT issued  
✓ **ADR 0019** (pureza del núcleo): No SmartNet.Contable.Core import in 4 new backend files; badge is pure `computed()` with no core import  
✓ **ADR 0003** (partición de datos): Only SELECT + LEFT JOIN dbo.Proveedor read; no dbo.* write; no Python/SmartNetWorker touched  
✓ **ADR 0021** (Excel API-side): ExportadorXlsx helper used; filename rebuilt from parsed ints (no user input in Content-Disposition)  
✓ **Money/rate types**: decimal nullable (not 0-coerced); export formats F2/F6 InvariantCulture  
✓ **Identifiers**: accounting domain Spanish, scaffolding English, no accents  
✓ **api-asientos untouched**: GET /api/asientos/{id} contract unchanged per Decision 3  
✓ **shell-layout untouched**: No changes to ShellLayout container or template

## Delivery

**Route**: Single size:exception PR (~1,150 changed lines)  
**Acceptance**: Owner explicitly accepted size:exception (cached in delivery strategy)  
**Receipt**: Disabled/unmanaged (receipt-driven development OFF per launch prompt)  
**Integration harness**: Flow recorded manually in .claude/skills/integration-spa-api/SKILL.md (harness precedent #22)

## Artifacts Archived

Change folder moved to: `openspec/changes/archive/2026-08-31-registro-compra-spa/`

Contains:
- exploration.md
- proposal.md
- design.md
- tasks.md (40 tasks, all checked)
- verify-report.md
- specs/registro-compra-api/spec.md (delta)
- specs/registro-compra-spa/spec.md (delta)
- specs/spa-shell-nav/spec.md (delta)

## Engram Artifact References

All intermediate phase artifacts preserved for traceability:

- Proposal: obs #251 (sdd/registro-compra-spa/proposal)
- Spec: obs #252 (sdd/registro-compra-spa/spec)
- Design: obs #253 (sdd/registro-compra-spa/design)
- Tasks: obs #254 (sdd/registro-compra-spa/tasks)
- Verify report: obs #256 (sdd/registro-compra-spa/verify-report)

## Notes for Operators

1. BACKLOG.md #23 may be marked DONE (all work shipped and verified)
2. SPRINT.md closure handled externally by lecciones-aprendidas skill
3. No rollback risk: all changes additive; `git revert` returns system to post-#22 state
4. No dependent changes: Shell nav, API routes, SPA feature all self-contained

## Verdict: ARCHIVED

**All 40 tasks complete. 241 API tests + 158 Core tests + 499 SPA tests green. No CRITICAL issues. Single WARNING-1 (spec prose, byte-identical wire contract) and SUGGESTION-1 (field naming) both fixed at archive. ADR 0003/0016/0019/0021 constraints all satisfied. No new SQL, no new GRANT, no núcleo contable references. Receipt-driven development disabled; delivery marked unmanaged per kill switch. Change is ready for post-archive cleanup and transition to the next BACKLOG item.**
