# Proposal: Ajuste visual de bandeja y panel de errores (BACKLOG #20)

## Intent

The inbox/bandeja screens were excluded from #18's visual-conformance pass. Their 5
components ship with **zero component CSS** — bare HTML on the global primitives — so
they do not match the design handoff (§2 dashboard, §5 panel de errores). A user
running the app reported "doesn't match the design". #20 applies the #18 playbook
(tokens/primitives + template restructure, no new data) to close that gap.

## Scope

### In Scope
- `styles.css`: new `.chip--error` / `.chip--alerta` primitives (`@layer primitives`,
  same shape as `.chip--validada`) + `--estado-error-{texto,fondo,borde}` /
  `--estado-alerta-{texto,fondo,borde}` token trios in **both themes**, `texto`
  derived from existing `--error-ink` / `--alerta-ink` (no new hue literal, #18 D1).
- `contraste.spec.ts` + `paleta.spec.ts` updated in lockstep (#18 D2 WCAG guard).
- Layout-only component CSS (outside cascade layers, token-driven, `angular.json`
  4kB/8kB budget) for all 5 inbox components, per the `indicadores-factura` /
  `asiento-lineas` precedent.
- Template restructure using **only data already on `BandejaItem`**: inbox-page
  header/subtitle/shell; inbox-filter horizontal bar; inbox-list `.tabla` /
  `.tabular-nums` + **additive derived Estado chip column** (`chipEstado`);
  panel-errores restrained card (`.alerta--informativa` pattern); confirmar-reproceso
  styled centered modal card + manual backdrop (non-modal `.open`, jsdom-safe #13 D6).
- Update each `*.spec.ts` (Strict TDD, RED-first).

### Out of Scope / Non-goals
- Summary counter cards → **BACKLOG #21** (needs `GET /api/bandeja` aggregate).
- Rich data columns (proveedor name, monto, moneda, numero, tipo, fechaEmision,
  glosa, TC, base, IGV) → **#21**.
- Standalone "Errores y notificaciones" route (#17 `panel-errores` spec).
- `configuracion/*` (#17) — token ripple only, not chased.
- Any change to #13: bandeja query, filter semantics, pagination, `chipsDe()`
  per-indicator logic, reprocesar 5-min window, `inbox.service.ts`.

## Capabilities

### New Capabilities
- `spa-visual-bandeja`: visual/structural requirements for the inbox screens
  (page shell, filter bar, styled table, derived Estado chip, restrained error
  panel, reprocesar modal), following `spa-visual-login` / `spa-visual-detalle-validacion`.

### Modified Capabilities
- `spa-design-tokens`: adds `.chip--error` / `.chip--alerta` primitives and the
  `--estado-error-*` / `--estado-alerta-*` tint token trios (both themes).

Read-only dependencies (MUST NOT be modified): `openspec/specs/inbox-screen/spec.md`,
`openspec/specs/bandeja/spec.md` (both #13 functional, frozen).

## Approach

Approach 2 from exploration: tokens/primitives + template restructure, no new data.
Derived semantic Estado chip is **additive, presentation-only**:
`errores.length > 0` → Error; `esProveedorGenerico || posibleDuplicado` → Alerta;
`PROMOVIDO` → Validada; `PENDIENTE` → Pendiente; `DESCARTADO` → keep `.chip--descartada`.
The per-indicator `chipsDe()` list is unchanged — this is NOT a change to #13's frozen
"indicators → chips" logic. Money display uses 2 decimals if any numeric appears
(handoff 3-decimal quirk ignored, CONVENTIONS.md wins).

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `SmartNet/SmartNetWeb/src/styles.css` | Modified | chip primitives + estado tint tokens (both themes) |
| `.../app/shared/contraste.spec.ts` | Modified | error+alerta estado pairs, both themes |
| `.../app/shared/paleta.spec.ts` | Modified | theme parity for new token names |
| `.../inbox/feature/inbox-page/*.{html,ts,spec.ts}` | Modified | header + layout shell |
| `.../inbox/ui/inbox-filter/*` + new `.css` | Modified/New | horizontal filter bar |
| `.../inbox/ui/inbox-list/*` + new `.css` | Modified/New | `.tabla`/`.tabular-nums` + Estado chip |
| `.../inbox/ui/panel-errores/*` + new `.css` | Modified/New | restrained error card |
| `.../inbox/ui/confirmar-reproceso/*` + new `.css` | Modified/New | styled modal + backdrop |

## Constraints

ADR 0009 (signals, no state lib); CONVENTIONS.md (accounting-domain identifiers in
Spanish e.g. `chipEstado`, technical scaffolding English, no accents/ñ in identifiers,
TS PascalCase types / camelCase methods+props); `angular.json` component-style budgets;
#18 token layer is the base — consume, do not fork; `contraste.spec.ts` AA in both
themes; Strict TDD active.

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Scope creep into #13 backend (handoff implies missing columns) | Med | Hard "no new data" line; columns/counters deferred to #21 |
| `contraste.spec.ts` lockstep gap → WCAG guard under-checks | Med | Any new `-ink` token enters parsed pair tables both themes (#18 D2) |
| Derived Estado chip read as functional change | Low | Proposal states it is additive, presentation-only; `chipsDe()` untouched |
| Counter/summary expectation gap (user's "doesn't match") | Med | Explicit deferral to #21, stated in-product as conscious decision |
| `<dialog>` styling under jsdom | Low | Manual backdrop element, `.open` attr, behaviorally identical (#13 D6) |

## Rollback Plan

Revert the change branch. Token additions are purely additive (new names, new
primitives) — removing them restores prior `styles.css`. Component `.css` files are
new; deleting them returns components to unstyled baseline. Template restructures
revert per-file. No data, API, or schema changes to roll back.

## Dependencies

- Merged #18 (`spa-design-tokens`, `spa-visual-*`) — token layer and WCAG guard.
- Merged #13 (`inbox-screen`, `bandeja`) — frozen functional surface.

## Delivery

Mirrors #18: each screen slice + TDD specs ~300–500 lines. Review budget 400,
`delivery_strategy: ask-on-risk`. Proposed chained-PR split (tasks phase formalizes):
- **PR1**: tokens/primitives + `contraste`/`paleta` guard + inbox-page shell + inbox-filter bar.
- **PR2**: inbox-list `.tabla`/`.tabular-nums` + derived Estado chip column.
- **PR3**: panel-errores restrained card + confirmar-reproceso styled modal.

## Open Questions (with proposed defaults)

1. **Token naming**: add `--estado-error-*` / `--estado-alerta-*` trios referencing
   existing inks (default) vs mapping `.chip--*` straight onto `--error-*` / `--alerta-*`.
   Default: add the trios — keeps the estado role nameable and guard-checkable.
2. **DESCARTADO chip**: keep `.chip--descartada` (default) — handoff has no "Descartada"
   state; do not fold into Alerta/neutral.
3. **Dialog backdrop**: manual backdrop element + centered card, accepting non-modal
   `.open` (default, jsdom-safe #13 D6).

## Success Criteria

- [ ] All 5 inbox components render per handoff structure using only `BandejaItem` data.
- [ ] `.chip--error` / `.chip--alerta` + estado tint tokens exist in both themes; no new hue literal.
- [ ] `contraste.spec.ts` passes AA for the new estado pairs in both themes; `paleta.spec.ts` parity holds.
- [ ] Component `.css` files stay within `angular.json` 4kB/8kB budgets.
- [ ] `#13` functional specs, `chipsDe()`, filters, pagination, reprocesar window unchanged.
- [ ] Counter cards and rich columns explicitly deferred to #21, not partially built.
