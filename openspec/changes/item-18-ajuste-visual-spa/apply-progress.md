# Apply Progress: item-18-ajuste-visual-spa

**Mode**: Strict TDD (test runner: `npx ng test --no-watch` — `@angular/build:unit-test` → vitest 4 / jsdom)
**Delivery**: chained PRs, `stacked-to-main`. PR1 is an accepted `size:exception` (tokens + WCAG guard are only meaningfully reviewable together).
**Branch**: `pr1/item-18-token-layer-wcag-guard` (off `main`)

## Phase 1 — Token layer + WCAG guard (PR1) — DONE (7/7)

| Task | Status | Notes |
|---|---|---|
| 1.1 RED `paleta.spec.ts` (node:fs read of styles.css) | [x] | Needed `@types/node` devDep + `"node"` in `tsconfig.spec.json` types |
| 1.2 GREEN `paleta.ts` (`leerTokens` + `componer` + `tokensPorTema`/`resolver`/`sinComentarios`) | [x] | Pure, zero I/O in module (ADR 0019 parity) |
| 1.3 RED `contraste.spec.ts` token-driven pair table | [x] | Every text/ink token × ALL FOUR surfaces, both themes |
| 1.4 GREEN `contraste.ts` accepts composited values | [x] | Added `contrasteSobre(color, fondoHex)` — passthrough hex / `componer` for rgba |
| 1.5 GREEN `styles.css` two-tier token layer | [x] | Private `--azul-600/700/400` ramp, role aliases, 4 warm surfaces, radii 8/12/16/20, hairline→prominente shadow scale, translucent hairline borders, Segoe-first integer px type scale |
| 1.6 GREEN ratified-exception comment at ramp | [x] | Documents decision 1 (accent reused for action + Pendiente chip + P00000) |
| 1.7 REFACTOR blue-literal + component-CSS guard | [x] | `grep` confirms no blue literal outside ramp; no hex and no `@layer` in any component CSS |

### WCAG ratified deviation (design.md D3 open question 1 — RESOLVED)
Dark `--accento-texto` = **`#409cff`** (`--azul-400`), NOT the design's `#0a84ff`. `#0a84ff` scores 3.82:1 on the raised dark surface `#2c2c2e` — fails AA. User ratified `#409cff` (handoff-native dark "pendiente" step; 4.92–6.01 across all four dark surfaces). The guard genuinely catches the regression (see TDD evidence).

### Token value adjustments for AA-on-4-surfaces (no visual intent change)
- Light `--fondo-app` `#f7f8fa`→`#f5f5f7`; added `--fondo-superficie-2` `#f0f0f2`, `--fondo-sidebar` `#eeeef1`.
- Light `--texto-secundario` `#5a6270`→`#44474e`; `--alerta-ink` `#b45309`→`#8a4300`; `--error-ink` `#b91c1c`→`#b3211f` (all to clear 4.5:1 on the two lower light surfaces).
- Dark `--fondo-*` → warm `#1c1c1e / #2c2c2e / #242426 / #232326`; `--alerta-ink` `#e8a33d`→`#f0b45f`, `--conflicto-ink` `#b79bf5`→`#c4aef8`.
- `--estado-pendiente-*` now accent-tinted (ratified); `--texto-terciario` added as non-text-only role (D3).
- Old names kept as aliases: `--radio-control`→`--radio-input`, `--radio-panel`→`--radio-card`, `--estado-pendiente-texto`→`var(--estado-pendiente-ink)`, `--borde-sutil` now translucent.

## TDD Cycle Evidence

| Task | RED (test first, observed failing) | GREEN | REFACTOR |
|---|---|---|---|
| 1.1/1.2 | `paleta.spec.ts` written first — 57 failing (tokens absent, module missing) | `paleta.ts` + `styles.css` tokens → pass | `sinComentarios()` extracted so prose `--x:` inside comments is never parsed as a token |
| 1.3/1.4 | `contraste.spec.ts` rewrite — failed incl. `--alerta-ink/--alerta-fondo` 4.48, `--accento-contraste` absent | `contrasteSobre` + palette values → pass | pair tables via `describe.each` flatMap |
| 1.5 (regression proof) | Temp-set dark `--accento-texto: #0a84ff` → `contraste -- tema oscuro > --accento-texto sobre --fondo-superficie >= 4.5:1` FAILS (3.82); 6 failing | reverted to `var(--azul-400)` → 240/240 pass | — |

