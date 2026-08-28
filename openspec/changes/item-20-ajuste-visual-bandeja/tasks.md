# Tasks: item-20 — Ajuste visual de bandeja y panel de errores

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~660 authored (PR1 ~245, PR2 ~230, PR3 ~215) |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR1 → PR2 → PR3 |
| Delivery strategy | ask-on-risk |
| Chain strategy | stacked-to-main |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: stacked-to-main
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | Estado tokens + `.chip--error/--alerta` primitives + WCAG guard + inbox-page shell + inbox-filter bar | PR1 | `npx ng test --no-watch -- paleta contraste inbox-page inbox-filter` | `npx ng build --configuration production` | `styles.css` token/primitive block, `paleta/contraste.spec.ts` diffs, new `inbox-page.css`/`inbox-filter.css`, their `.html` class attrs |
| 2 | `inbox-list` `.tabla` restructure + derived Estado chip column (`chipEstadoDe`) | PR2 | `npx ng test --no-watch -- inbox-list` | `npx ng build --configuration production` | `inbox-list.ts` added symbols + 2 lines, `inbox-list.html`, new `inbox-list.css` |
| 3 | `panel-errores` restrained card + `confirmar-reproceso` modal + manual backdrop | PR3 | `npx ng test --no-watch -- panel-errores confirmar-reproceso` | `npx ng build --configuration production` | new `panel-errores.css`/`confirmar-reproceso.css`, `confirmar-reproceso.ts` `abierto` signal + focus, both `.html` |

Base order (stacked-to-main): PR1→main, PR2→main after PR1, PR3→main after PR1 (PR3 independent of PR2).

## Phase 1: PR1 — Tokens, primitives, guard, page shell, filter bar

- [x] 1.1 RED: add `paleta.spec.ts` tests — 6 `--estado-*` tokens resolve `#rrggbb` both themes; each `texto`===its ink; anti-literal `not.toMatch(/#d70015|#c93400|#ff453a|#ff9f0a/i)`; `toMatch(/\.chip--error\s*\{[^}]*var\(--estado-error-texto\)/)` + `--alerta` analogue.
- [x] 1.2 RED: extend `contraste.spec.ts` — `PARES_TINTA_FONDO` += 2 estado texto/fondo rows; `TINTAS_NO_TEXTO` += `--estado-error-borde`,`--estado-alerta-borde`.
- [x] 1.3 GREEN: `styles.css` `@layer tokens` — add `--estado-error-{texto,fondo,borde}`, `--estado-alerta-{texto,fondo,borde}`, `--fondo-scrim` to BOTH light+dark blocks as `var()` aliases of `--error-*`/`--alerta-*` inks; `borde`=ink. No hue literal.
- [x] 1.4 GREEN: `styles.css` `@layer primitives` after `.chip--descartada` — `.chip--error`/`.chip--alerta` (color/background/border-color from role tokens, `.chip--validada` shape).
- [x] 1.5 REFACTOR: run `npm run lint`; confirm 1.1–1.2 green.
- [x] 1.6 RED: `inbox-page.spec.ts` — h1 text "Bandeja principal"; subtitle present; document order header→filter→list→dialog (`compareDocumentPosition`); error path renders `.banner .banner--error` keeping `role=alert` + `data-testid="inbox-error"`.
- [x] 1.7 GREEN: `inbox-page.html` — add `<header>`/h1/subtitle, error `<p>`→`.banner .banner--error`; create `inbox-page.css` (`:host` column shell, `__cabecera`,`__titulo`,`__subtitulo`).
- [x] 1.8 RED: `inbox-filter.spec.ts` — each `<label>` carries `campo inbox-filter__campo`; 5 controls still emit; 7 existing specs stay green.
- [x] 1.9 GREEN: `inbox-filter.html` class attrs only; create `inbox-filter.css` (horizontal wrap bar on card surface, per-field flex). Signals/inputs unchanged.
- [x] 1.10 REFACTOR: `npm run lint` + `npx ng build --configuration production`; flag any component `.css` nearing 4kB.

## Phase 2: PR2 — inbox-list table + derived Estado chip

