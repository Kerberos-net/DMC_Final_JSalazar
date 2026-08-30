# Exploration — item-21-bandeja-shell-nav

macOS sidebar navigation shell + BACKLOG #21 (enriched bandeja data + 4 summary
counters). Scope C, bundled by user request. Artifact store: hybrid (this file +
Engram topic `sdd/item-21-bandeja-shell-nav/explore`, obs #223).

## Current state — SPA shell & routing

- `app.ts` is now a bare `<router-outlet>` (this session's refactor, uncommitted).
- `shared/shell-layout/shell-layout.ts` = `ShellLayout`: header (marca + "GF" logo
  badge + theme `<select>`), then `<router-outlet>`. **No sidebar.**
- `app.routes.ts`: `/login` top-level OUTSIDE `ShellLayout` (guarded by
  `app.routes.spec.ts`). `path: ''` → `component: ShellLayout`, children
  `bandeja` / `detalle/:id` / `configuracion` (all `authGuard`) + `''` redirect to
  `bandeja`.
- **Real nav targets today: ONLY `bandeja` + `configuracion`.** `detalle/:id` is a
  drill-in, not nav. No routes for Registro de compra / Proveedores / Plan
  contable / Sincronización. Feature folders: `inbox`, `detalle`, `configuracion`,
  `login`, `shared`. `configuracion` is a generic `fact.Configuracion` key/value
  editor, not an integraciones/sync dashboard.

## CSS tokens & guard tests

- `styles.css` `@layer tokens, base, primitives`; component `styleUrl` OUTSIDE
  layers. Private `--azul-*` ramp + semantic aliases.
- `--fondo-sidebar` already exists (`#eeeef1` light / `#232326` dark), already one
  of 4 SUPERFICIES in `contraste.spec.ts`.
- `paleta.spec.ts` parses `styles.css`: blue-literal count ≤ 6
  (regex `#0071e3|#0a63c9|#409cff`); state-role tokens must `===` existing
  `--error-ink` / `--alerta-ink` (no new hue); handoff hexes
  `#d70015|#c93400|#ff453a|#ff9f0a` forbidden; `--fs-*` integer px; Segoe first.
- `contraste.spec.ts`: every `TINTAS_TEXTO` token ≥ 4.5:1 over all 4 surfaces both
  themes; `TINTAS_NO_TEXTO` ≥ 3:1. A new "text on sidebar" token ⇒ must add to the
  array + pass AA.
- `angular.json` prod budget `anyComponentStyle` 4kB warn / 8kB error.
  `shell-layout.css` tiny now; full sidebar + div-icons can approach 4kB.
- Existing components DO use inline `<svg>` (`.alerta svg` / `.banner svg`).
  DESIGN.md "no SVG, glyphs from `<div>`" is NOT currently enforced — decision
  point.

## Specs

- `openspec/specs/spa-visual-bandeja/spec.md` "Out of Scope" explicitly defers to
  #21: summary cards + rich columns (proveedor name, monto, moneda, número, tipo,
  fecha emisión, glosa, TC, base, IGV). Freezes #13 query + `inbox.service.ts` +
  `chipsDe()`.
- Archived `spa-theme-toggle` delta: sidebar redesign out of scope for #18
  (user decision 5).
- Derived Estado chip precedence (`inbox-list.ts` `chipEstadoDe`, FIRST MATCH):
  1. DESCARTADO
  2. `errores > 0`
  3. `indicadores && (esProveedorGenerico || posibleDuplicado)`
  4. PROMOVIDO
  5. PENDIENTE

## API / #21

- `BandejaEndpoints.cs` `GET /api/bandeja` params `estado, desde, hasta,
  proveedor, pagina, orden`; thin delegator → `IBandejaRepository` →
  `PaginaBandeja<BandejaItem>`.
- `IBandejaRepository.cs`: `FiltrosBandeja`, `BandejaItem`, `ErrorProcesamiento`,
  `PaginaBandeja<T>` records.
- `SqlBandejaRepository.cs`: one multi-resultset batch over `fact.InboxEvent ie
  LEFT JOIN fact.Factura f`. Already projects `f.ProveedorCodigo`,
  `f.RucProveedor`, 5 indicator flags, errors sub-select, reprocesar window.
  `FiltroWhere` default view = `EstadoConsumo='PENDIENTE' OR EXISTS(non-OBSOLETO
  ProcesamientoError)` ⇒ PROMOVIDO/DESCARTADO excluded unless `estado` filter set.
  Proveedor filter already has `JSON_VALUE(ie.Payload,
  '$.comprobante.rucProveedor')` fallback.
- SPA mirror `src/app/inbox/models/bandeja-item.model.ts`: `BandejaItem`
  discriminated union on `origen` `FACTURA` | `INCIDENCIA`; `PaginaBandeja<T>`.

### #21 enriched columns (all confirmed in schema `005_negocio.sql`)

| Column | Source | Notes |
|---|---|---|
| proveedor display name | `dbo.Proveedor.proveedor` NVARCHAR(80), join `pr.codpro = f.ProveedorCodigo` | `usr_api` HAS `GRANT SELECT ON dbo.Proveedor` (008 line 149) |
| monto | `fact.Factura.TotalOrig` DECIMAL(18,2) | |
| moneda | `fact.Factura.Moneda` CHAR(3) | |
| numero | `fact.Factura.Numero` VARCHAR(20) nullable | |
| tipo comprobante | `fact.Factura.TipoComprobante` CHAR(2) | CODE ONLY; no catalog for display names granted (`dbo.Origen` is libro-origen not comprobante). **OPEN** |
| fecha emisión | `fact.Factura.FechaEmision` DATE | |

All on `fact.Factura` (already LEFT JOINed) ⇒ null for INCIDENCIA / not-promoted;
could fall back to `ie.Payload` JSON.

### Per-estado aggregate (Pendientes / Validadas / Con error / Alertas)

- Count over the FULL filtered set independent of pagination — new SELECT in the
  batch. Needs `fact.ProcesamientoError` (`usr_api` SELECT via 018; 008 re-applies
  cross-DENY — gotcha) + indicator flags.
- **TENSION:** default `FiltroWhere` excludes PROMOVIDO/DESCARTADO so a naive
  "Validadas" is always 0 — the aggregate must run over a WIDER predicate than the
  list.
- "Alerta" def: first-match (mutually exclusive w/ Error) vs independent count —
  **OPEN**.
- Envelope: add `PaginaBandeja<T>.resumen` sibling field vs separate
  `GET /api/bandeja/resumen` — **OPEN**.

## Tests

- API: `SmartNet.Api.Tests/BandejaEndpointsTests.cs` (E2E `WebApplicationFactory` +
  real `fact_test_<guid>`, cookie, `BandejaTestDataHelper`).
  `SqlBandejaRepositoryTests` in `SmartNet.Inbox.Infrastructure.Tests` impersonates
  `usr_api` via `TestDatabaseFixture.ExecuteAsUserAsync` — catches a missing
  `dbo.Proveedor` grant.
- SPA: `inbox.service.spec.ts`, `inbox-list.spec.ts`, `inbox-page.spec.ts`,
  `paleta.spec.ts`, `contraste.spec.ts`, `shell-layout.spec.ts`,
  `app.routes.spec.ts`, `app.spec.ts`.
- `integration-spa-api` harness applies (contract change); needs local SQL Server
  else BLOCKED.
- Commands verified: SPA `npm test` (Vitest) + `npm run lint` (tsc, no ESLint) in
  `SmartNet/SmartNetWeb`; API `dotnet test SmartNet.sln` (needs SQL Server).
  ADR 0009 / 0008 / 0003.

## Approaches

1. One bundled change, single PR — 600–1000+ lines, blows the 800-line
   `ask-on-risk` budget, mixes visual + API skill sets. **Reject.**
2. One SDD change, 3 chained PR slices: (a) sidebar shell, (b) API + SQL contract
   widening, (c) SPA enriched columns + summary cards on (b). **RECOMMENDED.**
3. Split into two SDD changes (shell-nav only; #21 data separately) — cleanest
   review, matches BACKLOG's own #20/#21 split; contradicts user "together".
   **Fallback.**

## Recommendation

Approach 2. Proceed to `sdd-propose` with delivery strategy `ask-on-risk`, plan
chained PR slices.

## Risks

- Review budget far exceeds 800 lines bundled — chained PRs mandatory.
- ADR 0003 partition: new `dbo.Proveedor` join must be proven under the real
  `usr_api` login (`SqlBandejaRepositoryTests`). Grant present but re-running 008
  isolated re-applies cross-DENYs.
- Contract drift `BandejaItem` / `PaginaBandeja<T>` `.cs` ↔ `.ts` (discriminated
  union).
- "Validadas" counter structurally 0 under the default list predicate — aggregate
  needs its own wider query; wrong = misleading dashboard numbers.
- `spa-visual-bandeja` spec freezes #13 query + `inbox.service.ts`; #21
  deliberately breaks the freeze — both specs must be updated.
- CSS 4kB component-style budget; `paleta` / `contraste` specs RED on a new hue or
  an un-aliased literal.
- DESIGN.md "no SVG" conflicts with existing `.alerta svg` / `.banner svg` — needs
  a ratified decision.
- `tipo de comprobante` has no display-name catalog granted to `usr_api`.
- `main` has an uncommitted shell-layout refactor; this change builds on it.

## Open questions (for the proposal question round)

- Exact nav items + order; targets without routes (Registro de compra,
  Proveedores, Plan contable, Sincronización) — hide / disabled / out of scope?
- Collapsed vs expanded sidebar in scope for the first cut? default state?
  persisted per-viewer (localStorage like theme? `fact.Configuracion`? no)?
- 4 counter cards: current filtered view or global totals? double as filter
  shortcuts?
- "Alerta" aggregate: first-match precedence (mutually exclusive w/ Error) or
  independent count?
- Enriched row columns REPLACE the minimal row or additive?
- Bundled vs split vs chained slices?
- Icons `<div>` constructs vs existing inline `<svg>`; ~2 glyphs needed now
  (inbox, gear); is a shared `shared/icon/` primitive warranted?
- Envelope widening `PaginaBandeja.resumen` vs separate endpoint?
