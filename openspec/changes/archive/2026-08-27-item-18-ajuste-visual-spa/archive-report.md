# Archive Report: Ajuste visual del diseño SPA (BACKLOG #18)

**Date**: 2026-08-27
**Change**: item-18-ajuste-visual-spa
**Mode**: hybrid (OpenSpec + Engram)
**Final Status**: ARCHIVED — verify PASS WITH WARNINGS (0 CRITICAL)
**Branch**: `pr8/item-18-proveedor-picker` (7 commits stacked linearly on `main`; whole chain lands on `main` by fast-forward, no separate PRs)

## Verification Gate (Task Completion + Review Authority)

Per sdd-archive skill §Final-State Authority:

- **Verify Report** (`verify-report.md` / Engram #208, 2026-08-27 16:45): **PASS WITH WARNINGS** — 0 CRITICAL, 2 WARNING, 3 SUGGESTION. `gentle-ai sdd-verify-validate --requirements 38 --scenarios 69` → `{"valid":true,"verdict":"pass"}`. All 38 requirements / 69 scenarios COMPLIANT across the 8 delta specs.
- **Review Gate**: `reviewGate.delivery: disabled/unmanaged`. RDD is disabled at the repo level — structurally, not by preference: the `D:` volume is exFAT (no ACL), so Windows synthesizes `Everyone` as owner and gentle-ai authority validation cannot pass (see `SPRINT.md` §"Condiciones del entorno"). Consistent with every prior archive in this repo (#12–#17). No `gentle-ai review` receipt is required or manufactured.
- **Tasks Artifact** (`tasks.md` / Engram #205): Phases 1–6 and Phase 8 = all `[x]` at implementation time. Phase 7 (verification tasks 7.1–7.5) were `[ ]` in the persisted artifact; reconciled to `[x]` during this archive step — see below.
- **Action Context**: repo-local (`openspec/`), no `workspace-planning` mode.

### Phase 7 stale-checkbox reconciliation (exceptional, per skill §Task Completion Gate)

The orchestrator launched this archive explicitly for "Phase 7 task 7.5 + archive". Phase 7 is the verification phase, not implementation:

- **7.1–7.4** were performed by `sdd-verify` and are proven complete by `verify-report.md` / Engram #208 (SPA 296/296, .NET per-project all green, accent-reuse exception greppable at the `styles.css` ramp, no versioned SQL in the diff, money via `formato.ts` `toFixed(2)`). Marked `[x]` with the evidence source cited inline in `tasks.md`.
- **7.5** ("Update `BACKLOG.md` #18 status") was executed in this archive step. Following the item #17 convention (closed via `SPRINT.md`, commit `e6054ea`, not `BACKLOG.md`): the #18 closing entry was added to `SPRINT.md`. `BACKLOG.md` carries no per-row status mark for #17 either, so none was added for #18; the `## ⬜ Ítems 10, 15, 16 y 18` section in `SPRINT.md` was updated to drop #18.

**Reconciliation reason recorded**: `sdd-apply` marked all implementation phases (1–6, 8) but never re-entered the tasks artifact to check the Phase 7 verification boxes after `sdd-verify` ran. No implementation task was ever left unchecked. Archive proceeds.

## Source Artifacts

| Artifact | Engram ID | OpenSpec path | Status |
|----------|-----------|---------------|--------|
| exploration | — | `exploration.md` | Read |
| proposal | #202 | `proposal.md` | Read, complete |
| spec (delta) | #203 (rev 2) | `specs/` (8 delta specs) | Read, complete |
| design | #204 | `design.md` | Read, complete (D1–D6) |
| tasks | #205 (rev 9) | `tasks.md` | Read; Phases 1–6, 8 complete; Phase 7 reconciled here |
| apply-progress | #206 (rev 8) | `apply-progress.md` | Read (intermediate snapshot) |
| verify-report | #208 (rev 2) | `verify-report.md` | Read, PASS WITH WARNINGS |
| archive-report | (this file) | `archive-report.md` | Persisted to Engram + filesystem |

Supporting investigations: `item-18/proveedor-picker-investigation` (Engram #207).

## Stacked commit chain (7 commits, linear on `main`)

| # | Hash | Slice | One-line |
|---|------|-------|----------|
| 1 | `2ac0ac7` | PR1 | Two-tier token layer in `styles.css` + WCAG palette guard that reads `styles.css` via `node:fs` (`paleta.ts` / `paleta.spec.ts` / `contraste.spec.ts`) — `size:exception` ~480 lines |
| 2 | `505fdb8` | PR2 | App shell header (`<select>` theme control) + `login-page` recomposition to the handoff card |
| 3 | `93ec9a7` | PR3 | `detalle-page` restructure: page header + back + top-right actions, indicator banners hoisted above the split into new `indicadores-factura`, 42/58 static split, `asiento-lineas` tabular grid + cuadre pill |
| 4 | `8720f63` | PR4 | `factura-form` 2-col field grid, zero-backend editable fields (`monto`/`moneda`/`fechaEmision`/`proveedorCodigo`), per-field `.campo--resaltado`, derived mes/día, read-only base/IGV/TC rows |
| 5 | `aa91cda` | PR5 | .NET PATCH delta (4 layers) making `tipoComprobante`/`numero` editable + pure `ValidacionDeCorreccion` guard + `ResultadoComando.CorreccionInvalida` → 422 + new `SmartNet.Contable.Core/CodigoComprobante` canonical set + SPA binding — `size:exception` ~431 lines |
| 6 | `37a4ece` | PR6 | Additive `AsientoRespuesta.BasePEN`/`IgvPEN` read-only projection + SPA base/IGV row binding |
| 7 | `312d2b6` | PR8 | Functional proveedor picker: `GET /api/catalogos/proveedores` read-only over `dbo.Proveedor` (P00000 excluded, no versioned SQL/grant, no `dbo.*` write, no `fact.*`), `catalogos` data-access `ProveedorService`, `picker-proveedor` `<dialog>` component, `detalle-page` wiring through `borradorFactura` — `size:exception` ~1392 ins / 3 del (one commit, overriding the PR8a/8b/8c sub-split) |

`main` has not moved since the chain was cut. The orchestrator fast-forwards `main` to `pr8/item-18-proveedor-picker`.

## Delivery / Review Workload

Delivery strategy: `ask-on-risk`, stacked chain (PR1→PR2→PR3→PR4→PR5→PR6→PR8, each targeting its predecessor). The user decided the whole chain lands on `main` by fast-forward, not as separate PRs.

**5 `size:exception` PRs accepted by the project owner** — PR1, PR3, PR4, PR5, PR8. Each is a screen slice plus its TDD specs running ~430–580 authored lines (PR8 larger with planning artifacts), and each is only coherently reviewable whole: the palette guard is what proves the token layer; a screen restructure and its DOM specs are one reviewable unit; the picker slice's endpoint + service + dialog + wiring form one contract. PR2 and PR6 stayed under the 400-line budget.

## Ratified scope expansions (approved by the user mid-flight)

The change started as "pure visual conformance to the handoff" and grew three times, each with explicit user approval:

1. **`factura-form` editable fields** — the form was originally described as pure-visual; the user added two-way binding for `monto`, `moneda`, `fechaEmision`, `proveedorCodigo` (all already in the GET projection + PATCH contract, SPA-only work).
2. **`tipoComprobante` / `numero` PATCH-editability** — a .NET delta (PR5): these were in the GET projection but not the PATCH contract. Added additively to `CorreccionFacturaRequest` / `CorreccionFactura` / `ServicioDeFacturas.PatchAsync` with a pure domain guard. No versioned SQL (columns and `UPDATE ON fact.Factura` grant already exist).
3. **Functional proveedor picker + `GET /api/catalogos/proveedores`** — a whole new slice (PR8): `factura-form` already emitted an unhandled `buscarProveedor` output; the user asked for a working modal picker backed by a new read-only catalog search endpoint over `dbo.Proveedor`.

Deferred by ratified decision (not silently dropped): `base imponible` / `IGV` / `TC compra` stay read-only (editability needs a REGLAS.md review); `glosa` is absent (no column, needs versioned SQL); "TC compra" display uses the `(venta)` label because ADR 0018 pt.1 makes venta the operative rate.

## Specs Synced to `openspec/specs/`

| Domain | Action | Details |
|--------|--------|---------|
| `spa-design-tokens` | Updated | +6 ADDED (accent family fill/text tokens, four-level surface hierarchy, elevation shadow scale, radius 8/12/16/pill, Segoe-first integer type scale, translucent hairline borders); 2 MODIFIED (WCAG AA per-pair now names the accent fill / accent-text / new-surface pairs and the `contraste.spec.ts` assertion; two-tier alert emphasis now carries the ratified accent-reuse exception). Base requirements not touched by the delta preserved. |
| `spa-theme-toggle` | Updated | +1 ADDED (theme control stays a native `<select>`; sun/moon toggle and sidebar redesign out of scope — user decision 5). |
| `spa-visual-login` | Updated | +3 ADDED (handoff card composition, placeholder-labeled inputs, full-width accent submit button); 2 MODIFIED (login error now inline not `.banner--error`; token consumption now includes radius/elevation). |
| `spa-visual-detalle-validacion` | Updated | +5 ADDED (page header with back/title/estado pill/top-right actions; indicator banners above the split; static 42/58 split; corte-contable adjacent to asiento; `asiento-lineas` tabular grid + total row + cuadre pill); 3 MODIFIED (blocking indicators are now banners above the split incl. TC-faltante; informational indicators now per-field `.campo--resaltado`; CSS budget requirement adds `historial-correccion`). |
| `pantalla-detalle-validacion` | Updated | +3 ADDED ("Validar" hard-blocked on P00000 or duplicate, no ack bypass; `factura-form` header field set with editable/read-only/derived split; dedicated TC-faltante indicator); 2 MODIFIED (side-by-side layout references the full field set + 42% static; duplicate/afectación indicators require per-field OCR highlight). |
| `api-facturas` | Updated | +1 ADDED (`CorreccionFacturaRequest` additively accepts `tipoComprobante` / `numero`; blank `numero` or unknown `tipoComprobante` → 422; omitting them is a no-op). |
| `api-catalogos-proveedores` | Created | New capability (no prior spec). Full spec copied: authenticated `GET /api/catalogos/proveedores`, read-only partition-respecting `dbo.Proveedor` access, pagination, empty/short/no-match handling, P00000 exclusion, contract-test coverage. |
| `spa-picker-proveedor` | Created | New capability (no prior spec). Full spec copied: `ProveedorService` data-access (signals, debounced `firstValueFrom`), modal picker dialog (keyboard nav / focus trap / aria, no new token), opened from `factura-form` `buscarProveedor` → selection through `borradorFactura` / `onCambiosFactura`, test coverage. |

These 8 REVISE the archived `2026-08-24-diseno-visual-spa-item-12` specs (5 of them) and the `2026-08-23-api-facturas-asientos` `api-facturas` spec (1), and ADD 2 new capabilities.

## Test Evidence (per `verify-report.md` / Engram #208, run by the verifier)

- SPA `npx ng test --no-watch`: **34 files / 296 passed**, 0 fail (was 282 at PR6).
- SPA `npx ng build --configuration production`: clean, NO `anyComponentStyle` budget warning (`picker-proveedor.css` ~1.4 kB).
- SPA `npm run lint`: exit 0 (NOTE: this is `tsc --noEmit` only, not ESLint — carried forward as SUGGESTION 2).
- `dotnet build SmartNet.sln`: 0 warn / 0 err.
- .NET per-project (authoritative): Facturacion.Core 147, Api.Tests 163 (incl. `CatalogoEndpointsTests`, PR5 `FacturaEndpointsTests`, PR6 `AsientoEndpointsTests`), Catalogos.Infrastructure 66 (incl. `SqlProveedorRepository.BuscarAsync` ×9), Catalogos.Core 32, Facturacion.Infrastructure 53, plus Contable.Core 41, Sugerencia.Core 27, TiposCambio.Core 20, Inbox.Core 49, Auth.Core 33 — all PASS.
- `dotnet test SmartNet.sln` (solution-wide parallel): 32 failures across 8 integration assemblies. NOT item-18 regressions — SQL Server provisioning contention under parallel `fact_test_*` creation (proof: `TestBootstrapHarnessTests.CreateTestDatabase` and `Facturacion.Infrastructure` 0/53 in the solution run, 53/53 alone). Same pre-existing pattern documented on items #3/#4/#12/#13/#17. → WARNING 1.

## Design Conformance

D1–D6 all followed. D2 WCAG guard genuinely reads `styles.css` via `node:fs` (goes RED on a bad token). Dark `--accento-texto` = `#409cff` was ratified over the design's `#0a84ff` (fails AA 3.82:1 on `#2c2c2e`), documented at `styles.css:168-172`. The 6-vs-4 .NET touch-point deviation in PR5 (new `ResultadoComando.CorreccionInvalida` → 422; new `SmartNet.Contable.Core/CodigoComprobante`) is sound and documented in commit `aa91cda` + `apply-progress.md`.

## Final State — Warnings and Follow-ups

Per verify-report §Final-State Authority; these do NOT block archive.

**WARNING 1 — solution-wide `dotnet test` parallel contention.** Not a code defect. Recommendation: run integration assemblies serially in CI, or document the per-project invocation as the canonical one (same as items #3/#4/#12/#13/#17). Pre-existing debt, not introduced by #18.

**WARNING 2 — task 7.5 (`SPRINT.md`/`BACKLOG.md` #18 status) not done at verification time.** Resolved in this archive step: `SPRINT.md` #18 closing entry added; `BACKLOG.md` untouched per the item #17 convention.

**SUGGESTION 1 — `LIKE` wildcards in the picker `q` are not escaped.** No security impact (parameterised query), but it is the one open item lacking an in-repo comment. Recorded in the archived spec's Out-of-Scope section and in `openspec/specs/api-catalogos-proveedores/spec.md`; carried forward as a follow-up (escape the wildcards or add an in-code note).

**SUGGESTION 2 — `npm run lint` is typecheck-only** (`tsc --noEmit`, not ESLint). Follow-up: wire real linting.

**SUGGESTION 3 — `--accento-suave` literal sits outside the `--azul-*` ramp.** By design D3, acceptable; noted for a future reviewer.

### Pre-existing known open questions carried forward (deliberately out of scope of #18)

- **`PosibleDuplicado` goes stale after an identity-triple edit.** Editing `tipoComprobante` / `numero` changes the duplicate identity (`RucProveedor` + `TipoComprobante` + `Numero`), but `PosibleDuplicado` is a stored column written at ingestion and is not recomputed on PATCH — the banner stays stale until re-ingestion. Recomputing on PATCH is a domain-rule change, out of #18 unless ratified (design Open Question 2).
- **Invoice-wide, not per-field, OCR highlight.** The server exposes only `TieneCamposNoExtraidos: boolean`, so `.campo--resaltado` is applied at the coarsest correct granularity (all OCR-derived fields highlight together when the flag is true). 2 requirements carry a PARTIAL note for this documented deliberate limitation; scenarios pass at the exposed granularity.
- **No nonclustered index on `dbo.Proveedor(proveedor)`** — a `dbo.*` external-catalog object per ADR 0003, OUT OF SCOPE as a flagged decision. `LIKE` over ~6600 rows is acceptable.

## Conventions / ADR

No `dbo.*` writes in new .NET prod code (grep-verified — only test fixtures reach `dbo.Proveedor` via raw INSERT). No `fact.*` access from the `catalogos` slice. No versioned SQL, no new grant (`git diff` touches no `*.sql`; `usr_api` already has `SELECT ON dbo.Proveedor`). No EF/Alembic migrations. Accounting identifiers Spanish, no accents in identifiers. Signals-only state (ADR 0009). Money 2 decimals via `formato.ts`, never 3 (CONVENTIONS.md).

## Archive Folder Movement

**Source**: `openspec/changes/item-18-ajuste-visual-spa/`
**Destination**: `openspec/changes/archive/2026-08-27-item-18-ajuste-visual-spa/`

Contents (all full, not summarized): `exploration.md`, `proposal.md`, `design.md`, `tasks.md` (Phase 7 reconciled to `[x]`), `apply-progress.md`, `verify-report.md`, `archive-report.md` (this file), `specs/` (8 delta specs).

## SDD Cycle Complete

Explore → Proposal (Engram #202) → Spec (Engram #203, 8 delta specs) → Design (Engram #204, D1–D6) → Tasks (Engram #205) → Apply (Engram #206, Phases 1–6 + 8, Strict TDD RED→GREEN, 7 commits) → Verify (Engram #208, PASS WITH WARNINGS) → Archive (this report, Engram + filesystem).

**Next**: none — BACKLOG #18 is complete. Remaining open backlog items: #10 (Notas de crédito), #15 (Publicación a Drive), #16 (Publicación a Sheets).
