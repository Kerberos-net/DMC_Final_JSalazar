# Tasks: Bandeja shell navigation + enriched bandeja data (BACKLOG #21)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | PR1 ~700 · PR2 ~430 · PR3 ~830 · total ~1960 |
| Exceeds 800-line review budget | Yes (each slice 430–830; total ~1960) |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR1 (shell nav, SPA-only) -> PR2 (API + SQL contract) -> PR3 (SPA consumption, base = PR2) |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending (orchestrator asks user: stacked-to-main vs feature-branch-chain) |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

Design mandates 3 chained PRs; PR3 MUST NOT merge before PR2 is merged. Rollback is per slice in reverse dependency order (PR3 -> PR2 -> PR1). PR1 is independently revertible at any time.

### Suggested Work Units

| Unit | Goal | PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|----|----------------------|-----------------|-------------------|
| 1 | Sidebar shell nav, SPA-only, zero API contact | PR 1 | `npm test` && `npm run lint` && `npm run build` in `SmartNet/SmartNetWeb` | N/A — adds no route, no API call, no subprocess; `app.routes.ts` untouched | `sidebar.service.*`, `shared/shell-layout/sidebar/*`, `shell-layout` diff, BACKLOG note, `openspec/specs/spa-shell-nav/spec.md` |
| 2 | Widen `GET /api/bandeja` projection + global `resumen` aggregate | PR 2 | `dotnet test api/SmartNet.Api.Tests` && `dotnet test inbox/SmartNet.Inbox.Infrastructure.Tests` in `SmartNet/SmartNetApi` | `dotnet test` (needs local SQL Server) — BLOCKED, not PASS, if unavailable | `IBandejaRepository.cs` / `SqlBandejaRepository.cs` diff + their test files + `openspec/specs/bandeja/spec.md` delta |
| 3 | SPA consumes widened contract: 5 columns + 4 resumen cards | PR 3 (base = PR 2 branch) | `npm test` && `npm run lint` in `SmartNet/SmartNetWeb` | `integration-spa-api` harness — BLOCKED if local SQL Server absent, never fabricate PASS | `bandeja-item.model.ts`, `inbox.service.*`, `inbox-list.*`, `inbox-resumen/*`, `inbox-page` diff, `openspec/specs/spa-visual-bandeja/spec.md` delta |

## Phase 1: PR1 — Shell navigation (SPA only, zero API)

- [ ] 1.1 RED: `src/app/shared/sidebar.service.spec.ts` — default `expandido` on empty storage; `alternar()` writes `localStorage` key `fact.sidebar`; tampered / empty / wrong-case / null value falls back to `expandido` without throwing (threat-matrix: client input trust). Satisfies spa-shell-nav "Collapsed state persists per viewer in localStorage".
- [ ] 1.2 GREEN: create `src/app/shared/sidebar.service.ts` — `EstadoSidebar` type, pure `leerEstadoAlmacenado(storage?)`, `@Injectable({providedIn:'root'})` service mirroring `tema.service.ts` (private writable signal + `asReadonly()`, `colapsado` computed, `alternar()` writes storage + sets signal). ADR 0009: no state library.
- [ ] 1.3 RED: `src/app/shared/shell-layout/sidebar/sidebar.spec.ts` — renders exactly `Bandeja` then one hairline divider then `Configuración`; `routerLink` targets `/bandeja` and `/configuracion`; `RouterLinkActive` marks the active entry only; collapsed state keeps the `aria-label` accessible name; glyph markup has no `<svg>`, `<img>`, or icon font. Satisfies spa-shell-nav "lists only destinations with existing routes", "grouped with a single hairline divider", "glyphs hand-built from div elements".
- [ ] 1.4 GREEN: create `src/app/shared/shell-layout/sidebar/sidebar.{ts,html,css}` — presentational, `OnPush`, `input.required<boolean>('colapsado')`, `output<void>('alternar')`, `RouterLink` + `RouterLinkActive`; 2 nav items (primary group = Bandeja, utility group = Configuración) split by one hairline divider; collapse toggle; 3 `<div>` glyphs (bandeja, configuracion, chevron), max 1 element + `::before`/`::after` each. CSS layout-only, every color via `var(--...)`, no literals; reuse `--texto-principal` / `--texto-secundario`, `--accento*`, `--borde-hairline`, `--fondo-sidebar`.
- [ ] 1.5 RED: update `src/app/shared/shell-layout/shell-layout.spec.ts` — `<app-sidebar>` rendered inside the shell; toggle wired to `SidebarService.alternar`; a fresh instance re-reads persisted state; default width 216px (expanded) with no stored preference. Satisfies spa-shell-nav "expanded by default and collapsible".
- [ ] 1.6 GREEN: modify `src/app/shared/shell-layout/shell-layout.{ts,html,css}` — inject `SidebarService` (container-only), render `<app-sidebar [colapsado]="..." (alternar)="...">`, grid shell 216px / 60px. Stylesheet stays layout-only, within `anyComponentStyle` 4kB.
- [ ] 1.7 Confirm `app.routes.spec.ts`, `paleta.spec.ts`, `contraste.spec.ts` pass UNCHANGED (proof no route and no token moved). Do not edit them.
- [ ] 1.8 Create `openspec/specs/spa-shell-nav/spec.md` from spec artifact FILE 1 verbatim (new capability). Confirm `openspec/specs/spa-design-tokens/spec.md` and `src/styles.css` unchanged (spec FILE 4 is a documented no-op).
- [ ] 1.9 Append the one indented note under BACKLOG.md item #21, exact text from design D9; the #21 checkbox line stays byte-identical.
- [ ] 1.10 Full suite (no regression): `npm test` && `npm run lint` && `npm run build` in `SmartNet/SmartNetWeb`; confirm `anyComponentStyle` < 4kB for BOTH `shell-layout.css` and `sidebar.css`.

