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

## PR3 (Phase 3) — NOT STARTED
## Phase 4 verification — NOT STARTED