## Work Unit Evidence

| Evidence | Value |
|---|---|
| Focused test command + result | `npx ng test --no-watch` → **30 files / 240 tests passed** (was 180; +60 palette/contrast). `npm run lint` (tsc app + spec) clean. |
| Runtime harness | N/A for PR1 — no runtime boundary; global stylesheet + pure TS guard. Visual smoke (`npm start`, both themes) deferred to reviewer per `ask-on-risk`. |
| Rollback boundary | Revert `src/styles.css`, `src/app/shared/paleta.ts`, `src/app/shared/paleta.spec.ts`, `src/app/shared/contraste.ts`, `src/app/shared/contraste.spec.ts`, `tsconfig.spec.json`, and the `@types/node` devDep in `package.json`/`package-lock.json`. No other work touched. |

## Phase 2 — Shell header + login (PR2, dep PR1) — DONE (5/5)

**Branch**: `pr2/item-18-shell-header-login` (off `pr1/item-18-token-layer-wcag-guard`)

| Task | Status | Notes |
|---|---|---|
| 2.1 RED `app.spec.ts` shell theme control | [x] | `<select>` with `['sistema','claro','oscuro']`; guards: no `button`, no "Apariencia" text, no `[data-testid="toggle-tema"]`; marca shows "GF" `[data-testid="logo-badge"]` badge (real RED) |
| 2.2 GREEN `app.html`/`app.css` | [x] | Added `.app-shell__logo` GF badge inside `.app-shell__marca`; tokens only (`--accento`, `--accento-contraste`, `--radio-input`, `--fs-12`). `<select>` theme control unchanged. |
| 2.3 RED `login-page.spec.ts` recomposition | [x] | 4 new specs: vertical order (badge→h1→subtitulo→usuario→clave→error-slot→enviar→pie) via `compareDocumentPosition`; placeholder inputs w/ `aria-label`, zero `<label>`; full-width `.login-page__enviar` submit "Ingresar"; error rendered inside `[data-testid="error-slot"]` with `role="alert"`, `.banner--error` absent |
| 2.4 GREEN `login-page.html`/`.css` | [x] | Removed `<label>` wraps + `.banner--error` block; placeholder-only inputs; GF badge, title "Gestor de Facturas de Compra", subtitle, always-present error slot (`role="alert"` only when message), full-width accent button, footer. CSS layout-only, consumes `--radio-modal`/`--sombra-prominente`/`--borde-sutil`, zero color/font literals. |
| 2.5 REFACTOR style budgets | [x] | `ng build --configuration production` → no budget warnings. `login-page.css` 1640 B, `app.css` 928 B (both < 4 kB warn). |

### Phase 2 TDD Cycle Evidence

| Task | RED (test first, observed failing) | GREEN | REFACTOR |
|---|---|---|---|
| 2.1/2.2 | `app.spec.ts` — "GF badge" assertion failed (no `[data-testid="logo-badge"]`); select/marca/no-button assertions pass as regression guards | added `.app-shell__logo` badge → pass | badge styled from tokens; select control untouched |
| 2.3/2.4 | `login-page.spec.ts` — 4 failing: order (subtitulo/pie/error-slot/logo-badge absent), placeholder inputs (2 `<label>` present), full-width submit (`.login-page__enviar` absent), inline error (`.banner--error` still emitted) | rewrote template + css → 11/11 pass | always-present slot, `role="alert"` only when populated; existing 3 auth-flow specs still green |

### Phase 2 Work Unit Evidence

| Evidence | Value |
|---|---|
| Focused test command + result | `npx ng test --no-watch --include='**/app.spec.ts' --include='**/login-page.spec.ts'` → **2 files / 11 tests passed** (was 4). Full `npx ng test --no-watch` → **30 files / 247 passed** (was 240; +7). `npm run lint` (tsc app + spec) clean. |
| Runtime harness | N/A automated — SPA visual/shell change, no runtime boundary. `ng serve` → `/login` both themes deferred to reviewer per `ask-on-risk`. `ng build --configuration production` succeeded, no budget warnings. |
| Rollback boundary | Revert `src/app/app.html`, `src/app/app.css`, `src/app/app.spec.ts`, `src/app/login/feature/login-page/login-page.{html,css,spec.ts}`. `login-page.ts` untouched. No other work affected. |

