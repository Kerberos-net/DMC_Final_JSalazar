# Exploration: item-20-ajuste-visual-bandeja (BACKLOG #20 — Ajuste visual de bandeja y panel de errores)

## Summary
Same visual-conformance pass #18 did for `login-page` + `detalle-page`, now for the inbox/bandeja screens #18 excluded. The 5 inbox components today have ZERO component CSS — bare HTML inheriting only the global `@layer` primitives in `styles.css`. #20 is CSS + template-structure only; #13 functional behaviour (bandeja query, filters, pagination, indicators→chips, `<details>` error panel, reprocesar/5-min window) is FROZEN. Central finding: the handoff bandeja artboard shows a much richer data model than the app actually has; matching it literally is backend work of the kind #18 pushed to #19. A #18-shaped Approach 2 (tokens/primitives + template restructure, no new data) is recommended.

## Component inventory
- `InboxPage` `inbox/feature/inbox-page/` — container (smart): filter signals, effect→`InboxService.cargar`, reprocesar flow. No CSS. In scope: page header + layout shell.
- `InboxFilter` `inbox/ui/inbox-filter/` — dumb: estado/desde/hasta/proveedor/orden, `<label>`-wrapped `<select>`/`<input>`, stacked (global label display:block). No CSS. In scope: horizontal filter bar.
- `InboxList` `inbox/ui/inbox-list/` — dumb: `<table class="inbox-list">` cols Fecha|Estado|Detalle|Indicadores|Acciones; `chipsDe()` per-row indicator chips (base `.chip`, no modifier); reprocesar button + `<details>`. No CSS. In scope: `.tabla`/`.tabular-nums` + derived estado chip.
- `PanelErrores` `inbox/ui/panel-errores/` — dumb: `<ul class="panel-errores">` of {clasificacion,mensaje,ocurridoEn}, INLINE inside inbox-list `<details>`, renders nothing when empty. No CSS. In scope: restrained list/card.
- `ConfirmarReproceso` `inbox/ui/confirmar-reproceso/` — dumb: native `<dialog class="confirmar-reproceso">` toggled via `.open` attr (jsdom-safe, NOT showModal()), 2 buttons. No CSS. In scope: modal card/backdrop.
- inbox-page IS the container; inbox-filter/inbox-list are direct children; panel-errores is a grandchild (rendered by inbox-list, not a route); confirmar-reproceso is an inline `<dialog>` sibling in inbox-page.
- `configuracion/*` NOT in scope — brief §6 is item #17; #18 also excluded it.
- The standalone "Errores y notificaciones" sidebar route in the handoff (`.dc.html` L785+) is NOT the `panel-errores` component — that route is item #17's `openspec/specs/panel-errores/spec.md`. #20's panel de errores per BACKLOG row 20 is the inline `<details>` component. Flag for proposal.

## Current vs handoff — gaps
- **Table/tabular:** today no `.tabla`/`.tabular-nums`, estado is raw text `{{estadoConsumo}}`, indicators unstyled. Handoff (`.dc.html` L375-408): dense CSS grid, `font-variant-numeric:tabular-nums` on every numeric cell, right-aligned money, 12.5px rows, uppercase 11px headers. App HAS `.tabla`/`.tabular-nums` primitives (styles.css L374-399) but inbox-list ignores them.
- **Semantic status chip:** handoff `estadoInfo()` (`.dc.html` L1291-1305) 4 states light|dark: pendiente `#0071e3`/`rgba(0,113,227,0.1)` | `#409cff`; validada `#1f8a3d`/`rgba(52,199,89,0.13)` | `#30d158`; error `#d70015`/`rgba(255,59,48,0.1)` | `#ff453a`; alerta `#c93400`/`rgba(255,149,0,0.13)` | `#ff9f0a`. App enum `EstadoConsumo='PENDIENTE'|'PROMOVIDO'|'DESCARTADO'` — no Error/Alerta. Chips are `.chip--{pendiente,validada,descartada}` (styles.css L355-371). Mapping (presentation-only, data already on BandejaItem): `errores.length>0`→Error; `esProveedorGenerico||posibleDuplicado`→Alerta; PROMOVIDO→Validada; PENDIENTE→Pendiente; DESCARTADO→keep `.chip--descartada`. `.chip--error`/`.chip--alerta` are NEW modifiers. Derivation is adjacent to #13 frozen "indicators→chips" — proposal must state per-indicator `chipsDe()` unchanged and estado chip is additive new column.
- **Filters bar:** today stacked labels, no layout. Handoff: horizontal search + date range + clickable estado quick-filter chips w/ counts.
- **Quick counter/summary:** DOES NOT EXIST. inbox-page.html = h1 + filter + list + dialog. InboxService exposes items/loading/error/totalPaginas, NO per-estado counts. Handoff: 4 summary cards (Pendientes/Validadas/Con error/Alertas) w/ counts. Client can only derive from current 20-row page (misleading). Truthful counter = backend aggregate = #13-scope. Defer, raise as fork.
- **Panel de errores (brief §5 "urgencia sin sobrecargar de rojo"):** today bare `<ul>`. #18 already ratified the pattern — `.alerta--informativa` (1px border, no fill) vs `.alerta--bloqueante` (fill+4px rule), and `detalle/ui/indicadores-factura` precedent. Reuse: error rows get `--error-ink` text + hairline, not full red fill.

