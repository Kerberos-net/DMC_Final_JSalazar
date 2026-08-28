# Proposal: item-18-ajuste-visual-spa (BACKLOG #18 — Ajuste visual del diseño SPA)

BACKLOG #18. Depende de: #12 (SPA detalle/validación, archivado). Contexto extra: design handoff (`handoff/DESIGN_BRIEF.md`, `handoff/Gestor de Facturas.dc.html`); REGLAS.md + plan de cuentas required for the accounting fields pulled in by decision 3.

## Intent

The SPA (login + detalle-validación) was styled "con criterio" without a formal mockup — a risk the first visual pass (`2026-08-24-diseno-visual-spa-item-12`) flagged. The ratified handoff is now that mockup and it diverges from first-pass choices. #18 conforms the SPA to the handoff and closes the "no formal mockup" gap. It also fills functional holes in `factura-form` (most header fields are not rendered/editable today).

## Scope

### In Scope
- **Tokens**: rewrite `styles.css` token values — accent family (`#0071e3`/`#0a84ff`) + separate accessible `--accento-texto`, warm dark palette, 8/12/16 radii, elevated shadows, 4-level surface hierarchy, translucent hairline borders, Segoe-first stack with rounded integer type scale. `contraste.spec.ts` updated in lockstep, AA green both themes.
- **Ratified exception**: blue accent doubles as primary-action color, estado "Pendiente" chip, and P00000 informational banner. Deliberately departs from brief "un estado = un color" and first-pass D3 (pendiente = neutral gray). Rationale: fidelity to the mockup.
- **Template/CSS restructure** (handoff layout, these screens only): `login-page` (logo/subtitle/footer/placeholder inputs/full-width button); `detalle-page` (page header + back button + top-right Guardar/Validar; indicator banners moved above the split; visor 42% static, form flex:1); `app.html`/`app.css` header; `detalle/ui/{factura-form, asiento-lineas, visor-documento, conflicto-banner, historial-correccion}` token/radii/shadow follow-through.
- **factura-form functional additions**: render + two-way bind editable fields — tipo comprobante, número, monto, base imponible, IGV, moneda, fecha emisión, TC compra, glosa, mes/día contable, proveedor picker. Per-field OCR-missing highlight (`.campo--resaltado` bound to real data). Dedicated "tipo de cambio faltante" indicator (red, "Se muestra 0.00").
- **asiento-lineas**: tabular alignment (`.tabla`/`.tabular-nums`), Cuenta/Debe/Haber grid, total row, "+ Agregar línea" accent link, cuadre pill (cuadre already computed in `detalle-page.ts`).
- **P00000 and duplicate keep HARD-BLOCKING "Validar"** (first-pass D2 / brief §3). Handoff ack-checkbox pattern NOT adopted.
- Money display: 2 decimals everywhere (ignore handoff prototype 3-decimal `fmtMonto`).

