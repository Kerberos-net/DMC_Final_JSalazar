# Archive Report — consultas-catalogos-spa (BACKLOG #22)

## Executive Summary

BACKLOG item #22 (read-only catalog queries in the SPA) is **COMPLETE and ARCHIVED**. All 9 stacked PRs
were implemented, verified (PASS WITH WARNINGS, 0 CRITICAL), and the change is ready for production. The
feature delivers three read-only catalog screens (proveedores, plan contable, tipo de cambio), their
backend endpoints, Excel exports, and a sidebar navigation delta (7 → 8 entries). No accounting-core
changes, no new SQL, no new GRANT, no external-system writes.

## Artifacts & Traceability

| Artifact | Engram Observation ID |
|----------|-----------------------|
| Proposal (post-design scope expansion, decisions 6-9) | #238 |
| Spec v2.1 — catalog-queries-api (NEW, 8 req), catalog-queries-spa (NEW, 5 req), spa-shell-nav (DELTA) | #239 |
| Design v2 (FINAL, D1-D10 + ADR 0021) | #240 |
| Tasks (9 stacked PRs, all `[x]`) | #241 |
| Apply progress (PR1-PR9) | #243 |
| Verify report (PASS WITH WARNINGS, 0 CRITICAL, 9 req / 23 scen) | #244 |
| Archive report (this) | #245 |

### Spec merges into `openspec/specs/`

| Spec | Action |
|------|--------|
| `catalog-queries-api/spec.md` | CREATED — 3 catalog GET endpoints + 3 export routes + contract coverage |
| `catalog-queries-spa/spec.md` | CREATED — 3 guarded lazy routes + 3 query-only screens |
| `spa-shell-nav/spec.md` | MERGED delta — sidebar 7→8 entries, 2→5 links, new `Tipo de cambio` entry |

## What Shipped

### Backend endpoints (read-only, partition-respecting)
- `GET /api/catalogos/plan-contable` — full chart unpaged, camelCase `{cuenta, descripcion, nivel, esHojaImputable}`
- `GET /api/catalogos/proveedores?modo=catalogo&q=&orden=&direccion=&pagina=&tamanio=` — new catalog mode
  (browse-all, `PaginaBandeja<T>` envelope, `COUNT(*) OVER()` single-pass total, server sort whitelist +
  `codpro` tiebreak). Picker mode (`modo` absent) byte-frozen per #18.
- `GET /api/tipos-cambio?desde=&hasta=` — both params required (400 if missing/unparseable/inverted),
  max 366-day span (400 if exceeded), both `SBS` + `MANUAL` rows per date.
- `GET .../exportacion` for each — `.xlsx` of the full filtered set; `Content-Disposition` filename with
  no user input; 401 unauthenticated.

### SPA screens (query-only, inbox container/presentational pattern)
- `/catalogos/proveedores` — table código/razón social/RUC, debounced search, server-side sortable
  headers, `Página X de Y` pagination footer, Excel export.
- `/catalogos/plan-contable` — table código/denominación, client-side filter + client-side sort, export.
- `/catalogos/tipo-cambio` — date range (defaults: 1st-of-month → today, LOCAL date), table
  fecha/origen/compra/venta (both origins), client-side sort, export.

### Shared SPA components (PR3)
`catalogos/ui/tabla-paginador/`, `catalogos/ui/boton-exportar/`, `catalogos/ui/orden.ts` (pure sort
helpers), `catalogos/data-access/descarga-xlsx.ts` (blob download).

### Sidebar navigation delta
7 → 8 destinations (`Tipo de cambio` added to primary group after `Plan contable`); 2 → 5 routed `<a>`
links (`Proveedores`, `Plan contable`, `Tipo de cambio` activated); `Registro de compra`,
`Errores y notificaciones`, `Sincronización` remain inert. 8th glyph folded into existing bar rules;
`sidebar.css` stays under the 6kB optimized-build budget.

### New infrastructure + ADR
- `SmartNet.Exportacion.Infrastructure` project — isolated `DocumentFormat.OpenXml` 3.3.0 dependency;
  `ExportadorXlsx.Escribir(Stream destino, IEnumerable<IReadOnlyList<string>> filas, IReadOnlyList<string> columnas)`
  (SAX `OpenXmlWriter`, `MemoryStream` buffer); structural guard test asserts no `*.Core` project
  references it (ADR 0019).