## Data exists?
`BandejaItem` (`inbox/models/bandeja-item.model.ts`) has: inboxEventId, procesamientoId, estadoConsumo, creadoEn, proveedorCodigo, rucProveedor, motivoDescarte, errores[], reprocesarDisponibleEn, and (FACTURA only) facturaId, indicadores{esProveedorGenerico,posibleDuplicado,tieneCamposNoExtraidos,fechaEnDomingo,afectacionMixta}.
MISSING for handoff columns (would pull backend work like #18): proveedor display name, tipoComprobante, numero, monto, moneda, fechaEmision, glosa, tipoCambio, baseImponible, igv, origenLibro, adjuntos flag, per-estado aggregate counts. Adding any = widen `GET /api/bandeja` + `BandejaItem` + `SqlBandejaRepository` + `IBandejaRepository` (#13 frozen surface). Status chip, tabular alignment of creadoEn, restrained error panel = all doable with existing data.

## New primitives / token ripple
styles.css today: NO `--estado-error-*`/`--estado-alerta-*` family, NO `.chip--error`/`.chip--alerta`. Has: `--error-ink #b3211f`/`--error-fondo #fdecec` (used by `.banner--error` 422 + indicadores-factura --tc); `--alerta-ink #8a4300`/`--alerta-fondo #fdf0e1` (used by `.alerta--*`, `.campo--resaltado`, indicadores-factura --duplicado); `.chip--{pendiente,validada,descartada}` + matching `--estado-*` token trios.
Recommended: add `--estado-error-{texto,fondo,borde}` + `--estado-alerta-{texto,fondo,borde}` both themes, deriving texto from existing `--error-ink`/`--alerta-ink` — do NOT introduce raw handoff hexes `#d70015`/`#c93400` as new literals (#18 D1 two-tier ramp; they're also browner/redder than the AA-tuned inks). Add `.chip--error`/`.chip--alerta` in `@layer primitives`, same shape as `.chip--validada`.
Ripple `contraste.spec.ts`: `PARES_TINTA_FONDO` gains error+alerta estado pairs both themes; any new `-ink` name enters `TINTAS_TEXTO` (vs all 4 surfaces). `paleta.spec.ts` (`tokensPorTema`) parses by name — new tokens just need to exist in both theme blocks. Reusing `--error-fondo`/`--alerta-fondo` (already AA vs their inks) is the safe path.

## Semantic consistency w/ #18
- Pendiente: handoff chip blue = identical to #18 ratified accent-reuse (`--estado-pendiente-ink`=`--azul-700`/`--azul-400`). No conflict, reuse token.
- Error: handoff "Error" red = same hue as #18 `--error-*` (412/422 banners, TC-missing indicator). Reusing for estado chip is a semantic overload (integration/validation/conflict all red) but brief rule is "one state=one colour" and red="failed" is coherent. Acceptable, note it.
- Alerta: handoff "Alerta" burnt orange = same family as #18 `--alerta-*` (duplicado indicator, `.campo--resaltado`). Consistent — "alerta" already means duplicado/proveedor-generico/dato-faltante in #18. Reuse family.
- Do NOT re-tune `--alerta-ink`/`--error-ink` to exact handoff hexes — ripples every existing contraste.spec pair + every #18 detalle indicator for a sub-perceptual shift.
- Net: #20 introduces NO new hue, only two new `.chip--*` roles over #18-ratified colours.

## Handoff fidelity
Concrete for tokens, directional for structure. `.dc.html` gives usable values: estado chip fg/bg per state both themes (`estadoInfo()` L1291-1305); palette (`colors()` L1333+) = same 4-surface warm-dark set #18 adopted; radii 8/10-12/20, 12.5px rows, uppercase 11px headers; summary cards border:1px+radius:12+padding:16, 26px/800 count. BUT the artboard's table columns describe a data model the app lacks (año/mes/día, tipo, número, proveedor name, glosa, TC, base, IGV, monto, adjuntos, origen-libro) — reads as merged "dashboard + registro de compra", not the InboxEvent triage list #13 built. Column set = directional; token values = normative. `fmtMonto`/`fmtNum` use 3 decimals — known prototype quirk, CONVENTIONS.md (never 3-decimal) wins, same call #18 made.

## Open questions for proposal
1. Estado column scope: additive new "Estado" chip derived from errores[]+indicadores+estadoConsumo (presentation only), per-indicator chip list kept — confirm not touching #13 frozen logic.
2. Counter/summary: (a) omit; (b) client-derive from current page, label "en esta página"; (c) backend aggregate (new `GET /api/bandeja/resumen` or envelope fields) = #13-scope, separate slice. USER DECISION.
3. Handoff table columns (proveedor name, tipo, número, monto, moneda, fecha, glosa, TC, base, IGV): confirmed OUT (needs BandejaItem + `GET /api/bandeja` widening = #13/#19), or is a minimal subset (proveedor name+monto+moneda) wanted enough to justify a backend delta?
4. `panel-errores` scope: bandeja inline `<details>` only, or also the standalone #17 "Errores y notificaciones" route?
5. `configuracion/*`: confirmed out (#17), or token follow-through pass since it also has no component CSS?
6. New tokens vs reuse: add `--estado-error-*`/`--estado-alerta-*` trios (referencing existing inks), or map `.chip--error`/`.chip--alerta` straight onto `--error-*`/`--alerta-*`?
7. DESCARTADO chip: handoff has no "Descartada" state — keep `.chip--descartada`, or fold into Alerta/neutral?
8. `confirmar-reproceso` `<dialog>`: style as centered modal card + manual backdrop, accepting it stays non-modal (`.open` attr, #13 D6 / jsdom)?

## Approaches
1. **Tokens/primitives + component CSS only — Low.** Add `.chip--error`/`.chip--alerta` + estado tint tokens both themes + contraste.spec pairs; layout-only `.css` (outside `@layer`) on all 5 components (`.tabla`/`.tabular-nums`, filter flexbox, panel card, dialog card); derived estado chip in inbox-list; minimal template change. Does NOT deliver rich columns/summary/page header. Smallest diff.
2. **Tokens/primitives + template restructure, NO new data — Medium.** #1 plus: inbox-page "Bandeja principal" header+subtitle+shell; inbox-filter → horizontal bar (search + date range + estado quick-filter chips, counts omitted/page-scoped); inbox-list re-templated to dense token grid w/ estado chip column; panel-errores styled per #18 indicadores-factura precedent; confirmar-reproceso styled modal; each `*.spec.ts` updated. Matches handoff LOOK using only BandejaItem data. Mirrors exactly what #18 did. RECOMMENDED.
3. **Full handoff adoption incl rich columns + summary — High.** #2 plus widen `GET /api/bandeja` + `BandejaItem` + `SqlBandejaRepository` for the missing columns + per-estado aggregate. This is #13 functional work, not visual; violates why #20 was split. NOT recommended; spin a separate backend item like #19 if genuinely wanted.

## Recommendation: Approach 2, tightly scoped — the #18 playbook applied to the inbox.
Deliver: (1) styles.css `.chip--error`/`.chip--alerta` + `--estado-error-*`/`--estado-alerta-*` tint trios both themes, texto derived from existing `--error-ink`/`--alerta-ink`, NO new hue literal (#18 D1); update contraste.spec.ts pairs + paleta.spec.ts parity, RED-first, both themes. (2) Layout-only component CSS (outside `@layer`, token-driven, angular.json 4kB/8kB budget) for all 5 inbox components, following `detalle/ui/indicadores-factura` + `asiento-lineas` conventions. (3) Template restructure of the 5 components to handoff structure using ONLY existing BandejaItem data: page header+subtitle, horizontal filter bar, `.tabla`/`.tabular-nums` list with derived semantic estado chip column (additive), restrained error panel, styled reprocesar dialog; update each `*.spec.ts`.
Exclude (call out, don't chase): handoff artboard's proveedor-name/monto/moneda/número/tipo/fecha/glosa/TC/base/IGV columns; the 4 summary counter cards (needs backend aggregate); the standalone #17 "Errores y notificaciones" route; `configuracion/*`; any change to #13 bandeja query, filter semantics, pagination, `chipsDe()`, or the reprocesar 5-min window.

## Risks
- Scope creep into #13 backend — handoff artboard implies columns the model lacks; proposal must draw the "no new data" line hard.
- `contraste.spec.ts` lockstep — any new `-ink` token must enter the parsed pair tables in both themes or the WCAG guard silently under-checks (the exact failure #18 D2 fixed).
- Derived estado chip vs "#13 frozen" — needs explicit proposal statement that an additive presentation-only estado column is not a functional change.
- Counter/summary expectation gap — user ran the app and said "doesn't match design"; the missing summary cards may be a big part of that impression; deferring must be a conscious stated decision.
- `<dialog>` styling under jsdom — confirmar-reproceso uses `.open` attr not `showModal()`, no `::backdrop`; a styled overlay must be a manual element, behaviorally identical (#13 D6).

## Affected areas
- `SmartNet/SmartNetWeb/src/styles.css` — `.chip--error`/`.chip--alerta`, estado tint tokens both themes
- `SmartNet/SmartNetWeb/src/app/shared/contraste.spec.ts` — new token-pair assertions
- `SmartNet/SmartNetWeb/src/app/shared/paleta.spec.ts` — theme parity for new tokens (if names added)
- `SmartNet/SmartNetWeb/src/app/inbox/feature/inbox-page/inbox-page.{html,ts,spec.ts}` — header + layout shell
- `SmartNet/SmartNetWeb/src/app/inbox/ui/inbox-filter/inbox-filter.{html,spec.ts}` + new `.css` — horizontal bar
- `SmartNet/SmartNetWeb/src/app/inbox/ui/inbox-list/inbox-list.{html,ts,spec.ts}` + new `.css` — `.tabla`, tabular-nums, derived estado chip
- `SmartNet/SmartNetWeb/src/app/inbox/ui/panel-errores/panel-errores.{html,spec.ts}` + new `.css` — restrained error list
- `SmartNet/SmartNetWeb/src/app/inbox/ui/confirmar-reproceso/confirmar-reproceso.{html,spec.ts}` + new `.css` — modal card
- Out of scope (ripple only): `inbox/data-access/inbox.service.ts`, `configuracion/*`, `GET /api/bandeja`

## Prior SDD artifacts
- `openspec/changes/archive/2026-08-27-item-18-ajuste-visual-spa/` — token layer, `paleta.ts`/`contraste.spec.ts` WCAG guard, ratified accent-reuse (D1), the `indicadores-factura`/`asiento-lineas` component-CSS precedent.
- `openspec/changes/archive/2026-08-24-item-13-bandeja-incidencias/` — FROZEN functional spec for bandeja, filters, pagination, panel de errores, reprocesar.
- `openspec/specs/spa-design-tokens/spec.md`, `openspec/specs/spa-visual-detalle-validacion/spec.md` — merged #18 specs #20 extends.
- `openspec/specs/inbox-screen/spec.md`, `openspec/specs/bandeja/spec.md` — #13 merged specs (functional, unchanged).

## Ready for proposal: YES
Q2 (counter), Q3 (columns), Q4 (panel scope) are genuine forks; the rest are confirmations.