## Phase 2: PR2 — API + SQL contract (.NET)

- [ ] 2.1 RED: `inbox/SmartNet.Inbox.Infrastructure.Tests/SqlBandejaRepositoryTests.cs` — promoted row returns all 6 enriched fields with `ProveedorNombre` from a seeded `dbo.Proveedor` (test-only INSERT, precedent `DboCatalogSeedHelper`); row whose `codpro` is absent from catalog -> `ProveedorNombre` null; `INCIDENCIA` row -> all 6 fields null and row still present. Satisfies bandeja "rows carry comprobante identification fields".
- [ ] 2.2 RED (same file): buckets partition — `pendientes+validadas+conError+alertas+descartadas === total`; a `PROMOVIDO` row with no errors and no flags IS counted in `validadas` (anti-regression for the "structurally 0" risk); precedence — `DESCARTADO` + error history counts in `descartadas` not `conError`; error + `esProveedorGenerico` counts in `conError` not `alertas`. Satisfies bandeja "estado aggregate over a wider predicate".
- [ ] 2.3 RED (same file): `resumen` is byte-identical across `estado=PENDIENTE`, `desde`/`hasta`, `proveedor`, and `pagina=2` — filters do not touch the aggregate.
- [ ] 2.4 RED (same file): the whole widened batch runs via `ExecuteAsUserAsync` impersonating `usr_api` (ADR 0003 gate for `dbo.Proveedor` + `fact.ProcesamientoError` reads).
- [ ] 2.5 RED: `api/SmartNet.Api.Tests/BandejaEndpointsTests.cs` — `GET /api/bandeja` JSON carries `resumen` with all six camelCase keys and the 6 enriched keys per item; `resumen` unaffected by filter/pagination params.
- [ ] 2.6 GREEN: modify `inbox/SmartNet.Inbox.Core/IBandejaRepository.cs` — add 6 nullable fields on `BandejaItem` after `RucProveedor` (`ProveedorNombre`, `TipoComprobante`, `Numero`, `TotalOrig`, `Moneda`, `FechaEmision` per design "Interfaces / Contracts"); add `ResumenBandeja(int Pendientes, Validadas, ConError, Alertas, Descartadas, Total)` record; append required `ResumenBandeja Resumen` to `PaginaBandeja<T>`.
- [ ] 2.7 GREEN: modify `inbox/SmartNet.Inbox.Infrastructure/SqlBandejaRepository.cs` — `LEFT JOIN dbo.Proveedor pr ON pr.codpro = f.ProveedorCodigo` + 6 columns in resultset #2 ONLY; new aggregate resultset #3 (NO WHERE clause) placed between the errores resultset and the conditional fallback `COUNT(*)`, `CASE` bucket order = chip first-match precedence per design D2 / D2b (`EXISTS` on `fact.ProcesamientoError` with NO `Clasificacion` filter); extend reader for resultset #3 and the new columns. Do NOT touch `FiltroWhere`, the `@pagina` INSERT, or the fallback `COUNT(*)`.
- [ ] 2.8 Confirm `api/SmartNet.Api/BandejaEndpoints.cs` unchanged; NO new SQL script, NO new grant, NO ADR 0016 delta (D3: grants already exist).
- [ ] 2.9 Merge `openspec/specs/bandeja/spec.md` delta from spec FILE 2 — the enriched-fields requirement, the wider-aggregate requirement, and the D2b `OBSOLETO` asymmetry note.
- [ ] 2.10 Full suite (no regression): `dotnet test api/SmartNet.Api.Tests` && `dotnet test inbox/SmartNet.Inbox.Infrastructure.Tests` in `SmartNet/SmartNetApi` (needs local SQL Server; report BLOCKED, never PASS, if unavailable).

## Phase 3: PR3 — SPA consumption (base = PR2 branch; MUST NOT merge before PR2)