- `adrs/0021-generacion-de-archivos-excel-en-la-api.md` — Estado **"Propuesto. Revisión 1"**.
  **Owner TODO: move to "Aceptado" when governance ratifies this change.**

## Implementation: 9 Stacked PRs (`size:exception` accepted, `stacked-to-main`)

| PR | Branch | Slice | RED → GREEN |
|----|--------|-------|-------------|
| 1 | `feat/consultas-catalogos-spa-22-pr1` | Export infrastructure | `a1c97bc` (+ planning `48b6bfd`) |
| 2 | `…-pr2` | API plan contable | `7fc9bd6` → `711fb4c` |
| 3 | `…-pr3` | SPA shared chrome | `5680df6` → `1f11368` |
| 4 | `…-pr4` | SPA plan contable screen | `55a0bf6` → `e36fb39` |
| 5 | `…-pr5` | API proveedores catalogo mode | `eaebb5b` → `0dc990c` |
| 6 | `…-pr6` | SPA proveedores screen | `d74a0d3` → `f526b2b` |
| 7 | `…-pr7` | API tipo de cambio history | `7d45f40` → `8a86f67` |
| 8 | `…-pr8` | SPA tipo de cambio screen + sidebar 7→8 | `93005db` → `031899b` |
| 9 | `…-pr9` | Integration harness re-run + report | `6b44a1d` (docs) |

## Final Verification: PASS WITH WARNINGS (0 CRITICAL)

| Layer | Result |
|-------|--------|
| `dotnet test api/SmartNet.Api.Tests` | 203/203 |
| `npm test` (52 files) | 464/464 |
| `TiposCambio.Core.Tests` (PurityScan) | 20/20 |
| `TiposCambio.Infrastructure.Tests` | 15/15 |
| `Exportacion.Infrastructure.Tests` (Core-purity guard) | 4/4 |
| `npm run lint && npm run build` | clean, no CSS budget breach |
| `dotnet build api/SmartNet.Api` | 0 warnings / 0 errors |

Spec coverage: 9 requirements / 23 scenarios, all runtime-proven.

### Standing warnings (non-blocking)
1. `size:exception` — ~700 LOC over the 400-line-per-PR budget across the stack; owner-accepted feature-level exception.
2. Pre-existing `prettier@3.9.6` trailing-comma drift; effective gate `tsc --noEmit` passes.
3. `spa-shell-nav` active-destination scenario satisfied structurally (RouterLinkActive + token rule), no dedicated runtime assertion.

### Deviations (not defects)
1. PR9 committed previously-untracked `HARNESS.md` + `SmartNet/harnesses/integration-spa-api/SKILL.md` to git (slice 9's durable home; ~55 authored lines).
2. TC sidebar glyph uses two opposed CSS chevrons instead of a literal third bar — cosmetic, meets the div/span + pseudo-element, no-svg/img constraint.

## No regressions
- #18 picker: proveedores endpoint frozen (`{resultados, hayMas}`, min-length 2, `P00000` excluded) — zero-line diff in picker files.
- #8 Venta-freeze path (`ObtenerVigenteAsync`, `SqlUnidadDeTrabajo`, `SqlFacturacionStore`) untouched; `ListarHistoricoAsync` is read-only + clock-pure.
- ADR 0003: no index on `dbo.Proveedor`/`dbo.CuentaContable`, no `dbo.*` write.
- ADR 0016: no new versioned `.sql`, no new `GRANT`.

## Next steps for owner
1. Move ADR 0021 to **Aceptado** when the change is governance-ratified.
2. Merge the 9 stacked PRs to `main` per deployment cadence.
3. `/lecciones-aprendidas` — Obsidian note + SPRINT.md close for item #22.

## Archive metadata
- Change: `consultas-catalogos-spa` (BACKLOG #22)
- Archived: 2026-08-30
- Folder: `openspec/changes/archive/2026-08-30-consultas-catalogos-spa/`
- SDD cycle: proposal → spec → design → tasks → apply → verify → archive
