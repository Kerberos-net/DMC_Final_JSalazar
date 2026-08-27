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

## Phase 4 — factura-form field grid (PR4, dep PR3) — DONE (7/7), COMMIT BLOCKED ON BUDGET

**Branch**: `pr4/item-18-factura-form-grid` (off `pr3/item-18-detalle-restructure`). Implemented + all tests green + lint clean + prod build no budget warning. **NOT committed** — authored diff is **516 (add 404 / del 112)** vs the ~400 review budget. `delivery_strategy: ask-on-risk` → stopped before committing per the apply prompt. Needs a delivery decision (accept `size:exception` for PR4, or split PR4a form.ts+html+css / PR4b specs, or PR4a editable-grid / PR4b read-only+derived+TC-faltante).

| Task | Status | Notes |
|---|---|---|
| 4.1 RED grid + editable fields spec | [x] | `.factura-form__grid`, `.campo__etiqueta` text above input, no `<label>` wrap; `campo-monto/-moneda/-fechaEmision/-proveedorCodigo` + `abrir-picker-proveedor` |
| 4.2 GREEN editable zero-backend fields | [x] | `onMonto` emits `{ totalOrig: Number \| null }`; others `onCampoInput`. New shared pure helper `src/app/shared/formato.ts` (`dosDecimales`, `importeOpcional`) + `formato.spec.ts` (asserts never-3-decimals). Monto input shows `dosDecimales(totalOrig)` = `118.00`. |
| 4.3 RED read-only + derived spec | [x] | `valor-base`/`valor-igv` = `—`; `valor-tc` tabular; `valor-mes`/`valor-dia` from `fechaContable`; no `glosa` |
| 4.4 GREEN read-only + derived rows | [x] | `<output class="campo__valor tabular-nums">`; `baseImponibleTexto`/`igvTexto` = `importeOpcional(null)` → `—` (Phase 6 adds projection); `tipoCambioTexto` = raw TC value / `No aplica` (PEN) / `0.00`; `mesContable`/`diaContable` = `fechaContable().slice(5,7)/(8,10)` ?? `—` |
| 4.5 RED per-field highlight + TC-faltante spec | [x] | `.campo--resaltado` count > 1 when `tieneCamposNoExtraidos`; `indicador-tc-faltante` for USD+null, absent for PEN / TC-present |
| 4.6 GREEN highlight + TC-faltante indicator | [x] | `campoResaltado = computed(() => factura().tieneCamposNoExtraidos)` bound on every OCR-sourced field (coarsest correct — see risks); `tipoCambioFaltante = computed(() => moneda !== 'PEN' && tipoCambioVenta() === null)` → red `.factura-form__tc-faltante` "se muestra 0.00" + `.campo__valor--alerta` on the TC row. `esInformativa` computed + `.alerta--informativa` block removed. |
| 4.7 REFACTOR budget + literals | [x] | `factura-form.css` ~1.6 kB, prod build no budget warning; all values `var(--token)` (`--space-*`, `--fs-12/13/14`, `--texto-secundario/-terciario/-principal`, `--error-ink/-fondo`, `--borde-sutil`, `--radio-input/-card`) |

### Necessary minimal deviation — detalle-page.html (+1 line)
Added `[fechaContable]="asiento()?.fechaContable ?? null"` to the `<app-factura-form>` binding. Task 4.3/4.4 require the derived `mes`/`día` rows to read `AsientoContable.FechaContable`; without this single additive binding the feature is dead in prod (tests-only green would be fake). No `.ts` / logic / layout change to `detalle-page`. `detalle-page.spec.ts` asiento fixture already carries `fechaContable`.

### Proveedor picker
No picker component/dialog exists anywhere in the SPA today. Rendered a presentational trigger (`abrir-picker-proveedor` button → `buscarProveedor` output, `[disabled]="!editable()"`). The container binding is intentionally NOT wired (would need a `detalle-page` method — out of PR4 scope). Picker dialog + lookup is follow-up work.

