# Design: item-20-ajuste-visual-bandeja (BACKLOG #20 — Ajuste visual de bandeja y panel de errores)

## Technical Approach

Proposal Approach 2, as the #18 playbook applied to the inbox. Three layers, in dependency order:
(1) **append** to the #18 token layer — two `--estado-*` trios plus one scrim token, every value an
alias of an already-AA-verified ink, and two `.chip--*` primitives; (2) **extend the WCAG guard** so
those roles can go RED in `paleta.spec.ts` / `contraste.spec.ts`; (3) **layout-only component CSS +
class-level template restructure** on the 5 inbox components, plus one additive derived Estado chip
in `inbox-list`. No new data, no service, no store, no change to `inbox.service.ts`, `chipsDe()`,
the bandeja query, filters, pagination, or the reprocesar window. `openspec/specs/inbox-screen` and
`openspec/specs/bandeja` stay untouched.

## Architecture Decisions

### D1 — Estado tokens are pure aliases of the existing inks; `fondo` reuses the banner tint

| Option | Tradeoff | Decision |
|---|---|---|
| Raw handoff hexes `#d70015` / `#c93400` | New literals, new AA burden, browner/redder than the AA-tuned inks, forks #18 D1 | ✗ |
| `.chip--error` maps straight onto `--error-*` | No indirection cost; the *role* disappears, a reviewer collapses it | ✗ |
| New softer tints for `fondo` (`#fdf4f4`…) | Prettier chip; 4 new literals + 4 new AA pairs for a sub-perceptual gain, and the chip stops reading as "same thing as the banner" | ✗ |
| Role trios aliasing `--error-ink`/`--error-fondo` and `--alerta-ink`/`--alerta-fondo` | +12 lines; role named, zero new hue, AA already proven transitively | ✓ |

Added to **both** theme blocks in `@layer tokens` (mirrors how every existing `--estado-*` trio is
declared; the bare `:root` is documented as theme-independent and these resolve differently per
theme):

```css
/* item #20 -- estado roles for the derived Estado chip and the panel de errores. Aliases only:
 * no new hue enters the palette (design #18 D1). Decoupling any of these from its ink REQUIRES
 * promoting it into contraste.spec.ts TINTAS_TEXTO. */
--estado-error-texto: var(--error-ink);
--estado-error-fondo: var(--error-fondo);
--estado-error-borde: var(--error-ink);
--estado-alerta-texto: var(--alerta-ink);
--estado-alerta-fondo: var(--alerta-fondo);
--estado-alerta-borde: var(--alerta-ink);
--fondo-scrim: rgba(15, 23, 42, 0.45);   /* oscuro: rgba(0, 0, 0, 0.62) */
```

`borde` = the ink (not a separate border token as `--estado-pendiente-borde`/`--estado-validada-borde`
do), matching the precedent already set by `.banner--error` and `.asiento-lineas__cuadre--desbalance`,
which both draw `1px solid var(--error-ink)` over `var(--error-fondo)`.

`@layer primitives`, immediately after `.chip--descartada`:

```css
.chip--error  { color: var(--estado-error-texto);  background: var(--estado-error-fondo);
                border-color: var(--estado-error-borde); }
.chip--alerta { color: var(--estado-alerta-texto); background: var(--estado-alerta-fondo);
                border-color: var(--estado-alerta-borde); }
```

**Verified, not assumed** — every value is already asserted by the existing suite:
`--error-ink`/`--alerta-ink` are in `TINTAS_TEXTO` (≥4.5 vs all 4 surfaces, both themes) and
`[--error-ink, --error-fondo]` / `[--alerta-ink, --alerta-fondo]` are already in
`PARES_TINTA_FONDO`. Dark `--alerta-ink #f0b45f` on `--fondo-superficie #2c2c2e` computes 7.6:1.

### D2 — Guard extension: assert the ROLE names, not the ramp names

`contraste.spec.ts`, exactly two edits:

| Table | Added | New cases |
|---|---|---|
| `PARES_TINTA_FONDO` | `['--estado-error-texto','--estado-error-fondo', TEXTO]`, `['--estado-alerta-texto','--estado-alerta-fondo', TEXTO]` | 2 × 2 themes = 4 |
| `TINTAS_NO_TEXTO` | `'--estado-error-borde'`, `'--estado-alerta-borde'` | 2 × 4 surfaces × 2 themes = 16 |

**Not** added to `TINTAS_TEXTO`. Chip text renders over the chip's own tint, never bare over a
surface, so `PARES_TINTA_FONDO` is the correct assertion; `TINTAS_TEXTO × SUPERFICIES` would be 16
cases duplicating what `--error-ink`/`--alerta-ink` already prove. This is also the existing shape:
`--estado-pendiente-texto` is likewise absent from `TINTAS_TEXTO`. The token comment records the
condition under which that stops being true.

