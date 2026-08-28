# Apply Progress: item-20-ajuste-visual-bandeja

Artifact store: hybrid. Mode: Strict TDD. Runner: `npx ng test --watch=false --include <spec>`
(the `@angular/build:unit-test` builder rejects `--no-watch`; watch already defaults false in
non-TTY, and `--include` is the file selector).

## PR1 — `pr1/item-20-bandeja-tokens-shell` (Phase 1) — COMPLETE (10/10)

### Completed tasks
- [x] 1.1 RED paleta.spec.ts — 6 `--estado-*` tokens resolve `#rrggbb` both themes; `texto`/`borde` === ink; anti-literal; `.chip--error`/`.chip--alerta` bodies reference role token.
- [x] 1.2 RED contraste.spec.ts — `PARES_TINTA_FONDO` +2 estado texto/fondo rows; `TINTAS_NO_TEXTO` +`--estado-error-borde`,`--estado-alerta-borde`.
- [x] 1.3 GREEN styles.css `@layer tokens` — `--estado-error/alerta-{texto,fondo,borde}` + `--fondo-scrim` in BOTH light+dark blocks; pure `var()` aliases of `--error-*`/`--alerta-*`; `borde`=ink; scrim `rgba(15,23,42,.45)` / dark `rgba(0,0,0,.62)`. No new hex.
- [x] 1.4 GREEN styles.css `@layer primitives` after `.chip--descartada` — `.chip--error`/`.chip--alerta`, `.chip--validada` shape, role tokens only.
- [x] 1.5 REFACTOR lint clean; 1.1–1.2 green.
- [x] 1.6 RED inbox-page.spec.ts — h1 "Bandeja principal"; `[data-testid="inbox-subtitulo"]` present; header→filter→list→dialog via `compareDocumentPosition`; load error renders `.banner.banner--error` keeping `role="alert"` + `data-testid="inbox-error"`.
- [x] 1.7 GREEN inbox-page.html `<header>` + h1 + subtitle; error `<p>` → `class="banner banner--error"`; created inbox-page.css (`.inbox-page` column shell, `__cabecera`/`__titulo`/`__subtitulo`); `styleUrl` wired.
- [x] 1.8 RED inbox-filter.spec.ts — 5 `<label>` each carry `campo inbox-filter__campo`; 7 existing specs stay green.
- [x] 1.9 GREEN inbox-filter.html class attrs only (all testids/controls/bound signals unchanged); created inbox-filter.css (horizontal wrap bar on card surface, per-field column flex); `styleUrl` wired.
- [x] 1.10 REFACTOR lint clean; prod build clean, no budget warnings (styles.css 8.22kB; inbox-page chunk 13.65kB). Component CSS ~0.6kB / ~0.9kB, far under 4kB.

### TDD Cycle Evidence
| Task | Test File | Layer | Safety Net | RED | GREEN | TRIANGULATE | REFACTOR |
|------|-----------|-------|------------|-----|-------|-------------|----------|
| 1.1 | paleta.spec.ts | Unit (fs read of styles.css) | ✅ 109/109 (4-spec baseline) | ✅ 4 tests, tokens absent + chip regex miss | ✅ after 1.3/1.4 | ✅ 6 tokens × 2 themes + alias identity + anti-literal + 2 chip regex | ➖ none needed |
| 1.2 | contraste.spec.ts | Unit | ✅ baseline | ✅ 23 failing (token-absent) | ✅ after 1.3 | ✅ 4 pair + 16 border cases (both themes, `describe.each`) | ➖ |
| 1.3 | (styles.css) | — | ✅ | via 1.1/1.2 | ✅ 118/118 | n/a structural | ✅ comment records TINTAS_TEXTO decoupling rule |
| 1.4 | (styles.css) | — | ✅ | via 1.1 chip regex | ✅ | n/a | ✅ |
| 1.6 | inbox-page.spec.ts | Integration (TestBed) | ✅ 8/8 | ✅ 3 tests fail (no header/subtitle/banner class) | ✅ 11/11 | ✅ 3 distinct assertions (h1 text, doc order, error banner semantics) | ➖ |
| 1.7 | inbox-page.html/.css/.ts | Integration | ✅ | via 1.6 | ✅ 11/11 | n/a | ✅ |
| 1.8 | inbox-filter.spec.ts | Integration | ✅ 7/7 | ✅ 1 test fails (label class) | ✅ 8/8 | ➖ single structural assertion; 7 existing behavior specs stay green = presentational proof | ➖ |
| 1.9 | inbox-filter.html/.css/.ts | Integration | ✅ | via 1.8 | ✅ 8/8 | n/a | ✅ |