### tipoComprobante / numero
Rendered READ-ONLY (`disabled` inputs, `campo-tipoComprobante` / `campo-numero`) this phase — the editable PATCH delta is Phase 5 (4-layer .NET change + `factura.model.ts`).

### Phase 4 TDD Cycle Evidence

| Task | RED (test first, observed failing) | GREEN | REFACTOR |
|---|---|---|---|
| 4.1/4.2 | `factura-form.spec.ts` rewrite — build failed `TS2339: Property 'buscarProveedor' does not exist on type 'FacturaForm'`; grid/monto/picker assertions absent | grid template + `buscarProveedor` output + `onMonto` + `montoTexto` → pass | `formato.spec.ts` first failed on a float assertion (`(3.755).toFixed(2)` === `'3.75'` not `'3.76'`) → switched to decimal-count property assertion |
| 4.3/4.4 | `valor-base`/`valor-tc`/`valor-mes` testids absent; `tipo-cambio-venta` row logic replaced | `<output>` read-only rows + derived computeds → pass | values right-aligned tabular via `.campo__valor` + `tabular-nums` |
| 4.5/4.6 | `.campo--resaltado` count assertion (>1) fails (old single `.alerta--informativa` sentence); `indicador-tc-faltante` absent | `campoResaltado` binding on each field + dedicated TC banner; removed `esInformativa` | no color/font literals; prod build budget re-confirmed |

### Phase 4 Work Unit Evidence

| Evidence | Value |
|---|---|
| Focused test command + result | `npx ng test --no-watch --include='**/factura-form.spec.ts' --include='**/formato.spec.ts' --include='**/detalle-page.spec.ts'` → **3 files / 41 tests passed**. Full `npx ng test --no-watch` → **32 files / 278 passed** (was 266 at PR3; +12). `npm run lint` (tsc app + spec) clean. `npx ng build --configuration production` → no budget warnings (`detalle-page` lazy chunk 37.61 kB raw; styles 7.41 kB). |
| Runtime harness | N/A automated — SPA presentational component, no runtime boundary. `ng serve` → edit monto/moneda/fechaEmision/proveedor + foreign-currency-no-TC fixture deferred to reviewer per `ask-on-risk`. |
| Rollback boundary | Revert `detalle/ui/factura-form/{ts,html,css,spec.ts}`, the 1-line `detalle/feature/detalle-page/detalle-page.html` binding; delete `src/app/shared/formato.{ts,spec.ts}`. No other work touched; PR1/PR2/PR3 untouched; no .NET / model / token change. |

### Phase 4 risks / deviations
- **base imponible / IGV are `—` placeholders** — neither `FacturaRespuesta` nor `AsientoRespuesta` projects them. Phase 6 adds `AsientoRespuesta.basePEN/igvPEN`; the rows + `importeOpcional` helper are ready to bind then.
- **OCR highlight is invoice-wide, not per-field** — `FacturaRespuesta` only exposes `tieneCamposNoExtraidos: boolean`. Every OCR-sourced field carries `.campo--resaltado` together. True per-field granularity needs a backend field (not invented here).
- **TC row shows the raw projected `tipoCambioVenta`** (not `toFixed(2)`) — SBS publishes 3-decimal rates; forcing 2 decimals would misstate the rate the engine uses. The CONVENTIONS 2-decimal rule is applied to money (`monto`, base/IGV), not the exchange rate. Open question in design.md ("(venta)" label / TC display) still stands.
- **Label uses design D6 "(venta)"** not the spec's literal "TC compra" — ratified: ADR 0018 makes venta the operative rate; compra is unprojected reference data.
- **Authored diff 516 lines > 400 budget** — commit blocked pending orchestrator delivery decision.

## Phase 5 — .NET PATCH delta + tipoComprobante/numero binding (PR5, dep PR4) — DONE (9/9), COMMITTED `0642068`

