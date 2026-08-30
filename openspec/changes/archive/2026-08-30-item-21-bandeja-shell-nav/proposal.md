# Proposal: Bandeja shell navigation + enriched bandeja data (BACKLOG #21)

## Intent

The running SPA has drifted from the ratified `DESIGN.md` macOS brief: `ShellLayout`
renders only a header, with no sidebar navigation. Separately, BACKLOG #21 requires the
bandeja to become the operational worklist from handoff §2 — rows today lack proveedor
name, comprobante type, número, monto/moneda, and fecha de emisión, and the dashboard has
no at-a-glance counters. This change restores the navigation shell and delivers the #21
data so a user can answer "what must I attend to today?" without opening each row.

## Scope

### In Scope
- (a) macOS sidebar in `ShellLayout`: two nav destinations that have real routes today —
  `Bandeja` (primary group) and, after one hairline divider, `Configuración` (utility group).
  Expanded by default, collapse toggle (216px ⇄ 60px), state persisted per-viewer in
  `localStorage` (key `fact.sidebar`, same pattern as `TemaService`). Inbox + gear glyphs
  hand-built from `<div>` per DESIGN.md.
- (b) `GET /api/bandeja` contract widening (.NET + versioned SQL): enriched columns
  (proveedor display name via `dbo.Proveedor` join, `TipoComprobante` code, `Numero`,
  `TotalOrig`, `Moneda`, `FechaEmision`) plus a per-estado aggregate over a predicate WIDER
  than the list's default `FiltroWhere`.
- (c) SPA: rework the `inbox-list` row into the handoff §2 compras table (proveedor, tipo,
  número, monto+moneda tabular, fecha emisión, derived Estado chip); `origen: 'INCIDENCIA'`
  rows show "—" for factura-only cells. Four global summary cards
  (Pendientes / Validadas / Con error / Alertas), display-only.

### Out of Scope
- New routes/nav entries for unbuilt screens (Registro de compra, Proveedores, Plan
  contable, Sincronización) — added when those screens exist.
- Summary cards as filter shortcuts (display-only this change).
- `base imponible` / `IGV` / `glosa` / `tipo de cambio` columns — stay #19 / future.
- Existing inline `<svg>` in `.alerta` / `.banner` — pre-existing debt, untouched.
- Any change to reprocesar 5-min window, filter semantics, pagination, auth, or the
  per-indicator `chipsDe()` list column.
- Client-side comprobante display-name map (01→Factura, 03→Boleta, 07→Nota de crédito):
  proposed here, `sdd-design` to confirm; API keeps returning the code.

## Capabilities

### New Capabilities
- `spa-shell-nav`: macOS sidebar navigation shell in `ShellLayout` — nav groups, hairline
  divider, collapse toggle, `localStorage` persistence, `<div>` glyphs, "text on sidebar"
  contrast. Scope limited to destinations with existing routes.

### Modified Capabilities
- `bandeja`: enriched row fields (proveedor name, comprobante code, número, monto, moneda,
  fecha emisión) added to the response; NEW per-estado aggregate (`resumen`) counted over a
  wider predicate than the default view so "Validadas" is not structurally 0. The aggregate
  buckets are mutually exclusive with the same first-match precedence as the derived Estado
  chip (DESCARTADO → errores>0 → indicadores&&(genérico||duplicado) → PROMOVIDO → PENDIENTE)
  and partition the full set.
- `spa-visual-bandeja`: "Out of Scope" currently defers summary cards + rich columns to #21
  and freezes the #13 query + `inbox.service.ts` + `chipsDe()`. #21 deliberately unfreezes
  the query and service and moves those items in-scope. Both the freeze Requirement and the
  Out-of-Scope list MUST be updated deliberately (project rule 1 — no silent divergence).
- `spa-design-tokens`: delta ONLY IF a new "text on sidebar" token is required — must be
  added to the `contraste.spec.ts` array and pass AA in both themes; `paleta.spec.ts`
  forbids new hues / un-aliased literals. `--fondo-sidebar` already exists.

## Approach

Deliver as one SDD change in three chained PR slices, each with autonomous scope,
verification, and rollback:

1. **PR1 — shell nav (a)**: `ShellLayout` template/CSS + a small sidebar-state service
   (`localStorage`, mirrors `TemaService`). New `spa-shell-nav` spec + `shell-layout.spec.ts`
   / `app.routes.spec.ts` coverage. No API contact. Watch the 4kB `anyComponentStyle` budget.
2. **PR2 — API + SQL contract widening (b)**: extend the `SqlBandejaRepository` multi-resultset
   batch with the `dbo.Proveedor` join and the wider aggregate SELECT; extend `BandejaItem` /
   `PaginaBandeja<T>` records. Update the `bandeja` spec. Prove the `dbo.Proveedor` join under
   a real `usr_api` login (`SqlBandejaRepositoryTests` / `TestDatabaseFixture.ExecuteAsUserAsync`).
3. **PR3 — SPA enriched columns + summary cards (c)**, built on PR2: rework `inbox-list` row,
   add the four cards to `inbox-page`, extend the `.ts` mirror model + `inbox.service.ts`,
   client-side comprobante map. Update `spa-visual-bandeja`. Run the `integration-spa-api`
   harness for the contract change.

### Envelope shape — OPEN, lean toward option A, `sdd-design` decides
- **A. `PaginaBandeja<T>.resumen` sibling field** on the existing response. Pros: one round
  trip, one cache entry, the SPA already fetches this envelope on every filter change; the
  aggregate naturally shares the same resultset batch. Cons: the field is filter-independent
  yet rides a filter-scoped envelope — slightly surprising; recomputed on every page change.
