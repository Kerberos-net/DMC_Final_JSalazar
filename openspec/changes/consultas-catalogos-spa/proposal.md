# Proposal: Read-only catalog queries in the SPA (BACKLOG #22)

## Intent

The #21 sidebar shipped as the design-handoff replica: 7 entries, 5 inert. `Proveedores`
and `Plan contable` are dead links and there is no way to review the exchange-rate history
from the SPA. Operators must inspect these catalogs directly in SQL. This change gives
`Proveedores` and `Plan contable` a route and a screen, adds a new `Tipo de cambio`
history screen, and exposes the three read paths as `GET /api/*` endpoints. Query-only:
no create/edit/delete, no accounting-core changes, no write contract reopened.

## Scope

### In Scope
- `GET /api/catalogos/plan-contable` — thin route over `ListarPlanCompletoAsync`; payload `{cuenta, descripcion, nivel, esHojaImputable}`.
- Proveedores browse-all read path: paginated list mode (list all, server-side order by `proveedor`, `P00000` included), plus text search. Owner decision overrides explore Fork A(1).
- `GET /api/tipos-cambio?desde=&hasta=` — both params required, 400 if missing; bounded range. New read-only, clock-pure `ListarHistoricoAsync(DateOnly desde, DateOnly hasta, ct)` on `ITipoCambioRepository` + SQL adapter; rows carry `Origen` (both origins returned).
- SPA `catalogos/` feature: 3 lazy `ShellLayout` child routes under `authGuard` (`proveedores`, `plan-contable`, `tipo-cambio`), one data-access signal service per screen, shared `ui/` table components, `models/` per contract. Mirrors `inbox/`.
- `Proveedores` screen: columns código, razón social, RUC + search box. `Plan contable` screen: código, denominación + client-side filter. `Tipo de cambio` screen: fecha inicial/final filter, defaults = first day of current month / today.
- `spa-shell-nav` spec + `sidebar.spec.ts` delta: activate `nav-proveedores` + `nav-plan-contable`, ADD `Tipo de cambio` (7→8 entries/glyphs). Delta stated explicitly.
- `SmartNet.Api.Tests` coverage for all 3 endpoints in `CatalogoEndpointsTests` style (real DB, real cookie, 401, camelCase, 400 for TC).