**Branch**: `pr5/item-18-patch-tipocomprobante-numero` (off `pr4/item-18-factura-form-grid`). Strict TDD, RED→GREEN per task. All test suites green, `dotnet build SmartNet.sln` clean, SPA lint clean. **Committed `0642068`** — `feat(api): tipoComprobante/numero PATCH-editable (BACKLOG #18 PR5)`, 467 insertions / 30 deletions (~431 authored, ~212 of it new TDD test code). **Accepted `size:exception`** (maintainer-approved, over the 400 review budget). Not pushed, no PR opened. Only Phase 5 files staged; the pre-existing uncommitted Phase 8 `tasks.md` addendum + `notes/*.png` + `.codegraph/` + `SmartNet/Arquitectura SmartNet.png` left unstaged.

| Task | Status | Notes |
|---|---|---|
| 5.1 RED `ValidacionDeCorreccionTests.cs` | [x] | 8 cases: untouched→null, valid pair→null, blank/whitespace numero→invalid, >20 chars→invalid, exactly 20→null, tipo "99"/"1"/"Factura"→invalid, "01"/"03"/"07"→null. RED = `ValidacionDeCorreccion` / `CorreccionFactura.TipoComprobante` / `ResultadoComando.CorreccionInvalida` missing (compile) |
| 5.2 GREEN `ValidacionDeCorreccion.cs` (Facturacion.Core) | [x] | Pure (ADR 0019). `Validar(CorreccionFactura) → ResultadoComando?` (null = OK). New `SmartNet.Contable.Core/CodigoComprobante.cs` holds the single canonical {01→Factura, 03→Boleta, 07→NotaCredito} set; `SqlUnidadDeTrabajo.MapearTipoComprobante` refactored to `CodigoComprobante.Convertir` (no second list — design finding 5) |
| 5.3 RED `ServicioDeFacturasPhase2Tests.cs` +4 | [x] | ChangingTipoComprobanteAndNumero→2 audit rows (1 per field, `Accion=CORRECCION`); resending same tipo→0 rows; invalid tipo "99"→`CorreccionInvalida` + `GuardarFacturaAsync` never called + not committed; blank numero→same |
| 5.4 GREEN `CorreccionFactura.cs` + `ServicioDeFacturas.cs` + `ResultadoComando.cs` | [x] | 2 trailing `= null` params on `CorreccionFactura` (7-arg positional call sites still compile). `PatchAsync` calls `ValidacionDeCorreccion.Validar` BEFORE `AplicarCorreccion`/write → returns `CorreccionInvalida` with zero rows touched. `AplicarCorreccion` +2 blocks (`nameof(FacturaPersistida.TipoComprobante/Numero)`). New sealed `ResultadoComando.CorreccionInvalida(string Detalle)` |
| 5.5 GREEN `FacturaEndpoints.cs` | [x] | `CorreccionFacturaRequest` +2 trailing `= null` params + threaded through `ACorreccion()` |
| 5.6 RED `FacturaEndpointsTests.cs` +3 (real DB) | [x] | UpdatesTipoComprobanteAndNumero_AndGetReflectsThem; BlankNumero→422 problem+json + row unchanged; UnknownTipoComprobante "99"→422 + row unchanged. **Linchpin RED observed** by temporarily reverting the `SET` hunk: PATCH returned 200 but `fact.Factura` stayed `01\|F001-1` (persisted nothing) |
| 5.7 GREEN `SqlUnidadDeTrabajo.GuardarFacturaAsync` | [x] | `SET ... TipoComprobante = @tipoComprobante, Numero = @numero` + 2 `SqlParameter`s (`@numero` = `DBNull` guard, though `Numero` null never reaches here via PATCH). No versioned SQL, no new grant — `008_usuarios_y_permisos.sql` already grants object-level `UPDATE ON fact.Factura` (design-verified) |
| 5.8 RED `factura-form.spec.ts` | [x] | tipoComprobante = editable `<select>` with options `['01','03','07']`; numero = editable text input; both emit `{ tipoComprobante }` / `{ numero }` on `cambios`; both `[disabled]` when `editable=false`. `factura.model.ts` `CorreccionFacturaRequest` gains `tipoComprobante?`/`numero?`. RED = old test asserted `<input disabled>` |
| 5.9 GREEN `factura-form.{ts,html}` + `factura.model.ts` | [x] | `tiposComprobante` list (01 Factura / 03 Boleta / 07 Nota de crédito) in the component. HTML: `<select data-testid="campo-tipoComprobante" (change)="onCampoInput('tipoComprobante', …)">` with `@for` options + `[selected]`; `<input data-testid="campo-numero" (input)="onCampoInput('numero', …)">`. Both flow through the EXISTING generic `onCampoInput` → `cambios` → container `onCambiosFactura` → `borradorFactura` → PATCH on "Guardar avance". No new save contract, no `detalle-page` change |