## Phase 3 — detalle-page restructure + indicadores-factura + asiento-lineas (PR3, dep PR1) — DONE (9/9), COMMIT BLOCKED ON BUDGET

**Branch**: `pr3/item-18-detalle-restructure` (off `pr2/item-18-shell-header-login`). Implemented + all tests green + lint clean + prod build no budget warning. **NOT committed** — authored diff is 579 (add 507 / del 72) vs the ~400 review budget. `delivery_strategy: ask-on-risk` → stopped before committing per the apply prompt. Needs a delivery decision (accept `size:exception` for PR3, or split PR3a component/PR3b detalle-page).

| Task | Status | Notes |
|---|---|---|
| 3.1 RED `indicadores-factura.spec.ts` | [x] | 5 specs: none/duplicado-only/P00000-only/TC-only/all-three; role=alert on duplicado+TC; RED = module missing |
| 3.2 GREEN `detalle/ui/indicadores-factura/*` | [x] | 4 files. Pure inputs `posibleDuplicado`/`esProveedorGenerico`/`tipoCambioFaltante`, zero logic. CSS token-driven (`--alerta-*`, `--accento-suave`, `--info-generico-ink`, `--error-*`, `--radio-card`), 1308 B |
| 3.3 RED/GREEN detalle-page header | [x] | `tituloDetalle` = `{tipoComprobante} - {numero} - {proveedorCodigo}`; `estadoPill` computed (pendiente/validada/descartada); `← Volver` → `Location.back()`; top-right `detalle-acciones` with Guardar/Validar; `<app-indicadores-factura>` between header and `.detalle-layout` |
| 3.4 RED `bloqueosValidar` gate | [x] | `computed<readonly string[]>` → `['DUPLICADO']` / `['PROVEEDOR_GENERICO']` / both; `puedeValidar` = length===0; `validar()` early-returns when `!puedeValidar()`; `httpMock.expectNone('/validar')` proves request never sent; `[disabled]="!puedeValidar()"`. No ack-checkbox. |
| 3.5 GREEN hoist banners + split | [x] | banners in container; `.detalle-layout` `grid-template-columns: 42% 1fr`; visor `position: sticky` REMOVED (not sticky per spec); form column `flex`, `align-items: start` |
| 3.6 GREEN fecha-corte-contable placement | [x] | moved from header actions into `.detalle-asiento` wrapper next to `<app-asiento-lineas>` |
| 3.7 RED `asiento-lineas.spec.ts` | [x] | 4 specs: Total row per-column 2-decimal (`118.00`), balanced pill "Cuadra", unbalanced "No cuadra", "+ Agregar línea" label. `createComponent` helper + historial test updated for new required `cuadre` input |
| 3.8 GREEN `asiento-lineas` tabular | [x] | `cuadre = input.required<Cuadre>()` (passed from `detalle-page` `calcularCuadre` — NOT recomputed); `<tfoot>` Total row (`formatearMonto` = `toFixed(2)`, never 3) + cuadre pill `data-testid="cuadre-pill"` radius `--radio-pill`; "+ Agregar línea" now an accent-text link (`--accento-texto`), keeps `data-testid="agregar-linea"` |
| 3.9 REFACTOR token follow-through | [x] | `visor-documento.css`: `--radio-panel` alias → canonical `--radio-card`, added `box-shadow: var(--sombra-hairline)`. `conflicto-banner.css` (536 B) / `historial-correccion.css` (795 B) already token-only — confirmed, no change needed. All 6 in-scope component CSS < 4 kB warn |

### factura-form banner removal (scope: "only removal of the banners that move to indicadores-factura")
Removed the `@if (esBloqueante())` block from `factura-form.html` + the now-unused `esBloqueante` computed from `factura-form.ts`. `esInformativa` (OCR-missing / afectación-no-verificada) stays — its per-field split is Phase 4. `factura-form.spec.ts`: the 2 positive `.alerta--bloqueante` assertions flipped to negative (`toBeNull`, "hoisted to indicadores-factura").

