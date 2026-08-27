# Tasks: item-18-ajuste-visual-spa (BACKLOG #18 — Ajuste visual del diseño SPA)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~1740 across 6 slices (PR1 ~480, PR2 ~180, PR3 ~380, PR4 ~300, PR5 ~280, PR6 ~120) |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR1 → PR2 → PR3 → PR4 → PR5 → PR6 |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending (recommend feature-branch-chain: PR1→tracker, each later PR→predecessor) |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | Two-tier token layer + WCAG palette guard that reads styles.css | PR1 | `ng test --include='**/paleta.spec.ts' --include='**/contraste.spec.ts'` | `ng serve` visual smoke both themes | `src/styles.css`, `src/app/shared/paleta*.ts`, `contraste*.ts` |
| 2 | Shell header (`<select>` theme) + login-page recomposition | PR2 | `ng test --include='**/login-page.spec.ts' --include='**/app.spec.ts'` | `ng serve` → /login both themes | `app.html/.css`, `login-page/*` |
| 3 | detalle-page restructure + indicadores-factura + asiento-lineas tabular | PR3 | `ng test --include='**/detalle-page.spec.ts' --include='**/indicadores-factura.spec.ts' --include='**/asiento-lineas.spec.ts'` | `ng serve` → factura detalle (duplicado/P00000/TC fixtures) | `detalle-page/*`, `detalle/ui/indicadores-factura/*`, `asiento-lineas/*` |
| 4 | factura-form 2-col field grid, zero-backend fields, per-field `.campo--resaltado` | PR4 | `ng test --include='**/factura-form.spec.ts'` | `ng serve` → edit monto/moneda/fechaEmision/proveedor | `factura-form/*` |
| 5 | .NET PATCH delta (4 layers) + tipoComprobante/numero SPA binding | PR5 | `dotnet test --filter FullyQualifiedName~Correccion` then `ng test --include='**/factura-form.spec.ts'` | PATCH /api/facturas/{id} + GET roundtrip via `dotnet run` | `CorreccionFacturaRequest`, `CorreccionFactura.cs`, `ValidacionDeCorreccion.cs`, `ServicioDeFacturas.cs`, `SqlUnidadDeTrabajo.cs`, form binding |
| 6 | Additive `AsientoRespuesta` BasePEN/IgvPEN + read-only base/IGV display | PR6 | `dotnet test --filter FullyQualifiedName~Asiento` then `ng test --include='**/factura-form.spec.ts'` | GET /api/facturas/{id}/asiento roundtrip | `AsientoEndpoints.cs`, `asiento.model.ts`, form read-only rows |

## Phase 1: Token layer + WCAG guard (PR1 — satisfies spa-design-tokens, spa-theme-toggle tokens)