### Deviation from design finding 3 ("the .NET delta is 4 layers")
The delta is really **6 small additive touches**, not 4: layers 1–4 as designed (DTO / `CorreccionFactura` / `PatchAsync` / `GuardarFacturaAsync` SET), PLUS (5) new `ResultadoComando.CorreccionInvalida` sealed case + `ProblemasDeNegocio.Map` arm + helper — the spec's mandated **422 `application/problem+json`** for an invalid value has no existing `ResultadoComando` case that maps to 422 without misusing `InvariantesIncumplidas` (which needs a fixed `InvarianteContable` enum value and would be semantically wrong — these are command-shape checks, not REGLAS.md §7 invariants); PLUS (6) new `SmartNet.Contable.Core/CodigoComprobante.cs` + `SqlUnidadDeTrabajo` refactor so the {01,03,07} set lives once (design finding 5: "do NOT enumerate a new list"). Both are additive, source-compatible, and independently revertible.

### Phase 5 TDD Cycle Evidence

| Task | RED (test first, observed failing) | GREEN | REFACTOR |
|---|---|---|---|
| 5.1/5.2 | `ValidacionDeCorreccionTests.cs` — compile failure: `ValidacionDeCorreccion`, `CorreccionFactura.TipoComprobante/Numero`, `ResultadoComando.CorreccionInvalida` all missing | guard + `CodigoComprobante` + `CorreccionFactura` params + `ResultadoComando` case → 8/8 pass | `CodigoComprobante` shared by infra `MapearTipoComprobante` (one list) |
| 5.3/5.4 | `ServicioDeFacturasPhase2Tests` +4 — compile failure (same missing symbols) | guard call in `PatchAsync` + 2 `AplicarCorreccion` blocks → 147/147 Facturacion.Core.Tests pass | guard runs before any write; invalid → no `GuardarFacturaAsync`, no commit |
| 5.5/5.6/5.7 | `FacturaEndpointsTests` +3 — linchpin: reverted the `GuardarFacturaAsync` SET hunk, ran `PatchFactura_UpdatesTipoComprobanteAndNumero` → **200 OK but `fact.Factura` = `01\|F001-1`** (`Assert.Equal` failed pos 1) | SET list + 2 params → 32/32 FacturaEndpointsTests pass | `@numero` DBNull guard kept defensive though PATCH never sends null |
| 5.8/5.9 | `factura-form.spec.ts` — `tipo.tagName` expected `SELECT`, got `INPUT`; `disabled` expected `false`, got `true` | `<select>` + editable numero input + model keys → 25/25 factura-form.spec.ts pass | reused generic `onCampoInput` — zero new emit handlers, zero container change |

### Phase 5 Work Unit Evidence