- [ ] 3.1 RED: `src/app/inbox/data-access/inbox.service.spec.ts` — `resumen()` is null before load, populated after; enriched item fields survive the round trip; the `paginaVacia` test literal gains a `resumen`.
- [ ] 3.2 GREEN: modify `src/app/inbox/models/bandeja-item.model.ts` — 6 fields as `readonly ...: T | null` on `BandejaItemBase` (`fechaEmision: string | null` yyyy-MM-dd, `totalOrig: number | null`); `ResumenBandeja` interface (pendientes/validadas/conError/alertas/descartadas/total); `readonly resumen: ResumenBandeja` on `PaginaBandeja<T>`.
- [ ] 3.3 GREEN: modify `src/app/inbox/data-access/inbox.service.ts` — `private resumenSignal = signal<ResumenBandeja | null>(null)` + `readonly resumen = resumenSignal.asReadonly()`; populate from the response envelope.
- [ ] 3.4 RED: `src/app/inbox/ui/inbox-list/inbox-list.spec.ts` — 5 new columns present in order (`F. emisión`, `Proveedor`, `Tipo`, `Número`, `Monto`); `INCIDENCIA` row renders "—" in all factura-only cells; `nombreComprobante('01')==='Factura'`, `'03'->'Boleta'`, `'07'->'Nota de crédito'`, unknown non-null code renders raw, null -> "—"; empty-state `colspan="10"`; date and monto cells use a component-scoped tabular-figures class, NOT the global `.tabular-nums` primitive; `chipEstadoDe` precedence unchanged; `chipsDe()` per-indicator column unchanged. Satisfies spa-visual-bandeja MODIFIED "inbox-list table with derived Estado chip column".
- [ ] 3.5 GREEN: modify `src/app/inbox/ui/inbox-list/inbox-list.{ts,html,css}` — module-level pure `nombreComprobante(codigo: string | null): string` beside `chipsDe()` / `chipEstadoDe()`; add 5 columns in final order per design D7 (`Recibido | F. emisión | Proveedor | Tipo | Número | Monto | Estado | Detalle | Indicadores | Acciones`); `colspan` 5 -> 10; monto right-aligned `{{ monto }} {{ moneda }}` with component-scoped `font-variant-numeric: tabular-nums`; wrapper `overflow-x: auto`; `?? '—'` for factura-only cells.
- [ ] 3.6 RED: `src/app/inbox/ui/inbox-resumen/inbox-resumen.spec.ts` — exactly 4 cards (Pendientes / Validadas / Con error / Alertas); values come from the `input`, not derived from `items()`; component has no `output` and activating a card mutates no filter signal / re-issues no query. Satisfies spa-visual-bandeja ADDED "inbox-page global summary cards".
- [ ] 3.7 GREEN: create `src/app/inbox/ui/inbox-resumen/inbox-resumen.{ts,html,css}` — presentational `OnPush`, `input.required<ResumenBandeja>()`, NO output; 4 display-only cards mapped Pendientes<-`pendientes`, Validadas<-`validadas`, Con error<-`conError`, Alertas<-`alertas` (`descartadas`/`total` not rendered). CSS token-driven, layout-only, within `anyComponentStyle` budget.
- [ ] 3.8 RED: `src/app/inbox/feature/inbox-page/inbox-page.spec.ts` — `@if (resumen(); as r)` renders `<app-inbox-resumen [resumen]="r">`; the four card numbers do not change when a filter signal changes or the page moves.
- [ ] 3.9 GREEN: modify `src/app/inbox/feature/inbox-page/inbox-page.{ts,html}` — wire `<app-inbox-resumen [resumen]="inbox.resumen()">` under `@if`.
- [ ] 3.10 Merge `openspec/specs/spa-visual-bandeja/spec.md` delta from spec FILE 3 INCLUDING the "Notes for archive" prose edits (project rule 1 — deliberate, documented): (1) Purpose — mark the #13 bandeja query + `inbox.service.ts` DELIBERATELY UNFROZEN by #21 for the enriched comprobante fields and the `resumen` aggregate, keep filter semantics / pagination / `chipsDe()` per-indicator column / reprocesar 5-minute window frozen; (2) Out of Scope — REMOVE the two #21 bullets for the delivered fields (proveedor name, monto, moneda, número, tipo, fecha de emisión), KEEP glosa / tipo de cambio / base imponible / IGV out of scope, NARROW the last bullet so it no longer forbids changes to "the #13 bandeja query" or "`inbox.service.ts`" while still forbidding changes to filter semantics, pagination, `chipsDe()`, and the reprocesar window.
- [ ] 3.11 Full suite (no regression): `npm test` && `npm run lint` in `SmartNet/SmartNetWeb`.
- [ ] 3.12 Run the `integration-spa-api` harness for the widened `GET /api/bandeja` envelope — compare the SPA client's expected payload shape against the real API response (`resumen` camelCase keys, 6 enriched per-item keys, field types). Report **BLOCKED** if local SQL Server is unavailable — never fabricate a PASS.