- [x] 1.1 RED: create `src/app/shared/paleta.spec.ts` — `node:fs` read of `src/styles.css`, assert `--azul-600/-700/-400` ramp exists and `--accento`, `--accento-texto`, `--estado-pendiente-ink`, `--info-generico-ink` alias to ramp. Fails (no paleta.ts / tokens).
- [x] 1.2 GREEN: create `src/app/shared/paleta.ts` — pure `leerTokens(css): Map<string,string>` + `componer(rgba, fondoHex): hex` alpha compositing. No I/O in module.
- [x] 1.3 RED: extend `contraste.spec.ts` — pair table driven by parsed tokens: white-on-`--accento`, `--accento-texto` over each of 4 surfaces, pendiente chip ink on tint, every new surface/status pair, BOTH themes, ≥ AA floor.
- [x] 1.4 GREEN: update `contraste.ts` to accept composited values from `componer()`.
- [x] 1.5 GREEN: edit `src/styles.css` — private `--azul-*` ramp (only blue literals), role aliases per D1, 4 surfaces (#1c1c1e/#2c2c2e/#242426/#232326), radii 8/12/16/20, shadow hairline→prominent, translucent hairline borders, Segoe-first integer type scale. Dark `--accento-texto` = `#409cff` (D3 ratified); light pendiente ink = `--azul-700`.
- [x] 1.6 GREEN: add ratified-exception comment at the ramp (decision 1 — accent reused for action + Pendiente chip + P00000; reviewer must not "fix").
- [x] 1.7 REFACTOR: `rg -- '--azul-600'` enumerates roles; confirm no blue literal outside ramp; component CSS stays layout-only, outside `@layer`.

## Phase 2: Shell header + login (PR2, dep PR1 — spa-visual-login, spa-theme-toggle)

- [x] 2.1 RED: `app.spec.ts` — header renders native `<select>` with light/dark/system options; no sun/moon toggle, no sidebar redesign.
- [x] 2.2 GREEN: update `app.html`/`app.css` header; tokens only.
- [x] 2.3 RED: `login-page.spec.ts` — card order (GF badge → title "Gestor de Facturas de Compra" → subtitle "Inicia sesion para revisar y validar facturas" → placeholder inputs → inline error slot → full-width "Ingresar" → footer "Credenciales verificadas contra SQL Server"); inputs have accessible names, no visible `<label>`; error uses validation-error token message not `.banner--error`.
- [x] 2.4 GREEN: update `login-page/*` template + styles; consume radius/elevation tokens, zero color/font literals.
- [x] 2.5 REFACTOR: verify component style budget (4kB/8kB) for `app` and `login-page`.

## Phase 3: detalle-page restructure (PR3, dep PR1 — spa-visual-detalle-validacion, pantalla-detalle-validacion)

- [x] 3.1 RED: `indicadores-factura.spec.ts` — new `detalle/ui/indicadores-factura/*` renders up to 3 full-width banners: duplicado strong amber, P00000 accent-blue informational-styled, TC faltante strong red "Se muestra 0.00"; each shows only when its `FacturaRespuesta`/asiento condition true.
- [x] 3.2 GREEN: create `detalle/ui/indicadores-factura/` presentational component (inputs only, no `esBloqueante`/`esInformativa` logic).
- [x] 3.3 RED: `detalle-page.spec.ts` — banners render in detalle-page container BETWEEN header and split, NEVER inside factura-form; page header = back "← Volver" + title `{tipoComprobante} - {numero} - {proveedor}` + estado pill (real value, "Pendiente"=accent) + top-right "Guardar avance"/"Validar".
- [x] 3.4 RED: `detalle-page.spec.ts` — `bloqueosValidar = computed<readonly string[]>` (DUPLICADO | PROVEEDOR_GENERICO); `puedeValidar()` false and request never sent for duplicado-only, P00000-only, and both; re-enables when all clear. No ack-checkbox.
- [x] 3.5 GREEN: implement `bloqueosValidar`/`puedeValidar` signals + `[disabled]="!puedeValidar()"`; hoist banners to container; static split visor 42% (not sticky) / form flex:1 top-aligned.
- [x] 3.6 GREEN: move "Fecha de corte contable" control adjacent to asiento block (decision 5.1), not header.
- [x] 3.7 RED: `asiento-lineas.spec.ts` — tabular grid Cuenta/Debe/Haber, Debe/Haber right-aligned tabular-nums, Total row per column, "+ Agregar línea" accent-text link, cuadre pill from `cuadre` (detalle-page.ts), pill radius token.
- [x] 3.8 GREEN: implement `asiento-lineas` tabular layout.
- [x] 3.9 REFACTOR: token follow-through for `visor-documento`, `conflicto-banner`, `historial-correccion`; confirm CSS budgets incl. `historial-correccion`.

## Phase 4: factura-form field grid (PR4, dep PR3 — pantalla-detalle-validacion)

- [ ] 4.1 RED: `factura-form.spec.ts` — 2-col grid, label as secondary text above input; renders `monto`, `moneda`, `fechaEmision`, `proveedorCodigo` + picker; each emits `cambios` → `borradorFactura`.
- [ ] 4.2 GREEN: implement editable zero-backend fields; money via shared pure 2-decimal helper (never 3).
- [ ] 4.3 RED: `factura-form.spec.ts` — read-only tabular rows for `base imponible`, `IGV`, `TC (venta)` + SBS note from `asiento().tipoCambioVenta`; derived `mes`/`día` computed over `asiento().fechaContable`; `glosa` absent.
- [ ] 4.4 GREEN: implement read-only + derived display rows (base/IGV placeholder until PR6).
- [ ] 4.5 RED: `factura-form.spec.ts` — per-field `.campo--resaltado` bound to real OCR-missing data (not one generic sentence); dedicated TC-faltante indicator when `moneda !== 'PEN' && asiento()?.tipoCambioVenta === null`.
- [ ] 4.6 GREEN: implement per-field highlight + TC-faltante indicator; remove `esBloqueante`/`esInformativa` from factura-form.
- [ ] 4.7 REFACTOR: verify `factura-form` style budget; no color/font literals.

## Phase 5: .NET PATCH delta + binding (PR5, dep PR4 — api-facturas)

- [ ] 5.1 RED: core test — `ValidacionDeCorreccion.Validar(CorreccionFactura)` rejects blank `numero`, `tipoComprobante` not 2-char / outside accepted domain enum, `numero` > 20 chars; returns `null` when untouched.
- [ ] 5.2 GREEN: create `ValidacionDeCorreccion.cs` — pure guard, no DB/HTTP/clock (ADR 0019).
- [ ] 5.3 RED: core test — `AplicarCorreccion` emits one `AuditoriaCorreccion` row (`Accion=CORRECCION`) per changed field for `TipoComprobante`/`Numero`; resend of same value audits nothing.
- [ ] 5.4 GREEN: add trailing `string? TipoComprobante = null, string? Numero = null` to `CorreccionFactura.cs` (source-compatible) + 2 `AplicarCorreccion` blocks in `ServicioDeFacturas.cs` with guard call.
- [ ] 5.5 GREEN: add 2 trailing `= null` params to `CorreccionFacturaRequest` + `ACorreccion()` mapping in `FacturaEndpoints.cs`.
- [ ] 5.6 RED: API contract test — PATCH with `tipoComprobante`/`numero` returns 200 AND GET reflects new values (currently persists nothing); 7-arg positional `CorreccionFactura` construction still compiles.
- [ ] 5.7 GREEN: add both columns to `SqlUnidadDeTrabajo.GuardarFacturaAsync` UPDATE `SET` list + 2 SqlParameters (verified omitted today). No versioned SQL, no new grant (008 covers object-level UPDATE).
- [ ] 5.8 RED: `factura-form.spec.ts` — `tipoComprobante`/`numero` render editable and emit `cambios`; `factura.model.ts` `CorreccionFacturaRequest` gains `tipoComprobante?`/`numero?`.
- [ ] 5.9 GREEN: wire the two fields into `borradorFactura` → PATCH payload.

## Phase 6: read-only base/IGV projection (PR6 conditional, dep PR4 — api-facturas / pantalla)

- [ ] 6.1 RED: API contract test — `AsientoRespuesta` includes `basePEN`/`igvPEN` from `AsientoContable`; GET `/api/facturas/{id}/asiento` returns them.
- [ ] 6.2 GREEN: add `BasePEN`, `IgvPEN` to `AsientoRespuesta` in `AsientoEndpoints.cs` (already on `AsientoContable`; additive only).
- [ ] 6.3 RED: `factura-form.spec.ts` — read-only `base imponible`/`IGV` rows show formatted tabular values from `asiento().basePEN/igvPEN`.
- [ ] 6.4 GREEN: add `basePEN`/`igvPEN` to `asiento.model.ts`; bind the read-only rows.

## Phase 7: Verification

- [ ] 7.1 Run full `ng test` — palette guard, login, detalle-page, indicadores, asiento-lineas, factura-form all green both themes.
- [ ] 7.2 Run full `dotnet test` — correccion core, validation guard, API contract green.
- [ ] 7.3 Confirm ratified accent-reuse exception intact; `rg -- '--azul-'` shows literals only in ramp.
- [ ] 7.4 Confirm no versioned SQL added, data partition (ADR 0003) untouched, money helper never 3-decimal.
- [ ] 7.5 Update `BACKLOG.md` #18 status.

## Threat Matrix

N/A — design records no routing, shell, subprocess, VCS/PR automation, executable-file classification, or process-integration boundary. API delta is an additive DTO field. No RED threat-case tasks required.