| Evidence | Value |
|---|---|
| Focused test command + result | `dotnet test SmartNet.Facturacion.Core.Tests` → **147 passed** (was 143; +4 Phase2, +8 new file = +12 tests). `dotnet test SmartNet.Contable.Core.Tests` → **41 passed**. `dotnet test SmartNet.Facturacion.Infrastructure.Tests` → **53 passed**. `dotnet test SmartNet.Api.Tests --filter FacturaEndpointsTests` → **32 passed** (was 29; +3). `npx ng test --no-watch` → **32 files / 281 passed** (was 278 at PR4; +4 factura-form tests, −1 replaced). `npm run lint` clean. `dotnet build SmartNet.sln` → 0 warnings / 0 errors. |
| Runtime harness | Real SQL Server via `SmartNetApiFactory` / `SmartNet.Db.TestBootstrap` — `PatchFactura_UpdatesTipoComprobanteAndNumero_AndGetReflectsThem` does PATCH `/api/facturas/{id}` (If-Match) then reads `fact.Factura` + GET `/api/facturas/{id}` and asserts `07\|FC01-42` round-trips. 422 tests assert `fact.Factura` row unchanged. |
| Rollback boundary | Revert `CorreccionFactura.cs`, `ResultadoComando.cs`, `ServicioDeFacturas.cs` (PatchAsync guard + 2 AplicarCorreccion blocks), `FacturaEndpoints.cs` (2 params), `ProblemasDeNegocio.cs` (1 arm + helper), `SqlUnidadDeTrabajo.cs` (SET + 2 params + MapearTipoComprobante), `factura.model.ts`, `factura-form.{ts,html}`; delete `CodigoComprobante.cs`, `ValidacionDeCorreccion.cs`, `ValidacionDeCorreccionTests.cs`; revert the test additions in `ServicioDeFacturasPhase2Tests.cs` / `FacturaEndpointsTests.cs` / `factura-form.spec.ts`. No PR1–PR4 file touched except `factura-form.*` (additive). Reverting the SPA does not break the API and vice versa (design migration note). |

### Phase 5 risks / deviations
- **Authored diff ≈ 431 > 400 budget** — maintainer accepted `size:exception` for PR5 (~212 lines are new test code); committed as one commit `0642068`.
- **`PosibleDuplicado` goes stale** — it is a STORED column computed at ingestion from the identity triple (`RucProveedor`, `TipoComprobante`, `Numero`). Editing `tipoComprobante`/`numero` via PATCH does NOT recompute it, so the duplicate banner can be stale until re-ingestion. KNOWN, deliberately out-of-scope (design Open Question 2) — not touched.
- **`Numero` can never be cleared to NULL via PATCH** — `null` = "untouched" (consistent with every other nullable field on the DTO). Accepted limitation, per spec.
- **6 touches not 4** — see "Deviation from design finding 3" above; both extra touches are additive and necessary for the spec's mandated 422.
- **`ValidacionDeCorreccion` does not check `numero` uniqueness / format** — only blank + length; the DB `IX_Factura_Identidad` and server-side duplicate gate remain authoritative for identity collisions.

## PR boundary
- PR1 / `size:exception` (~ +480 lines authored). Start: `main`. End: token layer + WCAG guard, `ng test` green.
- PR2 / `pr2/item-18-shell-header-login` off `pr1/item-18-token-layer-wcag-guard`. ~+170 authored lines (within 400 budget). Start: PR1 tip. End: shell GF badge + login recomposition, `ng test` 247 green, lint clean, prod build no budget warning.
- PR3 / `pr3/item-18-detalle-restructure` off `pr2/item-18-shell-header-login`. **Authored 579 (add 507 / del 72) — OVER the 400 budget. Uncommitted, staged.** Start: PR2 tip. End (pending): detalle-page restructure + `indicadores-factura` + asiento-lineas tabular, `ng test` 266 green, lint clean, prod build no budget warning. **Blocked: needs `size:exception` acceptance or a PR3a/PR3b split decision.**
- PR3 / `pr3/item-18-detalle-restructure` — committed `93ec9a7`.
- PR4 / `pr4/item-18-factura-form-grid` — committed `8720f63`.
- PR5 / `pr5/item-18-patch-tipocomprobante-numero` off `pr4` — committed `0642068`, accepted `size:exception` (~431 authored, ~212 tests). .NET 4-layer PATCH delta + `ValidacionDeCorreccion` guard + `CorreccionInvalida`→422 + `CodigoComprobante` + SPA `tipoComprobante`/`numero` editable binding. All suites green (Facturacion.Core 147, Contable.Core 41, Facturacion.Infra 53, Api FacturaEndpoints 32, SPA 281), lint clean, `dotnet build SmartNet.sln` clean. Not pushed, no PR.
- Next: Phase 6 (`AsientoRespuesta` basePEN/igvPEN read-only projection), depends on PR4.