`paleta.spec.ts` gains three tests — parity, aliasing, and the anti-literal guard (the #20 analogue
of "un literal azul solo aparece en la definicion del ramp"):

```ts
it('los seis tokens de estado error/alerta existen resueltos en ambos temas', /* 6 names × 2 temas, /^#[0-9a-f]{6}$/i */);
it('el chip Error/Alerta reutiliza la tinta AA existente (sin hue nuevo)', () => {
  expect(claro.get('--estado-error-texto')).toBe(claro.get('--error-ink'));
  expect(oscuro.get('--estado-alerta-texto')).toBe(oscuro.get('--alerta-ink'));  /* …4 asserts */ });
it('los hexes de estado del handoff nunca entran a la hoja', () =>
  expect(CSS).not.toMatch(/#d70015|#c93400|#ff453a|#ff9f0a/i));
it('.chip--error/.chip--alerta consumen el token de rol, no un literal', () =>
  expect(CSS).toMatch(/\.chip--error\s*\{[^}]*var\(--estado-error-texto\)/));
```

The last one is the only assertion that can make the primitive *rule body* RED — jsdom cannot
observe a global stylesheet, so a DOM test can prove the class is applied but never that the class
is styled. Stating this openly is the point of #18 D2: the guard reads `styles.css`.

**RED sequence**: write all `paleta.spec.ts` + `contraste.spec.ts` additions first → RED
(`token --estado-error-texto ausente en tema claro`, and the `.chip--error` regex miss) → add the
12 declarations + 2 primitive rules to `styles.css` → GREEN.

### D3 — Estado chip derives in a module-level pure function inside `inbox-list`

`inbox-list` is a dumb component that *already* owns a module-level pure derivation (`chipsDe`).
The Estado chip is the same shape, so it goes in the same place and is memoized by the same existing
`filas` computed — no new service, no store, no signal (ADR 0009), no second pass over `items()`.

```ts
type ClaseChipEstado = `chip chip--${'error' | 'alerta' | 'validada' | 'pendiente' | 'descartada'}`;
interface ChipEstado { readonly etiqueta: string; readonly clase: ClaseChipEstado; }

/** Presentation-only. Reads existing BandejaItem fields; `chipsDe()` is untouched (#13 frozen). */
function chipEstadoDe(item: BandejaItem): ChipEstado;
```

Exact precedence, **first match wins**:

| # | Condition | Etiqueta | Clase |
|---|---|---|---|
| 1 | `estadoConsumo === 'DESCARTADO'` | Descartado | `chip--descartada` |
| 2 | `errores.length > 0` | Error | `chip--error` |
| 3 | `indicadores !== null && (esProveedorGenerico \|\| posibleDuplicado)` | Alerta | `chip--alerta` |
| 4 | `estadoConsumo === 'PROMOVIDO'` | Validada | `chip--validada` |
| 5 | otherwise (`PENDIENTE`) | Pendiente | `chip--pendiente` |

DESCARTADO ranks **first**, not last: the ratified input says descartado *keeps* `.chip--descartada`
unconditionally, and any lower rank would break that for a descartado row that also has error
history. Descartado is a terminal lifecycle fact; Error/Alerta are quality signals over a live row,
and that row still shows its error count, its `<details>` panel and its reprocesar button. Rule 3 is
null-safe for `origen === 'INCIDENCIA'` (`indicadores: null`). Flagged in Open Questions — the
precedence between rules 1 and 2 is an inference from the ratified wording, not an explicit ruling.

Coexistence: `chipEstadoDe` reads `estadoConsumo`, `errores.length` and two flags; `chipsDe` reads
`indicadores` and maps all five flags to per-indicator labels. Separate functions, separate columns,
no shared code path. `chipsDe()` and `FilaInbox.chips` change zero bytes; `FilaInbox` gains one
field, `filas` gains one line.

### D4 — `confirmar-reproceso`: a real backdrop element driven by an additive signal