### Phase 3 TDD Cycle Evidence

| Task | RED (test first, observed failing) | GREEN | REFACTOR |
|---|---|---|---|
| 3.1/3.2 | `indicadores-factura.spec.ts` — build failed `TS2307: Cannot find module './indicadores-factura'` | component created → 5/5 pass | icons + tone classes token-driven; `[data-testid^="indicador-"]` count assertion for the all-three case |
| 3.3/3.5/3.6 | `detalle-page.spec.ts` header + placement specs — failed (`[data-testid="volver"]`/`detalle-titulo`/`estado-pill`/`indicador-*` absent; compile also blocked by new required `cuadre` input) | header + `<app-indicadores-factura>` + layout → pass | split ratio literal 42%/1fr; sticky removed |
| 3.4 | `bloqueosValidar` specs — `bloqueosValidar`/`puedeValidar` undefined; `validar` still dispatched under duplicado | computeds + early-return guard → `expectNone('/validar')` passes | named-list `computed<readonly string[]>` per design D5 |
| 3.7/3.8 | `asiento-lineas.spec.ts` totals/pill specs — `total-debe`/`cuadre-pill` absent; existing suite RED on missing required `cuadre` input (helper updated) | `<tfoot>` + `cuadre` input → 4 new pass, 7 prior still green | `formatearMonto` pure 2-decimal helper extracted; agregar link |
| 3.9 | approval: existing `visor-documento`/`asiento-lineas` suites green before touching CSS | alias swap + shadow token; no behavior change | budgets re-confirmed via prod build |

### Phase 3 Work Unit Evidence

| Evidence | Value |
|---|---|
| Focused test command + result | `npx ng test --no-watch --include='**/detalle-page.spec.ts' --include='**/asiento-lineas.spec.ts' --include='**/factura-form.spec.ts' --include='**/indicadores-factura.spec.ts'` → **4 files / 45 tests passed**. Full `npx ng test --no-watch` → **31 files / 266 passed** (was 247; +19). `npm run lint` (tsc app + spec) clean. `npx ng build --configuration production` → no budget warnings (`detalle-page` lazy chunk 33.56 kB raw; component CSS all < 4 kB: detalle-page.css 1162 B, indicadores-factura.css 1308 B, asiento-lineas.css 1186 B). |
| Runtime harness | N/A automated — SPA layout/gate change, no runtime boundary. `ng serve` → factura detalle with duplicado / P00000 / foreign-currency-no-TC fixtures deferred to reviewer per `ask-on-risk`. |
| Rollback boundary | Revert `detalle/feature/detalle-page/{ts,html,css,spec.ts}`, `detalle/ui/asiento-lineas/{ts,html,css,spec.ts}`, `detalle/ui/factura-form/{html,ts,spec.ts}`, `detalle/ui/visor-documento/visor-documento.css`; delete `detalle/ui/indicadores-factura/`. No other work touched; PR1/PR2 untouched; no .NET / model / token change. |

## PR boundary
- PR1 / `size:exception` (~ +480 lines authored). Start: `main`. End: token layer + WCAG guard, `ng test` green.
- PR2 / `pr2/item-18-shell-header-login` off `pr1/item-18-token-layer-wcag-guard`. ~+170 authored lines (within 400 budget). Start: PR1 tip. End: shell GF badge + login recomposition, `ng test` 247 green, lint clean, prod build no budget warning.
- PR3 / `pr3/item-18-detalle-restructure` off `pr2/item-18-shell-header-login`. **Authored 579 (add 507 / del 72) — OVER the 400 budget. Uncommitted, staged.** Start: PR2 tip. End (pending): detalle-page restructure + `indicadores-factura` + asiento-lineas tabular, `ng test` 266 green, lint clean, prod build no budget warning. **Blocked: needs `size:exception` acceptance or a PR3a/PR3b split decision.**
- Next: delivery decision for PR3, then Phase 4 (PR4 factura-form field grid), depends on PR3.
