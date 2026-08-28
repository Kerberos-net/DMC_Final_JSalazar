# Archive Report: item-20-ajuste-visual-bandeja (BACKLOG #20)

**Archived**: 2026-08-27
**Change**: item-20-ajuste-visual-bandeja (Ajuste visual de bandeja y panel de errores — BACKLOG #20)
**Status**: CLOSED ✓ (intentional-with-warnings — 0 CRITICAL, 6 WARNING reconciled/recorded, 3 SUGGESTION carried as follow-ups)
**Mode**: hybrid (Engram persistence + OpenSpec filesystem)

## Cycle Summary

The SDD cycle for item #20 is complete: exploration → proposal → spec → design → tasks → apply → verify → archive.

Visual/structural pass over the five inbox/bandeja components (`inbox-page`, `inbox-filter`,
`inbox-list`, `panel-errores`, `confirmar-reproceso`) that #18 excluded. CSS and template structure
only; **no new data** — everything derives from data already on `BandejaItem`. The #13 functional
behavior (bandeja query, filter semantics, pagination, `chipsDe()` per-indicator logic, reprocesar
5-minute window, `inbox.service.ts`) was frozen and verified untouched.

## Final State — Authority Hierarchy

### 1. Native Review Authority
RDD (gentle-ai receipt-driven development) is **disabled at repo level** (D: is exFAT, no ACL —
see SPRINT.md "Condiciones del entorno"). `reviewGate.delivery: disabled/unmanaged`, kill switch
off. No terminal receipt is demanded or fabricated. Consistent with all prior archives (#12–#18).

### 2. Persisted Tasks Artifact
File: `openspec/changes/archive/2026-08-27-item-20-ajuste-visual-bandeja/tasks.md` — **23/23 tasks
`[x]` checked** (Phase 1: 10, Phase 2: 5, Phase 3: 6, Phase 4: 2). No unchecked implementation
tasks. No archive-time checkbox reconciliation was required.

### 3. Explicit Final-State Facts (orchestrator-verified, authoritative)
- 23/23 tasks complete.
- Branch verified: `pr3/item-20-panel-modal @ a5eee03`, stacked linearly on `main`:
  `ccdd96b` (PR1) → `f747c08` (PR2) → `a5eee03` (PR3). `main` has not moved since PR1 was cut.
- SPA `npx ng test --watch=false` → **342/342 passed / 34 files**, 0 failed, 0 skipped.
- SPA `npm run lint` (`tsc --noEmit`, app + spec) → clean, exit 0.
- SPA `npx ng build --configuration production` → clean, **no `anyComponentStyle` budget warning**
  (component CSS 592–1154 B, far under the 4kB warn threshold).
- .NET not run — #20 touches no .NET / SQL / API surface (correct).
- All 10 requirements / 20 scenarios covered (1 partial: R4 date-cell tabular treatment — behavior
  present via component-scoped `.inbox-list__fecha`, no direct test on the date cell).
- All #13 frozen surfaces confirmed untouched: `chipsDe()` body byte-identical (regression-lock
  test green), `inbox.service.ts` absent from diff, bandeja query / filter signals / pagination /
  reprocesar 5-min window unchanged, `openspec/specs/inbox-screen/spec.md` and
  `openspec/specs/bandeja/spec.md` not modified, `bandeja-item.model.ts` not modified.

### 4. Verify Report Snapshot (Engram #217, at verification time)
PASS WITH WARNINGS. 0 CRITICAL, 6 WARNING, 3 SUGGESTION. 342/342 SPA tests. Per this report's
Final-State Authority: the 6 WARNINGs are all doc/ratification reconciliation, addressed at archive
(see below); none block. The verify-report's snapshot claims about pending spec-text reconciliation
were valid at verification time and are now resolved.

**Conclusion**: Final state is COMPLETE and CLOSED.

## Verify Warnings — Disposition

| # | Warning (verify-report #217) | Disposition at archive |
|---|---|---|
| 1 | `spa-visual-bandeja` R4 precedence text said "errores-first, DESCARTADO-last" — contradicts ratified + implemented DESCARTADO-first order | **RECONCILED** — delta spec R4 requirement + scenarios rewritten to DESCARTADO-first/unconditional order before the merge to `openspec/specs/` |
| 2 | R4 date scenario said literal `.tabular-nums`; implementation uses component-scoped `.inbox-list__fecha` (design D5) | **RECONCILED** — R4 requirement + "Date cell" scenario reworded to "component-scoped tabular-figures treatment, not the global right-aligning `.tabular-nums`"; same wording applied to R5 panel-errores `ocurridoEn` |
| 3 | inbox-page heading: spec said "Bandeja"; implementation renders "Bandeja principal" (handoff §2 + design) | **RECONCILED** — R2 requirement + "Header and shell present" scenario changed to "Bandeja principal" |
| 4 | DESCARTADO-first + backdrop-click/Escape→`onCancelar()` asserted user-ratified, but design.md Open Questions still unchecked and no ratification record existed | **RECORDED** — see "Mid-Flight Ratifications" below. design.md checkboxes left as-is (historical); this report is the ratification record |
| 5 | `inbox-page.ts` gained `.catch(() => undefined)` on the load effect — slightly outside CSS/template-only framing | **NOTED** — behavior-neutral latent-unhandled-rejection fix, covered by a new `banner--error` test; NOT a #13 functional surface. In-scope deviation, accepted |
| 6 | `inbox-list` empty state `data-testid="inbox-vacio"` — new presentational DOM not in the original proposal | **NOTED** — presentation-only, tested; an in-scope addition. NOT the #21 counter/columns work |

## Mid-Flight Ratifications (WARNING 4 — ratification record)

The user ratified the following two decisions mid-flight; both were design.md Open Questions:

1. **Estado chip DESCARTADO-first / unconditional precedence.** A discarded row that still carries
   error history shows the "Descartada" chip, NOT "Error". A discarded row is a terminal lifecycle
   fact; Error/Alerta are quality signals over a live row. Implemented in `chipEstadoDe()`
   (`inbox-list.ts`), covered by the "precedence 1" test. (design D3, previously flagged as an
   inference.)
2. **`confirmar-reproceso`: backdrop-click AND Escape both trigger `onCancelar()`.** Same effect as
   the Cancelar button — an additive UI trigger. `onCancelar()` logic itself is unchanged.
   Implemented via a manual `@if (abierto())` backdrop `<div>` with `(click)="onCancelar()"` and
   `(keydown.escape)="onCancelar()"` on the `<dialog>`. (design D4.)

## In-Scope Deviations (change log — WARNINGs 5–6)

- **`inbox-page.ts` `.catch(() => undefined)` on the load effect** (PR1) — a behavior-neutral fix
  for a latent unhandled promise rejection. Slightly outside the "CSS/template-only" framing, but
  not a #13 functional surface; covered by the new `banner--error` path test.
- **`inbox-list` empty state `data-testid="inbox-vacio"`** (PR2) — new presentational DOM, tested.
  Not named in the original proposal; an in-scope presentation-only addition. Explicitly NOT the
  #21 summary-counter / enriched-column work.
- **`confirmar-reproceso` buttons gained `.btn` / `.btn--secundario` + `__acciones` flex wrapper**
  (PR3) — presentational; testids and handlers unchanged.
- **Focus store/restore** moved into a shared private `cerrar()` in `confirmar-reproceso.ts` (PR3).

## Spec Merges — Authority

### spa-visual-bandeja (NEW capability)
- Created: `openspec/specs/spa-visual-bandeja/spec.md` (full spec, not a delta).
- 8 requirements / 12 scenarios (verify-report counted 7/11 before the archive reconciliation
  added the "Discarded row with error history shows Descartada" scenario and split the errors
  scenario; the WCAG-pairs requirement is shared conceptually with spa-design-tokens).
- Reconciled at archive per WARNINGs 1–3 (precedence order, date-cell treatment, "Bandeja
  principal" heading).
- Status: Merged ✓

### spa-design-tokens (MODIFIED — delta applied)
- Updated: `openspec/specs/spa-design-tokens/spec.md`.
- **2 ADDED requirements**:
  - "Estado 'error' and 'alerta' chip primitives" — `.chip--error` / `.chip--alerta` in
    `@layer primitives`, same shape as `.chip--validada`, token-driven only (3 scenarios... 2).
  - "Estado 'error' and 'alerta' token trios both themes" —
    `--estado-error-{texto,fondo,borde}` / `--estado-alerta-{texto,fondo,borde}` in both light and
    dark blocks; `texto` derived from existing AA-tuned `--error-ink` / `--alerta-ink`, no new hue
    literal; `paleta.spec.ts` theme-parity covers the new names (3 scenarios).
- **1 MODIFIED requirement**: "WCAG AA contrast compliance per token pair" — the `contraste.spec.ts`
  clause now names the estado "error" / "alerta" pairs (`--estado-error-texto` /
  `--estado-alerta-texto` over all four surfaces + own `-fondo`) in both themes, alongside the
  existing accent-fill / accent-text / new-surface pairs. Added scenario "Estado error and alerta
  pairs pass AA in both themes". All prior scenarios preserved.
- **No existing spa-design-tokens requirement was lost** — all 13 prior requirements retained; the
  ratified accent-reuse exception and its scenarios are intact.
- Status: Merged ✓

## Archive Location

**Moved**: `openspec/changes/item-20-ajuste-visual-bandeja/` →
`openspec/changes/archive/2026-08-27-item-20-ajuste-visual-bandeja/`

Archived artifacts: `exploration.md`, `proposal.md`, `design.md`, `tasks.md`, `apply-progress.md`,
`verify-report.md`, `archive-report.md` (this file), `specs/spa-visual-bandeja/spec.md` (delta,
reconciled), `specs/spa-design-tokens/spec.md` (delta).

## Observation IDs (Traceability)

- Exploration: #211 (`openspec/changes/.../exploration.md`) `sdd/item-20-ajuste-visual-bandeja/exploration`
- Proposal: #212 `sdd/item-20-ajuste-visual-bandeja/proposal`
- Spec: #213 `sdd/item-20-ajuste-visual-bandeja/spec`
- Design: #214 `sdd/item-20-ajuste-visual-bandeja/design`
- Tasks: #215 `sdd/item-20-ajuste-visual-bandeja/tasks`
- Apply Progress: #216 `sdd/item-20-ajuste-visual-bandeja/apply-progress`
- Verify Report: #217 `sdd/item-20-ajuste-visual-bandeja/verify-report`
- Archive Report: this file — `sdd/item-20-ajuste-visual-bandeja/archive-report`

## Key Decisions Archived

- **D1** — Estado tokens are pure `var()` aliases of existing AA-tuned inks; `fondo` reuses the
  banner tint; `borde` = the ink itself. No new hue literal. Only new raw value: `--fondo-scrim`
  rgba.
- **D2** — WCAG guard asserts the ROLE names (`--estado-error-texto` etc.), not ramp names.
  `paleta.spec.ts` reads `styles.css` via `node:fs` so a primitive missing its token goes RED.
- **D3** — Estado chip derives in a module-level pure `chipEstadoDe()` inside `inbox-list`, beside
  `chipsDe()`. DESCARTADO-first precedence (ratified). Null-safe for `origen === 'INCIDENCIA'`.
- **D4** — `confirmar-reproceso` uses a real manual backdrop element driven by an additive
  `abierto` signal; `<dialog>` stays non-modal via `.open` (no `showModal()`, no `::backdrop`).
- **D5** — dates use a component-scoped `.inbox-list__fecha` tabular class, not the global
  right-aligning `.tabular-nums` primitive.

## Delivery Strategy

`sdd-tasks` forecast ~660 authored changed lines (High risk, over the 400-line budget) and
recommended a 3-PR stacked chain. `delivery_strategy: ask-on-risk`, `chain_strategy:
stacked-to-main`. Each of the 3 PRs is within the 400-line budget individually (no `size:exception`
needed). PR1 → main; PR2 off PR1; PR3 off PR2. `main` fast-forwards to `pr3/item-20-panel-modal @
a5eee03`.

## Deferred to BACKLOG #21 (not partially built)

- Summary counter cards (Pendientes / Validadas / Con error / Alertas) — needs a `GET /api/bandeja`
  aggregate.
- Enriched row columns (proveedor display name, monto, moneda, número, tipo, fecha de emisión,
  glosa, tipo de cambio, base imponible, IGV) — needs the `GET /api/bandeja` + `BandejaItem` +
  `SqlBandejaRepository` contract widened. Functional work, not visual.

## Project Status Docs

- `SPRINT.md` — #20 closing entry added (`## ✅ 20.`), following the item #18 convention (closed
  via `SPRINT.md`, commit `450ea05`, not `BACKLOG.md`). Header status lines updated: "15 de 21
  cerrados" → "16 de 21 cerrados"; "Ciclo SDD activo" → "Ninguno — último cerrado: ítem #20";
  "Última fase cerrada" → item #20; the "## ⬜ Ítems 10, 15, 16, 19 y 21" section note about #20
  having an open cycle removed.
- `BACKLOG.md` — not touched. Item #18's row carries no per-row status mark, so none added for #20
  either. #20/#21 rows left as-is.

## Next Item

No follow-up SDD needed for #20. BACKLOG #21 (Bandeja: datos enriquecidos y contadores de resumen)
remains open and carries the deferred functional work.

---

**Completeness**: All phases concluded. 0 CRITICAL. 6 WARNING dispositioned (3 reconciled in spec
text, 1 recorded as ratification, 2 noted as in-scope deviations). 3 SUGGESTION carried as
follow-ups. #21 deferral explicit.
