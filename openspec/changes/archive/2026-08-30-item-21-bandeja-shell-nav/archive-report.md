# Archive Report: item-21-bandeja-shell-nav (BACKLOG #21 + macOS shell nav)

**Archived**: 2026-08-30
**Change**: item-21-bandeja-shell-nav — Bandeja: datos enriquecidos y contadores de resumen
(BACKLOG #21), delivered together with the macOS sidebar navigation shell (`DESIGN.md`, not a
backlog item — folded in per user decision, noted under #21 in `BACKLOG.md`).
**Status**: CLOSED ✓ (pass-with-warnings — 0 CRITICAL, 3 WARNING reconciled, 2 SUGGESTION: 1 acted on, 1 carried)
**Mode**: hybrid (Engram persistence + OpenSpec filesystem)

## Cycle Summary

Full SDD cycle: exploration → proposal → spec → design → tasks → apply → verify → archive.

Two concerns bundled by user request (scope "C"), delivered as three stacked commits on `main`:

1. **PR1 — macOS sidebar navigation shell.** `ShellLayout` gains a presentational `Sidebar`
   (RouterLink to `/bandeja` and `/configuracion` — the only destinations with a real route today,
   per user decision — one hairline divider, `<div>`-only glyphs, collapse toggle) plus a
   `SidebarService` mirroring `TemaService` (`localStorage` key `fact.sidebar`, tampered value →
   `expandido`). New `spa-shell-nav` capability spec.
2. **PR2 — `GET /api/bandeja` contract widening (.NET + SQL).** `BandejaItem` gains six nullable
   comprobante fields (proveedor display name via a `dbo.Proveedor` read, `TipoComprobante` code,
   `Numero`, `TotalOrig`, `Moneda`, `FechaEmision`). `PaginaBandeja<T>` gains a required `Resumen`:
   a global per-estado aggregate computed as a third resultset in the existing single batch, with
   NO `WHERE` clause (filter- and pagination-independent) over a predicate wider than the default
   list view. No schema script, no new grant.
3. **PR3 — SPA consumption.** `bandeja-item.model.ts` mirror, `InboxService.resumen()`,
   `inbox-list` reworked into the handoff §2 compras table (10 columns, client-side comprobante
   map, "—" for null factura cells), new display-only `inbox-resumen` component (4 cards), wired
   into `inbox-page`.

## Final State — Authority Hierarchy

### 1. Native Review Authority

RDD (gentle-ai receipt-driven development) is **disabled at repo level** (D: is exFAT, no ACL —
see SPRINT.md "Condiciones del entorno"). `reviewGate.delivery: disabled/unmanaged`, kill switch
off. No terminal receipt is demanded or fabricated. Consistent with all prior archives (#12–#20).

### 2. Persisted Tasks Artifact

File: `openspec/changes/archive/2026-08-30-item-21-bandeja-shell-nav/tasks.md` — **32/32 tasks
`[x]` checked** (Phase 1: 10, Phase 2: 10, Phase 3: 12). Archive-time checkbox reconciliation was
required (verify WARNING 1) and was performed in commit `bb1fb59`.

### 3. Explicit Final-State Facts (orchestrator-verified, authoritative)

| Fact | Evidence |
|---|---|
| SPA suite | `npm test` → **379/379** green (`bb1fb59` adds no SPA test) |
| SPA lint / build | `npm run lint` (tsc) clean; `npm run build` clean, every `anyComponentStyle` < 4 kB |
| API — inbox infra | `dotnet test inbox/SmartNet.Inbox.Infrastructure.Tests` → **49/49** green (48 at verify + 1 new D2b OBSOLETO test in `bb1fb59`) |
| API — host | `dotnet test api/SmartNet.Api.Tests` → **164/164** green (real local SQL Server) |
| Regression guards untouched | `paleta.spec.ts`, `contraste.spec.ts`, `app.routes.spec.ts`, `src/styles.css`, `SmartNetBD/schema/**`, `BandejaEndpoints.cs` — none appear in `git diff a93f4c7..HEAD` |
| ADR 0003 | `dbo.Proveedor` join proven under a real `usr_api` login by `SqlBandejaRepositoryTests.ListarAsync_WidenedBatch_RunsAsUsrApi_ProvingProveedorAndAggregateGrants` via `TestDatabaseFixture.ExecuteAsUserAsync`; no `dbo.*` write anywhere |
| ADR 0016 | no new versioned SQL script; the change is an additive read projection |

### 4. Delivery

`size:exception` accepted by the user (bundled ~1960 changed lines exceed the 800-line
`ask-on-risk` budget). Delivered as stacked commits directly on `main` (local, **not pushed**),
the project's established pattern:

| Commit | Slice |
|---|---|
| `cafa478` | refactor(spa): move login outside the app shell (prior-session groundwork this change builds on) |
| `a93f4c7` | feat(spa): macOS sidebar navigation shell (PR1) |
| `a83c5ee` | feat(api): enrich GET /api/bandeja with comprobante fields + estado aggregate (PR2) |
| `84c05e4` | feat(spa): enriched bandeja rows + global summary cards (PR3) |
| `bb1fb59` | test(api): explicit D2b OBSOLETO aggregate scenario + close item-21 verify |

## Spec changes synced to `openspec/specs/`

| Capability | Change | Synced during |
|---|---|---|
| `spa-shell-nav` | **NEW** full capability spec | PR1 (`a93f4c7`) |
| `bandeja` | ADDED: enriched comprobante fields; ADDED: global estado aggregate with the D2b OBSOLETO note + explicit scenario | PR2 (`a83c5ee`) + `bb1fb59` |
| `spa-visual-bandeja` | MODIFIED: inbox-list §2 compras table; ADDED: inbox-page global summary cards; Purpose + Out-of-Scope prose deliberately unfrozen for the #13 query / `inbox.service.ts` (project rule 1) | PR3 (`84c05e4`) |
| `spa-design-tokens` | **no delta** — `--fondo-sidebar` already exists and is already asserted in `contraste.spec.ts`; rationale recorded in the change's `specs/spa-design-tokens/spec.md` marker | — |

## Verify Warnings — disposition

| # | Warning | Disposition |
|---|---|---|
| 1 | `tasks.md` / Engram obs #228 had 0/32 checkboxes ticked | **Reconciled** — all 32 marked `[x]` in `bb1fb59`; this report records completion |
| 2 | apply-progress had no structured per-task TDD Cycle Evidence table | **Accepted** — RED-before-GREEN is evidenced by the stacked commits and the scenario tests; not re-litigated |
| 3 | `SqlBandejaRepository.ListarConConexionAsync` widened to `internal static` for the impersonation test | **Accepted** — pre-existing seam (task 3.7 of #13 used it); no functional risk |

## Suggestions

| # | Suggestion | Disposition |
|---|---|---|
| 1 | Add an explicit bandeja spec scenario for the D2b OBSOLETO asymmetry (was prose-only) | **Done** in `bb1fb59` — spec scenario + `SqlBandejaRepositoryTests` case |
| 2 | No SPA coverage tool configured | **Carried** — project-wide gap, not specific to this change |

## Follow-ups carried

- BACKLOG **#19** (Campos contables editables y resaltado OCR) still owns `glosa`, `tipo de
  cambio`, `base imponible`, `IGV` as row columns — explicitly out of scope here.
- The summary cards are display-only this change; wiring them as filter shortcuts is a future
  enhancement (user decision, recorded in the proposal).
- `tipo de comprobante` has no display-name catalog granted to `usr_api`; the client-side map
  (`01`/`03`/`07`) is the interim solution.