`showModal()` and `::backdrop` are both unavailable, and not only under jsdom: `::backdrop` paints
only for a dialog promoted to the top layer, which *only* `showModal()` does. Under `.open = true`
(#13 D6) there is no `::backdrop` in jsdom **or** in a real browser. A manual element is the correct
rendering path, not a test workaround.

```html
@if (abierto()) {
  <div class="confirmar-reproceso__fondo" data-testid="confirmar-reproceso-fondo" (click)="onCancelar()"></div>
}
<dialog #dialogo class="confirmar-reproceso" (keydown.escape)="onCancelar()"> … </dialog>
```

`readonly abierto = signal(false)` is set alongside the existing `nativeElement.open` writes in
`open()`/`onConfirmar()`/`onCancelar()` — the `.open` attribute contract and all four existing specs
stay green. Backdrop before the dialog in source order, `z-index` 1 / 2. Focus: `open()` focuses the
Cancelar button (safe default) after storing `document.activeElement`; both close paths restore it.
Backdrop click and Escape both route to `onCancelar()` — an additive cancel affordance, flagged.

### D5 — Dates use a component-scoped tabular class, not the global `.tabular-nums`

`@layer base .tabular-nums` sets `text-align: right`, which is correct for money and wrong for a
date. #20 has no money column (rich columns are #21), so the list uses
`.inbox-list__fecha { font-variant-numeric: tabular-nums; text-align: left; }` and leaves the global
primitive alone. Deviates from the proposal's shorthand "`.tabla`/`.tabular-nums`" — `.tabla` is
adopted as written.

## Data Flow

```
GET /api/bandeja ─→ InboxService.items ─→ InboxPage ─┬→ inbox-filter   (classes only, same outputs)
                                                     ├→ inbox-list
                                                     │    filas = items.map(item => ({
                                                     │      item,
                                                     │      chips:      chipsDe(item.indicadores),   ← FROZEN
                                                     │      chipEstado: chipEstadoDe(item),          ← NEW, additive
                                                     │      reprocesarDisponible: …                  ← FROZEN
                                                     │    }))
                                                     │    └→ <details> → panel-errores  (card, .alerta--informativa shape)
                                                     └→ confirmar-reproceso  (abierto() → backdrop + card)
```

## File Changes

| File | Action | Description |
|---|---|---|
| `SmartNetWeb/src/styles.css` | Modify | 6 estado tokens × 2 themes + `--fondo-scrim` × 2; `.chip--error`/`.chip--alerta` in `@layer primitives` |
| `src/app/shared/paleta.spec.ts` | Modify | 4 tests: parity, alias identity, anti-literal, primitive-consumes-token |
| `src/app/shared/contraste.spec.ts` | Modify | 2 `PARES_TINTA_FONDO` rows + 2 `TINTAS_NO_TEXTO` names |
| `inbox/feature/inbox-page/inbox-page.html` | Modify | `<header>` + h1 "Bandeja principal" + subtitle; error `<p>` → `.banner .banner--error` (existing primitive, `role="alert"` + testid kept) |
| `inbox/feature/inbox-page/inbox-page.css` | Create | `:host` column shell, `__cabecera`, `__titulo`, `__subtitulo` (~30 lines) |
| `inbox/ui/inbox-filter/inbox-filter.html` | Modify | Class attributes only — `campo inbox-filter__campo` on each `<label>`. All 5 testids and both selects unchanged |
| `inbox/ui/inbox-filter/inbox-filter.css` | Create | Horizontal wrap bar on a card surface; per-field column flex overrides the base `label{display:block}` (component CSS is outside `@layer`) (~30) |
| `inbox/ui/inbox-list/inbox-list.ts` | Modify | `ClaseChipEstado`, `ChipEstado`, `chipEstadoDe`, one `FilaInbox` field, one `filas` line |
| `inbox/ui/inbox-list/inbox-list.html` | Modify | `class="tabla inbox-list"`; Estado cell → chip; `__fecha`/`__indicadores`/`__acciones` wrappers; empty state |
| `inbox/ui/inbox-list/inbox-list.css` | Create | `:host` card + `overflow-x:auto`, indicator-chip hairline, detalle stack, empty state (~45) |
| `inbox/ui/panel-errores/panel-errores.html` | Modify | Class attributes only; `<ul>`/`<li>`/3 `<span>` and all 5 testids unchanged |
| `inbox/ui/panel-errores/panel-errores.css` | Create | `.alerta--informativa` shape: 1px `--estado-error-borde`, no fill, ink on the clasificación only (~30) |
| `inbox/ui/confirmar-reproceso/confirmar-reproceso.ts` | Modify | `abierto` signal + focus store/restore |
| `inbox/ui/confirmar-reproceso/confirmar-reproceso.html` | Modify | Backdrop element, title/actions wrappers; both testids unchanged |
| `inbox/ui/confirmar-reproceso/confirmar-reproceso.css` | Create | Centered fixed card, `--radio-modal`, `--sombra-prominente`, `--fondo-scrim` backdrop, `:not([open]){display:none}` (~40) |

Every new `.css` is 30–45 lines ≈ 1–1.5 kB, well inside `anyComponentStyle` 4 kB warn / 8 kB error.
No component reaches into `styles.css` for anything beyond tokens and the existing
`.chip` / `.tabla` / `.campo` / `.banner` / `.btn` primitives; the only global additions are D1's.
h1 rename "Bandeja de entrada" → "Bandeja principal" is safe — grep confirms no spec or merged spec
asserts the current text, and `DESIGN.md` L135 + handoff §2 both name the screen "Bandeja principal".

## Testing Strategy (Strict TDD — RED first, `npx ng test --no-watch`, vitest 4 / jsdom)

| Spec | New assertions |
|---|---|
| `paleta.spec.ts` | 6 role tokens resolve to `#rrggbb` in both themes; each `texto` equals its ink; handoff estado hexes absent from the sheet; `.chip--error`/`.chip--alerta` bodies reference the role tokens |
| `contraste.spec.ts` | 4 pair cases (texto vs fondo ≥ 4.5) + 16 border cases (≥ 3 vs all 4 surfaces) |
| `inbox-page.spec.ts` | `<h1>` text; subtitle present; document order header → filter → list → dialog (`compareDocumentPosition`, #18 precedent); error renders as `.banner--error` keeping `role="alert"` + `data-testid="inbox-error"` |
| `inbox-filter.spec.ts` | Each `<label>` carries `campo inbox-filter__campo`; 5 controls still emit — **7 existing specs must stay green untouched** (proves the restructure is class-level) |
| `inbox-list.spec.ts` | `table.tabla`; `[data-testid="chip-estado"]` exists once per row; **5 precedence cases** (descartado+errores → `chip--descartada`; errores → `chip--error`; genérico *or* duplicado → `chip--alerta`; promovido limpio → `chip--validada`; pendiente limpio → `chip--pendiente`); INCIDENCIA row (`indicadores: null`) does not throw; `[data-testid="indicador-chip"]` count and labels **unchanged** (the `chipsDe()` regression lock); empty state |
| `panel-errores.spec.ts` | `.panel-errores__item` per error; clasificación carries `__clasificacion`; renders nothing when empty (existing) |
| `confirmar-reproceso.spec.ts` | Backdrop absent when closed, present after `open()`, absent after either close path; backdrop click emits `cancelar` without `confirmar`; Escape emits `cancelar`; Cancelar button holds `document.activeElement` after `open()`; **4 existing `.open` specs stay green** |

No `dotnet test` surface — #20 touches no .NET, no SQL, no API.

## Threat Matrix

N/A — no routing change, shell, subprocess, VCS/PR automation, executable-file classification, or
process-integration boundary. CSS tokens, component styles, and template structure only.

## PR sequencing (400-line budget, `ask-on-risk`)

| PR | Scope | Est. lines (+/−) | Depends on |
|---|---|---|---|
| 1 | Tokens + primitives + guard extension + inbox-page shell + inbox-filter bar | ~245 | — |
| 2 | `inbox-list` `.tabla` + derived Estado chip | ~200 | 1 (needs `.chip--error`/`.chip--alerta`) |
| 3 | `panel-errores` card + `confirmar-reproceso` modal | ~215 | 1 (needs `--estado-error-*`, `--fondo-scrim`) |

`Decision needed before apply: No` · `Chained PRs recommended: Yes` · `400-line budget risk: Low`.

Unlike #18's PR1 (~480, it *authored* the token layer), #20 only appends to it, so the guard slice
has room to carry the page shell and the filter bar — and those two are the smallest, lowest-risk
templates, which makes PR1 a natural "prove the tokens, then use them once" unit. PR2 and PR3 are
independent of each other; Feature Branch Chain still stacks them linearly: PR1 → tracker,
PR2 → PR1, PR3 → PR2.

## Migration / Rollout

No data migration, no schema change, no API change, no feature flag. Token and primitive additions
are purely additive; the 5 component `.css` files are new (delete to restore baseline); each
template restructure reverts per file. Every PR is independently revertible.

## Open Questions

- [ ] Estado precedence rules 1 vs 2: a DESCARTADO row **with** error history shows `Descartado`,
      not `Error` (D3). Inferred from "DESCARTADO keeps `.chip--descartada`" — confirm.
- [ ] Backdrop click and Escape both emit `cancelar` (D4). Standard modal UX and within the existing
      cancel affordance, but it is a new way to trigger it — confirm.
- [ ] `inbox-list` empty state (`data-testid="inbox-vacio"`) is new DOM not named in the proposal.
      Presentation-only; confirm it is wanted in #20 rather than deferred with #21.
- [ ] Indicator chips get a component-scoped hairline (`.inbox-list__indicador`) rather than a new
      global neutral `.chip--*` primitive, to avoid touching the #13 indicator surface. Confirm.
- [ ] Summary counter cards and rich data columns remain deferred to #21 — the user's "doesn't match
      the design" impression may be driven mostly by the missing counters (proposal risk, restated).
