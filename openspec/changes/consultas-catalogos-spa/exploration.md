# Exploration: Read-only catalog queries in the SPA (BACKLOG #22) — change `consultas-catalogos-spa`

## Scope (BACKLOG.md item #22; ⚠ context = design handoff for the catalog screens ONLY, NOT REGLAS.md/plan de cuentas)
Three query-only screens + their `GET /api/*` endpoints over repos that already exist. No create/edit/delete anywhere.
No new versioned SQL, no new GRANT — `usr_api` (member of role `fact_api`) already has SELECT on all three tables
(`008_usuarios_y_permisos.sql`; `dbo.Proveedor` + `dbo.CuentaContable` = ADR 0003 external read-only catalogs,
`fact.TipoCambio` granted SELECT,INSERT,UPDATE). Manual TC load stays #4's admin CLI. Depends on #3, #4, #21.

## Current State — API
- Proveedores: `GET /api/catalogos/proveedores?q=&pagina=` exists (`CatalogoEndpoints.cs`, BACKLOG #18 picker) — min query len 2,
  fixed page size 20, `P00000` excluded, `proveedor LIKE OR rucpro LIKE`, order by `proveedor`, payload `{ resultados[], hayMas }`.
  Picker != catalog: blank/short query returns empty (no "list all").
- Plan contable: NO endpoint. `ICuentaContableRepository.ListarPlanCompletoAsync` already returns full plan
  (`cuenta, descripcion, nivel, ctarefleja, ctapuente`; `CuentaContable.EsHojaImputable => Nivel is null`). Need a GET route only.
- Tipo de cambio: only `POST /api/tipos-cambio` (manual load). `ITipoCambioRepository` has only `ObtenerVigenteAsync` /
  `CargarManualAsync`. Need NEW read method (histórico by date range) on port + SQL adapter + GET route.
- `Program.cs` already registers all 3 repos (singletons) and calls `app.MapCatalogoEndpoints()` + `app.MapTipoCambioEndpoints()`.
- `fact.TipoCambio` cols: Fecha, Origen('SBS'|'MANUAL'), Compra, Venta DECIMAL(12,6), FechaConsulta, CargadoPorUsuarioId?, CargadoEn; PK (Fecha,Origen).
- Domain records already Spanish-named: `Proveedor(Codigo,Nombre,CodigoTipoDocumento,Ruc)`,
  `CuentaContable(Cuenta,Descripcion,Nivel?,CtaReflejaCodigo?,CtaPuenteCodigo?)`, `TipoCambio(Fecha,Origen,Compra,Venta,FechaConsulta)`.
- Tests: `CatalogoEndpointsTests.cs` (7 cases, real DB via `SmartNetApiFactory`), `SqlProveedorRepositoryTests`,
  `SqlCuentaContableRepositoryTests`, `SqlTipoCambioRepositoryTests`.

## Current State — SPA
- `catalogos/` feature folder exists but only has `data-access/proveedor.service.ts` (+ `proveedor.model.ts`) — debounced search
  server-state for the detalle picker (signals: resultados/hayMas/buscando, `firstValueFrom(http.get('/api/catalogos/proveedores'))`).
  No `feature/` or `ui/` under `catalogos/` yet.
- Mirror pattern: `inbox/` (data-access signal service; `feature/inbox-page` container owns filter signals + `effect()`→fetch; `ui/` presentational; `models/`).
- Routing `app.routes.ts`: `login` top-level; `''`→`ShellLayout` children `bandeja`, `detalle/:id`, `configuracion`, each `canActivate:[authGuard]`, lazy `loadComponent`.
  `app.routes.spec.ts` asserts `arrayContaining` (additive-safe) + auth guard on every non-empty child.
- Sidebar `shared/shell-layout/sidebar/sidebar.ts`: hardcoded `primarios` (Bandeja principal, Registro de compra, Proveedores, Plan contable)
  + `utilitarios` (Errores y notificaciones, Sincronización, Configuración). `DestinoNav.ruta?` optional → no route renders inert (`aria-disabled`).
  `sidebar.spec.ts` asserts EXACT 7-item ordered list + only nav-bandeja/nav-configuracion are `<a>`, 7 glyphs. #22 must edit this spec
  (canvas-replica `spa-shell-nav` spec reopened AGAIN): activate nav-proveedores + nav-plan-contable + ADD `Tipo de cambio` (7→8 entries/glyphs).
  `angular.json` anyComponentStyle 6kB warn / 8kB error; `sidebar.css` ~5.3kB.

## Integration harness
`SmartNet/harnesses/integration-spa-api` (skill): SPA↔API HTTP seam over real `WebApplicationFactory<Program>` + `fact_test_<guid>` + real cookie.
In-scope flows today = session + bandeja/detalle only; new `/api/*` routes NOT auto-covered. Guardrails: no new deps, no test writes unless asked.
#22 endpoint contract (route, method, 401, camelCase payload) → cover with new `SmartNet.Api.Tests` cases in `CatalogoEndpointsTests` style; re-run harness + add flow to its report manually.

## Forks
- **A. Proveedores endpoint**: (1) REUSE #18 picker as-is (search box, min 2 chars) — Low effort, SPA-only, proven, but no browse-all + P00000 hidden.
  (2) add list mode — Medium, unindexed scan risk, couples 2 consumers. → Recommend (1).
- **B. Plan contable**: one thin `GET /api/catalogos/plan-contable` → `ListarPlanCompletoAsync`, project `{cuenta,descripcion,nivel,esHojaImputable}`,
  client-side filter in SPA over full (small/static) list. Low effort.
- **C. Tipo de cambio histórico**: new port method `ListarHistoricoAsync(DateOnly desde, DateOnly hasta, ct)` returning both origins per date
  (row carries `Origen`, no server-side origin filter). Route `GET /api/tipos-cambio?desde=&hasta=` — MUST bound range / reject missing params → 400.
  Put route in `TipoCambioEndpoints` (same resource as POST). Medium effort (Core + adapter + 2 test files + endpoint).
- **D. SPA structure**: one `catalogos/` feature folder, 3 feature pages (proveedores/plan-contable/tipo-cambio), shared `ui/` table comps,
  one data-access service per screen (reuse existing `ProveedorService`), `models/` per contract. Mirrors `inbox`.
- **E. Delivery (800-line budget)**: 3 chained PRs — (1) Plan contable, (2) Proveedores screen, (3) Tipo de cambio (Core+adapter+endpoint+screen+NEW sidebar entry).
  Total may approach/exceed 800 authored lines (3 screens + specs + sidebar spec rewrite) → chained PRs recommended.

## Recommendation
Proceed to `sdd-propose`. Thin, additive, read-only: Proveedores screen reuses picker endpoint; Plan contable = 1 thin GET + client filter;
Tipo de cambio = new read method + bounded GET + screen + new sidebar entry; SPA extends `catalogos/` folder; 3 new guarded lazy routes;
`spa-shell-nav` spec: activate 2 + add 1 (7→8). Slice into 3 chained PRs.

## Risks
- `spa-shell-nav` spec + `sidebar.spec.ts` reopened a 3rd time — memory `shell-nav-canvas-replica` warns reviewers not to "restore" prior rules; delta must be explicit.
- Design handoff (⚠) for catalog screens NOT yet located — find in `handoff/` / `DESIGN_BRIEF.md` before `sdd-design` or screens invent UX.
- Unbounded scans: `dbo.Proveedor` no name index (ADR 0003); `fact.TipoCambio` histórico route MUST bound date range + reject missing params.
- `ITipoCambioRepository` is on the accounting-critical path (#8 freezes Venta via SqlFacturacionStore/SqlUnidadDeTrabajo) — keep new method read-only + clock-pure (ADR 0019, PurityScanTests guards Core).
- Sidebar CSS budget — 8th glyph vs 6kB warn cap.
- No `dbo.*` writes / no new SQL / no new GRANT — all 3 reads already permitted; drift = wrong approach (CLAUDE.md rules 3 & 4).
- New `/api/*` routes not auto-covered by harness — add `SmartNet.Api.Tests` cases (real DB, real cookie, 401, camelCase payload).

## Ready for Proposal: Yes
Open input: design handoff for the catalog screens (needed before sdd-design). Sidebar/`spa-shell-nav` spec modified a 3rd time.