- **B. separate `GET /api/bandeja/resumen`**. Pros: clean separation, cacheable independently,
  not recomputed on pagination. Cons: second endpoint, second spec surface, second auth path,
  extra client call to coordinate; risk of the two views showing inconsistent totals mid-refresh.
- Lean **A** — the batch already exists and the cards must stay consistent with the list.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `src/app/shared/shell-layout/shell-layout.{ts,html,css}` | Modified | Sidebar nav, groups, divider, collapse toggle |
| `src/app/shared/shell-layout/` (new sidebar-state service) | New | `localStorage` persistence, mirrors `TemaService` |
| `src/styles.css` | Possibly Modified | "text on sidebar" token only if AA needs it |
| `SmartNetApi` `BandejaEndpoints.cs` / `IBandejaRepository.cs` / `SqlBandejaRepository.cs` | Modified | Enriched columns, `dbo.Proveedor` join, wider aggregate SELECT, `resumen` on envelope |
| versioned SQL (`fact` schema scripts) | Modified | No new grants expected — `usr_api` already has `SELECT` on `dbo.Proveedor` and `fact.ProcesamientoError` |
| `src/app/inbox/models/bandeja-item.model.ts` | Modified | Mirror new `.cs` fields + `resumen` |
| `src/app/inbox/data/inbox.service.ts` | Modified | Unfrozen; carry new fields + resumen |
| `src/app/inbox/ui/inbox-list/*` | Modified | Row reworked into §2 compras table |
| `src/app/inbox/feature/inbox-page/*` | Modified | Four summary cards |
| `openspec/specs/{bandeja,spa-visual-bandeja,spa-design-tokens}/spec.md` | Modified (delta) | See Capabilities |
| `openspec/specs/spa-shell-nav/spec.md` | New | Navigation shell capability |
| `BACKLOG.md` | Modified | #21 checkbox; record the shell-nav decision (see Dependencies) |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| ADR 0003 partition: `dbo.Proveedor` join fails under real `usr_api` (re-running `008` isolated re-applies cross-DENYs) | Med | `SqlBandejaRepositoryTests` impersonates `usr_api` via `ExecuteAsUserAsync`; run before merge of PR2 |
| Contract drift `BandejaItem` / `PaginaBandeja<T>` .cs ↔ .ts discriminated union | Med | `integration-spa-api` harness on PR3; keep field names 1:1 |
| "Validadas" structurally 0 if aggregate reuses the default list predicate | High | Aggregate MUST run over its own wider predicate; spec scenario asserts a promoted row is counted |
| `spa-visual-bandeja` freeze broken silently (project rule 1) | Med | Delta spec updates both the freeze Requirement and Out-of-Scope in the same change |
| CSS 4kB `anyComponentStyle` budget on full sidebar + `<div>` glyphs | Med | Layout-only CSS, no literals; measure in PR1; `paleta`/`contraste` specs stay green |
| Review budget: bundled work far exceeds the 800-line ask-on-risk budget | High | Mandatory 3 chained PR slices; forecast revisited after `sdd-tasks` |
| `integration-spa-api` harness needs local SQL Server | Med | If unavailable, report BLOCKED per harness doctrine — do not fabricate PASS |
| DESIGN.md "no SVG" vs existing inline `<svg>` | Low | New glyphs use `<div>`; existing `<svg>` explicitly out of scope as noted debt |

## Rollback Plan

Each slice is an independent revert. PR1 revert restores the header-only `ShellLayout` with
no schema or contract impact. PR2 revert restores the pre-#21 `bandeja` response shape;
because PR3 depends on PR2, roll back PR3 first (SPA falls back to the minimal row and no
cards), then PR2. No data migration, no destructive SQL — schema changes are additive
projections/queries only, so reverting the SQL script leaves no orphaned state.

## Dependencies

- Builds on the uncommitted `ShellLayout` refactor currently on `main` (bare `<router-outlet>`
  in `app.ts`).
- **BACKLOG decision (recommend, do not decide)**: #21 (BACKLOG.md line 41) covers the
  enriched data + counters only; the sidebar shell is NOT a backlog item. Recommend folding
  shell-nav into this change's scope explicitly and adding a one-line note under #21 in
  BACKLOG.md rather than opening a separate item — the two are delivered together and the
  shell is a small, ratified-by-DESIGN.md gap. `sdd-spec` / user to confirm.
- No new DB grants anticipated; confirm during `sdd-design`.

## Success Criteria

- [ ] `ShellLayout` shows a macOS sidebar with `Bandeja` and `Configuración`, a hairline
      divider, a working collapse toggle, and state that survives reload per viewer.
- [ ] `GET /api/bandeja` returns proveedor display name, comprobante code, número, monto,
      moneda, and fecha de emisión per row, plus a `resumen` aggregate.
- [ ] The four summary cards show GLOBAL totals independent of active filters/pagination,
      partition the set (sum = total), and use the row Estado chip's first-match precedence.
- [ ] `inbox-list` row renders the handoff §2 compras columns; `INCIDENCIA` rows show "—"
      for factura-only cells.
- [ ] `dbo.Proveedor` join verified under a real `usr_api` login.
- [ ] `paleta.spec.ts`, `contraste.spec.ts`, `shell-layout.spec.ts`, `app.routes.spec.ts`,
      `inbox.service.spec.ts`, `inbox-list.spec.ts`, `inbox-page.spec.ts` green;
      `anyComponentStyle` within 4kB.
- [ ] `bandeja`, `spa-visual-bandeja` (freeze + Out-of-Scope), and `spa-shell-nav` specs
      updated; `spa-design-tokens` updated iff a new token was needed.
- [ ] Delivered as 3 chained PR slices within the agreed review budget.