### Out of Scope
- Other inert sidebar destinations (`Registro de compra`, `Errores y notificaciones`, `Sincronización`).
- Manual TC load / SBS scraping (stays in #4 admin CLI).
- New versioned SQL, new `GRANT`, any `dbo.*` write — all three SELECTs already permitted via `008`.
- Data migration, external accounting integration, `REGLAS.md`/plan-de-cuentas rule work.
- Adding a name index to `dbo.Proveedor` (ADR 0003 forbids schema drift on external catalogs).

## Capabilities

### New Capabilities
- `catalog-queries-api`: read-only `GET` endpoints for proveedores (browse-all + search), plan contable, and tipo de cambio history, incl. param validation and payload shape.
- `catalog-queries-spa`: three query-only SPA screens under `catalogos/`, their routes, data-access services, and filter/default behavior.

### Modified Capabilities
- `spa-shell-nav`: activate two existing inert entries and add `Tipo de cambio`; sidebar list grows 7→8.

## Approach

- **Plan contable**: one thin GET projecting the existing full-plan repository result; SPA filters client-side over the small static list.
- **Proveedores**: extend the catalog read path with a list mode (paginated, server-ordered). The endpoint serves both the #18 detalle picker and the new browse-all screen; the unindexed-scan tradeoff on `dbo.Proveedor` is flagged for `sdd-design` (pagination bounds the scan; ordering is server-side).
- **Tipo de cambio**: new port method + SQL adapter reading `fact.TipoCambio` by date range; route lives in `TipoCambioEndpoints` beside the existing POST. Method stays read-only and clock-pure (ADR 0019, `PurityScanTests`); it does not touch the #8 Venta-freeze path.
- **SPA**: extend `catalogos/` following the `inbox/` container/presentational + signals pattern; routes added additively so `app.routes.spec.ts` `arrayContaining` assertions still hold.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `SmartNet.Api/CatalogoEndpoints.cs` | Modified | Add plan-contable route; add/extend proveedores list mode |
| `SmartNet.Api/TipoCambioEndpoints.cs` | Modified | Add bounded `GET /api/tipos-cambio` |
| `SmartNet.Domain` `ITipoCambioRepository` + Core | Modified | New `ListarHistoricoAsync` port method |
| `SmartNet.Infrastructure` SQL TipoCambio adapter | Modified | Implement history read |
| `SmartNet.Api.Tests/CatalogoEndpointsTests.cs` (+ TC tests) | Modified/New | Endpoint contract coverage |
| SPA `src/app/catalogos/**` | New | 3 feature pages, services, shared table UI, models |
| SPA `app.routes.ts` | Modified | 3 lazy guarded child routes |
| SPA `shared/shell-layout/sidebar/sidebar.ts` + `.spec.ts` + `.css` | Modified | Activate 2 + add 1 entry/glyph |
| `openspec/specs/spa-shell-nav` | Modified | Nav delta |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Unindexed scan on `dbo.Proveedor` browse-all | Med | Mandatory pagination + server-side order; no schema change; flag scan cost for design |
| `spa-shell-nav`/`sidebar.spec.ts` reopened a 3rd time; reviewer "restores" old rules | Med | State delta explicitly in spec + PR; cite memory `shell-nav-canvas-replica` |
| New `ITipoCambioRepository` method drifts onto accounting-critical path | Low | Read-only + clock-pure; `PurityScanTests` guards Core; no `SqlUnidadDeTrabajo` involvement |
| New `/api/*` routes not auto-covered by `integration-spa-api` harness | High | Add `SmartNet.Api.Tests` cases; re-run harness + update its report manually |
| Sidebar 8th glyph pushes `sidebar.css` past 6kB warn cap | Low | Reuse existing glyph/token pattern; check `angular.json` budget before merge |
| TC history route called without params / huge range | Med | Require `desde` + `hasta` (400 if missing); bound range in endpoint |
| Total authored lines approach/exceed 800 budget | Med | 3 chained PRs (see Delivery) |

## Delivery Slicing

Chained PRs (owner accepts chaining):
1. **Plan contable** — GET route + SPA screen + route + tests. Smallest, no port change.
2. **Proveedores** — list-mode read path + browse-all screen + route + tests.
3. **Tipo de cambio** — Core port method + SQL adapter + bounded GET + screen + route + `spa-shell-nav`/sidebar delta + tests.

Each slice: autonomous scope, own verification (unit + `SmartNet.Api.Tests` + SPA specs), independent rollback.

## Rollback Plan

Per slice: revert the slice PR. Endpoints and routes are additive; removing them restores prior behavior. No schema, GRANT, or data changes to undo. Sidebar entries revert to inert via the spec/`sidebar.ts`/`.spec.ts` revert in slice 3.

## Dependencies

- BACKLOG #3 (catalogos read), #4 (TipoCambio table + manual load), #21 (sidebar shell). All delivered.
- `usr_api` SELECT on `dbo.Proveedor`, `dbo.CuentaContable`, `fact.TipoCambio` — already granted via `008`.

## Success Criteria

- [ ] `Proveedores` and `Plan contable` sidebar entries navigate to working screens; `Tipo de cambio` entry exists and navigates.
- [ ] Proveedores screen lists all proveedores (incl. `P00000`) with código/razón social/RUC + working search.
- [ ] Plan contable screen shows código/denominación with working filter.
- [ ] TC screen defaults to first-of-month → today; `GET /api/tipos-cambio` returns 400 without both params.
- [ ] All 3 endpoints return 401 unauthenticated and camelCase payloads; covered by `SmartNet.Api.Tests`.
- [ ] `app.routes.spec.ts`, `sidebar.spec.ts`, `PurityScanTests`, integration harness all green.
- [ ] No new versioned SQL, no new GRANT, no `dbo.*` write in the diff.

## Proposal question round (resolved by project owner 2026-08-30)

1. **CONFIRMED** — Proveedores browse-all shares ONE endpoint with the #18 picker (list mode added), not a separate route.
2. **CONFIRMED** — Proveedores list is server-paginated with "load more" like the picker (not a full unpaged dump).
3. **CONFIRMED** — TC screen shows both `SBS` and `MANUAL` rows for each date, no origin filter/selector in this slice.
4. **CONFIRMED** — Plan contable returns the full plan in one response (no pagination); small and static.
5. **RESOLVED** — Design-handoff gap for the new `Tipo de cambio` screen: no dedicated brief will be added. `sdd-design` models the TC screen with the same visual language as the Proveedores / Plan contable screens (table + filters). The ⚠ handoff still governs the Proveedores and Plan contable screens.

## Scope expansion (project owner, 2026-08-30, after first design pass)

The first `sdd-design` pass proposed trimming the canvas footer to "load more" and dropping export/sort. The owner overrode both. Spec and design are regenerated with:

6. **Full pagination** (canvas-faithful): `Anterior/Siguiente · Página X de Y` + rows-per-page selector. The proveedores endpoint payload gains a `total` (via `COUNT(*)`, a second unindexed scan on `dbo.Proveedor`, accepted). Contract `{ resultados, hayMas }` → `{ resultados, total, pagina, tamano }`.
7. **Sortable column headers** on all 3 screens. Proveedores: server-side sort (`proveedor`, `ruc`, `codigo`). Plan contable + Tipo de cambio: client-side sort.
8. **Export to Excel** on all 3 screens: a server-side endpoint per catalog returning a real `.xlsx` of the full filtered set. Requires a new backend Excel library (e.g. ClosedXML) — `sdd-design` evaluates whether this needs a new ADR.
9. `GET /api/tipos-cambio`: max span 366 days; 400 if exceeded or if `desde`/`hasta` missing.

Line estimate rises well above the earlier ~1,510; delivery is `size:exception` across additional stacked PRs, none exceeding 400 lines.