### Work Unit Evidence (PR1)
| Evidence | Value |
|---|---|
| Focused test command + result | `npx ng test --watch=false --include {paleta,contraste,inbox-page,inbox-filter}.spec.ts` → 4 files, **137 passed** (baseline 109 + 28 new), 0 unhandled errors |
| Runtime harness command + result | `npx ng build --configuration production` → bundle complete, no budget warning; styles.css 8.22kB, inbox-page chunk 13.65kB |
| Lint | `npm run lint` (`tsc --noEmit` app + spec) → clean |
| Rollback boundary | `styles.css` token/primitive block (D1); `paleta.spec.ts` + `contraste.spec.ts` diffs; new `inbox-page.css` / `inbox-filter.css` (delete to restore); `inbox-page.html` / `inbox-filter.html` class attrs; `styleUrl` line in each `.ts`; the `.catch(() => undefined)` in `inbox-page.ts` effect |

### Deviations from design
- `inbox-page.ts` also gained `.catch(() => undefined)` on the effect's `void this.inboxService.cargar(...)`.
  Design's File Changes table lists only inbox-page.html + .css. Reason: task 1.6 requires a test of
  the load-error path; `cargar` re-throws on failure (for its own service spec), so without the catch
  the container's init fetch produces an unhandled promise rejection that vitest flags as a run error.
  The change is behavior-neutral for the container (it only ever consumed the `error()` signal) and
  removes a latent unhandled-rejection on every failed inbox load in production. Not in the frozen
  list (frozen: inbox-list, panel-errores, confirmar-reproceso, inbox.service.ts, #13 semantics).
- Subtitle copy: "Qué necesito atender hoy: incidencias con error, alertas de calidad y facturas
  pendientes de promover." (design left exact copy open; answers the required question).

### Open Questions (unchanged, for verify/user)
- Estado precedence rules 1 vs 2 (DESCARTADO first) — PR2.
- Backdrop click + Escape both emit `cancelar` — PR3.
- `inbox-list` empty state — PR2.

## PR2 — `pr2/item-20-inbox-list-table` (Phase 2) — COMPLETE (5/5)

Branched off `pr1/item-20-bandeja-tokens-shell` @ `ccdd96b`.

### Completed tasks
- [x] 2.1 RED inbox-list.spec.ts — `table.tabla`; column headers preserved in order; one `[data-testid="chip-estado"]` per row; 5 precedence cases (DESCARTADO-with-errors → "Descartada"; error history → "Error"; quality flag → "Alerta" over Validada; clean PROMOVIDO → "Validada"; PENDIENTE → "Pendiente"); INCIDENCIA `indicadores:null` does not throw.
- [x] 2.2 RED regression lock — `[data-testid="indicador-chip"]` count+labels byte-identical (`['Proveedor genérico','Campos no extraídos']`) for the representative promoted item; new `[data-testid="inbox-vacio"]` empty state.
- [x] 2.3 GREEN inbox-list.ts — added `ClaseChipEstado` type, `ChipEstado` interface, module-level pure `chipEstadoDe(item)` beside `chipsDe()`; `FilaInbox` gained `chipEstado`; `filas` computed gained one line `chipEstado: chipEstadoDe(item)`. `chipsDe()` untouched. `styleUrl` wired.
- [x] 2.4 GREEN inbox-list.html — `class="tabla inbox-list"`; Estado cell renders `<span [class]="fila.chipEstado.clase" data-testid="chip-estado">`; `__fecha`/`__indicadores`/`__acciones` cell classes; `@empty` row with `data-testid="inbox-vacio"`. Created inbox-list.css (`:host` card + `overflow-x:auto`, `all-small-caps` header, `.inbox-list__fecha` component-scoped tabular-nums NOT global `.tabular-nums`, indicator chip spacing, empty-state).
- [x] 2.5 REFACTOR — lint clean; full suite green; prod build clean, no `anyComponentStyle` warning.

### TDD Cycle Evidence
| Task | Test File | Layer | Safety Net | RED | GREEN | REFACTOR |
|------|-----------|-------|------------|-----|-------|----------|
| 2.1 | inbox-list.spec.ts | Integration (TestBed) | ✅ 14/14 baseline | ✅ 8 failing (no table.tabla, no chip-estado, precedence unresolved) | ✅ 22/22 | ➖ |
| 2.2 | inbox-list.spec.ts | Integration | ✅ | ✅ regression + empty-state failing | ✅ 22/22 | ➖ |
| 2.3 | inbox-list.ts | — | ✅ | via 2.1/2.2 | ✅ | ✅ pure fn, no signal/service/store (ADR 0009) |
| 2.4 | inbox-list.html/.css | Integration | ✅ | via 2.1/2.2 | ✅ | ✅ |

### Work Unit Evidence (PR2)
| Evidence | Value |
|---|---|
| Focused test command + result | `npx ng test --watch=false --include "**/inbox-list.spec.ts"` → **22 passed** (14 baseline + 8 new) |
| Full suite | `npx ng test --watch=false` → **335 passed / 34 files**, 0 failures |
| Runtime harness | `npx ng build --configuration production` → bundle complete 4.4s, no `anyComponentStyle` budget warning; styles.css 8.22 kB unchanged; inbox-list.css ~1 kB folded into lazy inbox chunk |
| Lint | `npm run lint` (`tsc --noEmit` app + spec) → clean |
| Rollback boundary | `inbox-list.ts` (added `ClaseChipEstado`/`ChipEstado`/`chipEstadoDe` + one field + one `filas` line + `styleUrl`), `inbox-list.html` class attrs + Estado cell + `@empty`, new `inbox-list.css` (delete to restore), `inbox-list.spec.ts` diff. `chipsDe()`, reprocesar, `<details>`/panel-errores wiring untouched. |
| Authored diff | 216 insertions / 5 deletions = **221 changed lines** (within 400 budget; ~130 of the additions are the new spec) |

### Deviations from design
- None material. Design D3 named the interface fields `etiqueta`/`clase` with `clase` as the full `chip chip--x` string — followed exactly; template uses `[class]="fila.chipEstado.clase"` (whole-string binding, no static `class`).
- Header "small-caps" satisfied with `font-variant: all-small-caps` on `.inbox-list thead th` layered on top of the global `.tabla th` `text-transform: uppercase`.

## PR3 — `pr3/item-20-panel-modal` (Phase 3) — COMPLETE (6/6)

Branched off `pr2/item-20-inbox-list-table` @ `f747c08`.

### Completed tasks
- [x] 3.1 RED panel-errores.spec.ts — 2 new tests: one `.panel-errores__item` per error +
  `.panel-errores__clasificacion` on the classification span; container carries `.panel-errores`
  and NO `.alerta--bloqueante` / `.banner--error` fill class. Existing empty-array spec kept.
- [x] 3.2 GREEN panel-errores.html — class attributes only (`panel-errores__item`,
  `__clasificacion`, `__mensaje`, `__fecha`); all 5 testids + `| date: 'short'` unchanged. New
  panel-errores.css (1094 B): `.alerta--informativa` shape — `1px solid var(--estado-error-borde)`,
  `background: transparent` (NO fill), `--estado-error-texto` on the clasificación only, date
  `font-variant-numeric: tabular-nums` (component-scoped, not global `.tabular-nums`). `styleUrl` wired.
- [x] 3.3 RED confirmar-reproceso.spec.ts — 5 new tests: backdrop absent while closed / present
  after `open()` / removed after both close paths; backdrop click → `cancelar` not `confirmar` +
  dialog closes; `keydown.escape` on dialog → `cancelar`; `document.activeElement` === Cancelar
  button after `open()`. 4 existing `.open` specs untouched.
- [x] 3.4 GREEN confirmar-reproceso.ts — added `readonly abierto = signal(false)` set alongside
  every `nativeElement.open` write (`open()` true, private `cerrar()` false used by
  `onConfirmar()`/`onCancelar()`); `open()` stores `document.activeElement` then focuses the
  `#botonCancelar` viewChild; `cerrar()` restores focus. NO `showModal()`. `styleUrl` wired.
- [x] 3.5 GREEN confirmar-reproceso.html — `@if (abierto())` backdrop `<div
  class="confirmar-reproceso__fondo" data-testid="confirmar-reproceso-fondo" (click)="onCancelar()">`
  before `<dialog>`; `(keydown.escape)="onCancelar()"` on the dialog; `__titulo` + `__acciones`
  wrappers; `#botonCancelar` ref + `.btn`/`.btn--secundario` primitives; both testids unchanged.
  New confirmar-reproceso.css (1070 B): fixed centered card `translate(-50%,-50%)`, `--radio-modal`,
  `--sombra-prominente`, `--borde-sutil`; `__fondo` fixed `inset:0` `z-index:1` `var(--fondo-scrim)`;
  card `z-index:2`; `.confirmar-reproceso:not([open]){display:none}`.
- [x] 3.6 REFACTOR — `npm run lint` clean; full suite 342 passed; prod build clean, no
  `anyComponentStyle` budget warning.

### TDD Cycle Evidence
| Task | Test File | Layer | Safety Net | RED | GREEN | REFACTOR |
|------|-----------|-------|------------|-----|-------|----------|
| 3.1 | panel-errores.spec.ts | Integration (TestBed) | ✅ 2/2 baseline | ✅ 2 failing (`.panel-errores__item` 0≠2; classes absent) | ✅ 4/4 | ➖ |
| 3.2 | panel-errores.html/.css/.ts | Integration | ✅ | via 3.1 | ✅ 4/4 | ✅ token-only, outside `@layer` |
| 3.3 | confirmar-reproceso.spec.ts | Integration | ✅ 4/4 baseline | ✅ 3 failing (no backdrop; focus stays `<body>`) | ✅ 9/9 | ➖ |
| 3.4 | confirmar-reproceso.ts | — | ✅ | via 3.3 | ✅ | ✅ `abierto` is a `signal` (ADR 0009), no service/store |
| 3.5 | confirmar-reproceso.html/.css | Integration | ✅ | via 3.3 | ✅ 9/9 | ✅ manual scrim element, no `showModal()`/`::backdrop` |

### Work Unit Evidence (PR3)
| Evidence | Value |
|---|---|
| Focused test command + result | `npx ng test --watch=false --include "**/panel-errores.spec.ts" --include "**/confirmar-reproceso.spec.ts"` → **13 passed** (8 baseline + 5 new) |
| Full suite | `npx ng test --watch=false` → **342 passed / 34 files**, 0 failures (was 335; +2 panel-errores +5 confirmar-reproceso… net +7) |
| Runtime harness | `npx ng build --configuration production` → bundle complete 4.6s; NO `anyComponentStyle` warning; styles.css 8.22 kB unchanged; inbox-page lazy chunk 17.79 kB |
| Lint | `npm run lint` (`tsc --noEmit` app + spec) → clean |
| Component CSS sizes | panel-errores.css **1094 B**, confirmar-reproceso.css **1070 B** — both far under the 4 kB `anyComponentStyle` warn threshold |
| Authored diff vs `pr2/item-20-inbox-list-table` | tracked 161 ins / 18 del + 2 new `.css` (~87 lines) ≈ **266 changed lines** (within 400) |
| Rollback boundary | new `panel-errores.css` + `confirmar-reproceso.css` (delete to restore); `panel-errores.{html,ts}` class-attr / `styleUrl` diff; `confirmar-reproceso.{html,ts}` (`abierto` signal + focus store/restore + backdrop element + wrappers) diff; both `.spec.ts` diffs. `onCancelar()`/`onConfirmar()` emit contract, `chipsDe()`, `inbox.service.ts`, #13 semantics untouched. |

### Deviations from design
- `confirmar-reproceso` buttons gained `.btn` / `.btn--secundario` primitive classes and a
  `__acciones` flex wrapper (design named only "actions wrapper"). Purely presentational, keeps
  both testids and the click handlers. No behavior change.
- Focus restore added in the private `cerrar()` helper shared by both close paths (design said
  "restore on both close paths" — one helper satisfies both).
- `panel-errores` field names confirmed against `bandeja-item.model.ts` `ErrorProcesamiento`:
  `{ clasificacion, mensaje, ocurridoEn }` all present — design assumption held, no rename needed.

## Phase 4 — Verification — COMPLETE (2/2)

- [x] 4.1 Full `npx ng test --watch=false` → **342 passed / 34 files, 0 failures**. Read-only
  files confirmed untouched: `git diff --name-only pr2/item-20-inbox-list-table` lists only the
  6 PR3 scope files (+ 2 untracked new `.css`). `styles.css`, `inbox.service.ts`, `inbox-list.*`,
  `inbox-page.*`, `inbox-filter.*`, `openspec/specs/inbox-screen/spec.md`,
  `openspec/specs/bandeja/spec.md` — zero changes. `chipsDe()`, bandeja query, filter semantics,
  pagination, reprocesar 5-min window — not in any touched file.
- [x] 4.2 Requirement → evidence map below.

### Requirement Coverage (spa-visual-bandeja + spa-design-tokens delta)

| Requirement | Status | Evidence |
|---|---|---|
| **spa-visual-bandeja R1** — inbox components consume tokens, layout-only CSS outside `@layer`, no literals, within 4 kB budget | Met (PR1–PR3) | PR3: `panel-errores.css` / `confirmar-reproceso.css` every value a `var(--*)` token, header comment states "outside the @layer stack"; build has no `anyComponentStyle` warning; 1094 B / 1070 B |
| **R2** — inbox-page header + shell | Met (PR1) | `inbox-page.spec.ts` h1 "Bandeja principal" + subtitle + doc-order tests (PR1, 11/11) |
| **R3** — inbox-filter horizontal bar, signals frozen | Met (PR1) | `inbox-filter.spec.ts` label-class test + 7 unchanged behavior specs green (PR1, 8/8) |
| **R4** — inbox-list table + derived Estado chip | Met (PR2) | `inbox-list.spec.ts` `table.tabla` + `chip-estado` per row + 5 precedence cases + `indicador-chip` regression lock (PR2, 22/22) |
| **R5** — panel-errores restrained card (informativa shape, no fill, renders nothing when empty; rows show clasificación/mensaje/ocurridoEn) | Met (PR3) | `panel-errores.spec.ts:34` fields render; `:47` empty → `panel-errores` container null; `:54` `.panel-errores__item` ×2 + `__clasificacion`; `:71` container has `.panel-errores`, NO `.alerta--bloqueante` / `.banner--error`. `panel-errores.css:12` `border:1px solid var(--estado-error-borde)` + `background:transparent`; `:29` ink on `__clasificacion` only |
| **R6** — confirmar-reproceso modal card + manual backdrop, `<dialog>` stays non-modal via `.open` (no `showModal()`, no `::backdrop`), 2 buttons keep behavior | Met (PR3) | `confirmar-reproceso.ts:38` `nativeElement.open = true` (no `showModal()`); `.html:2` `@if (abierto())` manual `__fondo` div w/ `var(--fondo-scrim)`; `.css:14` fixed centered card `--radio-modal` + `--sombra-prominente`; `confirmar-reproceso.spec.ts` 9/9 incl. 4 original `.open` specs + backdrop present/absent + click→cancelar + Escape→cancelar + focus→Cancelar |
| **R7** — new estado pairs pass WCAG AA both themes | Met (PR1) | `contraste.spec.ts` 4 pair cases (`--estado-error-texto`/`--estado-alerta-texto` over own `-fondo`, ≥4.5) + 16 border cases, both themes (PR1) |
| **spa-design-tokens ADDED** — `.chip--error` / `.chip--alerta` primitives, `.chip--validada` shape, token-driven | Met (PR1) | `styles.css:395-405` `@layer primitives`; `paleta.spec.ts` chip-body regex tests |
| **spa-design-tokens ADDED** — estado error/alerta trios both themes, `texto` derived from `--error-ink`/`--alerta-ink`, no new hue | Met (PR1) | `styles.css:146-151` (claro) / `:212-217` (oscuro) pure `var()` aliases; `paleta.spec.ts` alias-identity + anti-literal tests |
| **spa-design-tokens MODIFIED** — WCAG AA per token pair now names the estado error/alerta pairs | Met (PR1) | `contraste.spec.ts` `PARES_TINTA_FONDO` + `TINTAS_NO_TEXTO` rows |

No Partial / Not-met.

### Open Questions carried to verify (design "Open Questions" + apply flags)
- **Estado precedence 1 vs 2** (DESCARTADO row WITH error history shows "Descartada", not "Error") —
  implemented in PR2 per design D3 inference; still flagged for user confirmation.
- **Backdrop click + Escape both emit `cancelar`** — implemented in PR3 per design D4; the prompt
  states this is user-ratified ("same effect as the Cancelar button, no new logic path").
- **`inbox-list` empty state** (`data-testid="inbox-vacio"`) — added in PR2; confirm for #20 vs #21.
- Summary counter cards + rich data columns remain deferred to #21.