### Out of Scope / Non-Goals
- `inbox/*` (bandeja #13), `configuracion/*` (#17), `registro-de-compra` (does not exist), sidebar nav redesign. Token ripple auto-improves these currently-unstyled screens — acceptable, not chased.
- No changes to #12/#13/#17 functional behaviour EXCEPT the `factura-form` fields above.
- No new accounting rules invented; no external accounting integration; no data migration.

## Backend work pulled in by decision 3

Investigated `FacturaRespuesta` / `CorreccionFacturaRequest` (`SmartNet/SmartNetApi/api/SmartNet.Api/FacturaEndpoints.cs`), the archived #12 API change, and the SPA `detalle/{models,data-access}`. Findings, by field:

| Field | Today | Work required |
|---|---|---|
| monto (`totalOrig`), `moneda`, `fechaEmision`, `proveedorCodigo` | In GET projection **and** PATCH contract | SPA binding only — no backend |
| `tipoComprobante`, `numero` | In GET projection, **not** PATCH-editable | Backend delta: add to `CorreccionFacturaRequest` + `CorreccionFactura` core + `ServicioDeFacturas.PatchAsync` + domain validation |
| base imponible, IGV | Live on `fact.AsientoContable` (`BasePEN`/`IgvPEN`), **not** on `fact.Factura`; absent from projection | New projection + edit path; crosses into accounting-core territory (REGLAS.md normative) |
| TC compra | `fact.TipoCambio.Compra` (reference data); ADR 0018 pt.1 converts pasivo at **venta**, never compra | Read-only projection at most; editability is a domain question |
| glosa | **No column** on any table | Versioned SQL schema change (`SmartNet/db/schema/`, ADR 0016) + projection + edit path |
| mes / día contable | Derivable from `fact.AsientoContable.FechaContable` | Projection of derived values; edit semantics unclear |

Only the first row is pure visual/SPA work. Everything below it is functional and, for base/IGV/TC/glosa/mes/día, touches the accounting core and/or the schema — the exact reason #18 was split from #12.

## Approach

Exploration Approach 2 (tokens + template restructure of the two screens), extended with the decision-3 functional fields. Work order:
1. Token refresh + `contraste.spec.ts` (Strict TDD: spec pairs first).
2. Theme + shell header + `login-page` restructure.
3. `detalle-page` restructure + `asiento-lineas` tabular + cuadre pill.
4. `factura-form` fields: first the four zero-backend fields + `tipoComprobante`/`numero` (small API delta); base/IGV/TC/glosa/mes/día gated behind the open-question resolution and, where needed, an ADR touchpoint + versioned SQL.

## Capabilities

### New Capabilities
- None.

### Modified Capabilities
- `spa-design-tokens`: accent family, `--accento-texto`, warm dark palette, radii/shadow/surface/type scale, AA re-assertion.
- `spa-theme-toggle`: confirm `<select>` control retained (no sidebar in scope).
- `spa-visual-login`: logo/subtitle/footer/placeholder inputs/full-width button/copy.
- `spa-visual-detalle-validacion`: page header + top actions, banners above split, 42/58 static split, tabular asiento + cuadre pill, per-field highlight, TC-faltante indicator, ratified color exception.
- `pantalla-detalle-validacion`: `factura-form` now renders/binds the full header field set; P00000 + duplicate remain hard blocks on Validar.
- `api-facturas` (delta): `CorreccionFacturaRequest` gains `tipoComprobante`/`numero` (and, pending open-question, base/IGV/glosa/mes/día); `FacturaRespuesta` projection extended accordingly.

## Open Questions (defaults proposed — confirm before spec)

1. **"Fecha de corte contable" placement** — handoff header has no slot. Default: keep it adjacent to the asiento block as a period control.
2. **Theme control** — `<select>` vs handoff sun/moon toggle. Default: keep `<select>` (shell nav redesign out of scope).
3. **Accent-text accessibility** — `#0071e3` on `#f5f5f7` ≈ 4.0:1, fails AA as normal text. Default: separate `--accento-texto` (darker, e.g. `#0a63c9` light / keep `#0a84ff` dark) for links/text; `#0071e3` stays for button fills (white-on-blue passes). `contraste.spec.ts` stays green.
4. **Money decimals** — Default: 2 decimals everywhere; ignore prototype `fmtMonto` 3-decimal.
5. **Decision-3 accounting fields (base/IGV/TC compra/glosa/mes/día contable)** — Default: split from the visual PRs; base/IGV/TC displayed read-only first; glosa deferred behind a versioned SQL schema change + ADR touchpoint; editability of accounting-core fields raised with REGLAS.md before implementation.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `SmartNet/SmartNetWeb/src/styles.css` | Modified | Token refresh |
| `SmartNet/SmartNetWeb/src/app/shared/contraste.spec.ts` | Modified | Re-assert every token pair, both themes |
| `SmartNet/SmartNetWeb/src/app/{app.html,app.css}` | Modified | Header, theme control, marca |
| `SmartNet/SmartNetWeb/src/app/login/feature/login-page/*` | Modified | Template + CSS restructure |
| `SmartNet/SmartNetWeb/src/app/detalle/feature/detalle-page/*` | Modified | Page header, action placement, split ratio, corte-contable control |
| `SmartNet/SmartNetWeb/src/app/detalle/ui/factura-form/*` | Modified | 2-col grid, new bound fields, per-field highlight, TC indicator |
| `SmartNet/SmartNetWeb/src/app/detalle/ui/asiento-lineas/*` | Modified | Tabular table, cuadre pill |
| `SmartNet/SmartNetWeb/src/app/detalle/ui/{visor-documento,conflicto-banner,historial-correccion}/*` | Modified | Token/radii/shadow follow-through |
| `SmartNet/SmartNetWeb/src/app/detalle/models/factura.model.ts` | Modified | Extend `FacturaRespuesta` / `CorreccionFacturaRequest` |
| `SmartNet/SmartNetApi/api/SmartNet.Api/FacturaEndpoints.cs` | Modified | `CorreccionFacturaRequest`/`FacturaRespuesta` records |
| `SmartNet/SmartNetApi/facturacion/SmartNet.Facturacion.Core/CorreccionFactura.cs` + `ServicioDeFacturas.cs` | Modified | New correction fields + validation (pending OQ5) |
| `SmartNet/SmartNetApi/db/schema/*` | New (pending OQ5) | `glosa` column, versioned SQL only (ADR 0016) |
| `*.spec.ts` (login-page, detalle-page, factura-form, asiento-lineas) | Modified | New structure/binding assertions |
| `inbox/*`, `configuracion/*` | Ripple only | Token inheritance, not chased |

## Constraints

- ADR 0009 — signals, no state library; new reactive state = signal.
- ADR 0016 — schema changes via versioned SQL only (no EF/Alembic).
- ADR 0018 pt.1 — pasivo en ME converts at TC venta, never compra.
- ADR 0019 — accounting core free of DB/HTTP/clock.
- CONVENTIONS.md — Spanish accounting domain / English scaffolding; no accents/ñ in identifiers; money `DECIMAL(18,2)`, never float, never 3-decimal display; exchange rate `DECIMAL(12,6)`.
- `angular.json` `anyComponentStyle` budget: 4kB warn / 8kB error — component CSS stays layout-only, no color/font literals.
- `contraste.spec.ts` AA in both themes.
- Strict TDD active.
- REGLAS.md + plan de cuentas mandatory context for any accounting-field work (⚠).

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Ratified color exception confuses "estado = color" semantics | Med | Document as explicit ratified exception in spec + design with rationale |
| Decision-3 accounting fields balloon scope / invent accounting rules | High | OQ5 default: read-only first, glosa deferred, editability reviewed against REGLAS.md before code |
| `glosa` schema change on shared `fact` schema | Med | Versioned SQL only; isolate in its own PR slice |
| `#0071e3` text fails WCAG AA | High (known) | Separate `--accento-texto` token; `contraste.spec.ts` gate |
| Change exceeds 400-line review budget | High | Chained PRs (see Delivery) |
| Component CSS exceeds style budget | Med | Keep literals in `styles.css` tokens; layout-only component CSS |

## Delivery / Size Risk

Large change; review budget 400 lines, strategy `ask-on-risk`. Chained PRs likely. Proposed split (tasks phase formalizes):
- **PR1** — tokens (`styles.css`) + `contraste.spec.ts` + theme + `login-page` + shell header.
- **PR2** — `detalle-page` restructure (header, top actions, split, banner relocation, corte-contable control) + `asiento-lineas` tabular + cuadre pill.
- **PR3** — `factura-form` new fields (the zero-backend four + `tipoComprobante`/`numero` API delta) + per-field highlight + TC-faltante indicator.
- **PR4 (conditional, OQ5)** — base/IGV/TC read-only projection; glosa schema + edit path if approved.

## Impacted Specs (spec phase)

Revises archived `2026-08-24-diseno-visual-spa-item-12`. Delta specs to touch/create in this change folder: `spa-design-tokens`, `spa-theme-toggle`, `spa-visual-login`, `spa-visual-detalle-validacion`, `pantalla-detalle-validacion`, `api-facturas`.

## Rollback Plan

Each PR is an independent revert. PR1 revert = restore prior `styles.css` + `contraste.spec.ts` (tokens are the only cross-cutting artifact; component CSS is layout-only and self-contained). API delta (PR3/PR4) reverts independently of SPA templates since new fields are additive/optional. `glosa` schema migration ships as its own forward-only versioned script with a documented down path.

## Dependencies

- Ratified design handoff (`handoff/`).
- REGLAS.md + plan de cuentas for OQ5 accounting-field decisions.
- User confirmation of the 5 open questions before spec phase.

## Success Criteria

- [ ] `login-page` and `detalle-page` match the handoff layout (header, actions, split, banners, login card).
- [ ] `styles.css` tokens reflect handoff palette/radii/shadows/type; `contraste.spec.ts` green in both themes.
- [ ] `factura-form` renders and binds the agreed header fields; OCR-missing fields visually highlighted per field; TC-faltante indicator present.
- [ ] `asiento-lineas` tabular with cuadre pill.
- [ ] P00000 and duplicate still hard-block "Validar".
- [ ] Money shown with 2 decimals everywhere.
- [ ] Excluded screens (`inbox/*`, `configuracion/*`) unbroken by token ripple.
- [ ] `ng test` and `dotnet test` green; component style budgets not exceeded.