- [x] 2.1 RED: `inbox-list.spec.ts` — `table.tabla`; `[data-testid="chip-estado"]` once per row; 5 precedence cases (DESCARTADO first/unconditional even with error history → "Descartada"; then errores.length>0 → Error; then `esProveedorGenerico||posibleDuplicado` → Alerta; then PROMOVIDO → Validada; then PENDIENTE → Pendiente); INCIDENCIA row `indicadores:null` does not throw.
- [x] 2.2 RED: `inbox-list.spec.ts` regression lock — `[data-testid="indicador-chip"]` count+labels UNCHANGED (`chipsDe()` frozen); new empty state `data-testid="inbox-vacio"`.
- [x] 2.3 GREEN: `inbox-list.ts` — add `ClaseChipEstado` type, `ChipEstado` interface, module-level pure `chipEstadoDe(item)` beside `chipsDe()`; add one `FilaInbox` field `chipEstado`; add one line to `filas` computed. Do not touch `chipsDe()`.
- [x] 2.4 GREEN: `inbox-list.html` — `class="tabla inbox-list"`, Estado cell → chip binding, `__fecha`/`__indicadores`/`__acciones` wrappers, empty state; create `inbox-list.css` (`:host` card + `overflow-x:auto`, `.inbox-list__fecha{font-variant-numeric:tabular-nums;text-align:left}`, indicator hairline, detalle stack, empty state).
- [x] 2.5 REFACTOR: `npm run lint` + `npx ng build --configuration production`.

## Phase 3: PR3 — panel-errores card + confirmar-reproceso modal

- [x] 3.1 RED: `panel-errores.spec.ts` — `.panel-errores__item` per error; clasificación carries `__clasificacion`; renders nothing when `errores` empty (keep existing spec).
- [x] 3.2 GREEN: `panel-errores.html` class attrs only (5 testids unchanged); create `panel-errores.css` — `.alerta--informativa` shape: 1px `var(--estado-error-borde)`, no fill, ink on clasificación only; date `.tabular-nums`.
- [x] 3.3 RED: `confirmar-reproceso.spec.ts` — backdrop absent closed / present after `open()` / absent after either close; backdrop click emits cancelar (not confirmar); `keydown.escape` emits cancelar; `document.activeElement` is Cancelar after `open()`; 4 existing `.open` specs stay green.
- [x] 3.4 GREEN: `confirmar-reproceso.ts` — add `readonly abierto = signal(false)` set alongside every `nativeElement.open` write in `open()`/`onConfirmar()`/`onCancelar()`; store `document.activeElement` in `open()`, restore on both close paths. No `showModal()`.
- [x] 3.5 GREEN: `confirmar-reproceso.html` — `@if (abierto())` backdrop `<div class="confirmar-reproceso__fondo" data-testid="confirmar-reproceso-fondo" (click)="onCancelar()">` before `<dialog>`; add `(keydown.escape)="onCancelar()"`; title/actions wrappers; testids unchanged. Create `confirmar-reproceso.css` — centered fixed card, `--radio-modal`, `--sombra-prominente`, `--fondo-scrim`, `:not([open]){display:none}`.
- [x] 3.6 REFACTOR: `npm run lint` + `npx ng build --configuration production`.

## Phase 4: Verification

- [x] 4.1 Full `npx ng test --no-watch` green; confirm read-only files untouched (`inbox-screen/spec.md`, `bandeja/spec.md`, `inbox.service.ts`, bandeja query, filter semantics, pagination, `chipsDe()`, reprocesar window).
- [x] 4.2 Map each spec requirement (spa-visual-bandeja 1–7, spa-design-tokens ADDED/MODIFIED) to its covering spec/test; record in apply evidence.

## Requirement → Task Map

| Requirement | Tasks |
|---|---|
| tokens: estado chip primitives (spa-design-tokens ADDED) | 1.1, 1.4 |
| tokens: estado trios both themes, derived, no hue (ADDED) | 1.1, 1.3 |
| tokens: WCAG AA per pair (MODIFIED) | 1.2, 1.3 |
| visual R1 consume tokens / no literals / budget | 1.3, 1.7, 1.9, 2.4, 3.2, 3.5, 1.10, 2.5, 3.6 |
| visual R2 inbox-page header + shell | 1.6, 1.7 |
| visual R3 inbox-filter horizontal bar, signals frozen | 1.8, 1.9 |
| visual R4 inbox-list table + derived Estado chip | 2.1, 2.2, 2.3, 2.4 |
| visual R5 panel-errores restrained card | 3.1, 3.2 |
| visual R6 confirmar-reproceso modal + manual backdrop | 3.3, 3.4, 3.5 |
| visual R7 estado pairs WCAG AA both themes | 1.2, 1.3 |

## Parallelism

- Sequential within each PR (TDD RED→GREEN→REFACTOR).
- PR1 blocks PR2 and PR3 (both need `.chip--error/--alerta` and `--estado-*`/`--fondo-scrim`).
- PR2 and PR3 are mutually independent — can be developed in parallel once PR1 lands.

## Threat Matrix

N/A per design — no routing, shell, subprocess, VCS/PR automation, or executable-classification boundary. CSS tokens, component styles, template structure only.
