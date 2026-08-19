# Proposal: Inbox y promoción (item #7)

## Intent

Item #6 closed without writing `fact.InboxEvent` (deliberately deferred). Today no processing
outcome from the worker is visible to the business, and no `Factura` row is ever created — the
purchase-invoice book has no entry point. This change builds both sides of the ADR 0005 contract:
the Python producer that reports a fact per finished document, and the .NET consumer that decides
whether to promote it into `fact.Factura`, so every processed document becomes visible and
actionable (promoted or pending manual review) instead of silently sitting in worker-private
tables.

## Scope

### In Scope
- Python: `inbox_event_repo.py` (INSERT-only, `fact_worker` role) writing one `InboxEvent` row for
  **every** document `cli_procesamiento.py` finishes — success or failure — in a step separate from
  and after item #6's already-closed transaction (reads committed `Procesamiento` rows; does not
  reopen #6's transaction boundary).
- .NET: `SmartNet.Inbox.Core` (pure promotion decision + 6 indicator flags, DB/HTTP/clock-free,
  `PurityScanTests`) + `SmartNet.Inbox.Infrastructure` (repos, hosted background consumer in
  `SmartNet.Api`) that reads `InboxEvent`, decides promote/no-promote, and writes `Factura` +
  `FacturaExtraccion` transactionally.
- Resolution of the `InboxEvent.Tipo` CHECK discrepancy: `Tipo` stays the single value
  `PROCESAMIENTO_FINALIZADO`; success/failure is read from `Procesamiento.Estado`
  (`COMPLETADO`/`ERROR`), not from a second `Tipo` literal. ADR 0005 prose will be corrected to
  match the as-built schema, not the other way around.
- Business rule: insufficient data to represent a valid factura → no `Factura` row is created at
  all. "Sufficient" is a **structural** check — presence/absence of the fields required to
  construct `Factura` and `FacturaExtraccion` — not a REGLAS.md §1–4 validation pass; weighing
  REGLAS.md business rules against extracted data is out of scope for item #7 (resolved in the
  proposal question round below). No `Estado='DESCARTADA'` placeholder row. The `InboxEvent` is
  marked `EstadoConsumo='DESCARTADO'` with `MotivoDescarte`, and the document surfaces in the
  Angular Inbox as pending manual review.
- Angular SPA: Inbox screen consuming `InboxEvent`/`Factura` state via the API — list of processed
  documents with outcome (promoted / pending review / discarded) and the 6 indicator flags as
  visual cues, plus basic filter (by estado) and sort (by fecha) — included in this same slice, not
  deferred. Read-only otherwise for this item (manual actions on discarded items are item #13).
- Idempotency via `Factura.ProcesamientoId` + `UQ_Factura_Procesamiento` filtered unique index
  (already exists) — re-running promotion is a safe no-op.
- Contract tests (ADR 0019 level 2): .NET reads exactly what Python writes to `InboxEvent.Payload`.

### Out of Scope
- Manual review/incidencia workflow actions (approve, edit, re-trigger) — item #13.
- Extraction-accuracy metrics consuming `FacturaExtraccion` — ADR 0017, later item.
- Núcleo contable / asiento generation — item #8, depends on `Factura` existing but not built here.
- Any schema migration: DDL for `InboxEvent`/`Factura`/`FacturaExtraccion`/permissions already
  exists (items #1/#3) and needs no change.
- Reopening or modifying item #6's transaction, tests, or commit history.

## Capabilities

### New Capabilities
- `inbox-event-publishing`: Python worker reports one InboxEvent fact per finished document
  (success or failure), separate step from item #6's pipeline.
- `factura-promotion`: .NET background service consumes InboxEvent, decides promote/no-promote,
  persists Factura + FacturaExtraccion + indicators, enforces idempotency.
- `inbox-screen`: Angular SPA view listing InboxEvent/Factura outcomes for manual review triage.

### Modified Capabilities
- None (this is greenfield consumption of existing #1/#3/#6 schema and data).

## Approach

1. **Python producer**: new module scans committed `Procesamiento` rows lacking a corresponding
   `InboxEvent` (idempotency guard: a `Procesamiento` row without an `InboxEvent` row), builds
   `Payload` JSON (comprobante data, per-field/source evidence, `AfectacionMixta`, association
   warnings), inserts `InboxEvent(Tipo='PROCESAMIENTO_FINALIZADO')` in its own transaction/commit,
   independent of item #6's already-closed and verified transaction.
2. **.NET consumer**: hosted background service in `SmartNet.Api` polls `InboxEvent` where
   `EstadoConsumo='PENDIENTE'`. For each: `SmartNet.Inbox.Core` decides sufficiency (pure function
   over the payload) → if sufficient, transactionally creates `Factura` (`PENDIENTE_VALIDACION`) +
   `FacturaExtraccion` rows + 6 indicators, sets `EstadoConsumo='PROMOVIDO'`, `FacturaId`; if
   insufficient, sets `EstadoConsumo='DESCARTADO'` + `MotivoDescarte`, creates no `Factura` row.
3. **Angular**: new Inbox route/component reading a new read endpoint exposing `InboxEvent` +
   linked `Factura` (when promoted), grouped by outcome, driving the 6 indicator flags as chips.
4. Both polling loops (Python's "find un-notified `Procesamiento`" scan and .NET's `InboxEvent`
   consumer) run independently, each on a fixed **1-minute** cadence — comfortably inside ADR 0005's
   15-minute visibility budget (resolved in the proposal question round below).

## Affected Areas

| Area | Impact | Description |
|------|--------|--------------|
| `SmartNet/worker/` | New | `inbox_event_repo.py`, call site after #6's pipeline (separate step) |
| `SmartNet/api/SmartNet.Inbox.Core` | New | Promotion decision, indicator computation, purity-scanned |
| `SmartNet/api/SmartNet.Inbox.Infrastructure` | New | Repos, hosted background consumer |
| `SmartNet.Api/Program.cs` | Modified | Register the new hosted background service |
| SPA `src/app/inbox/` (or equivalent) | New | Inbox list screen, signals-based, no state library |
| `docs/adr/0005-*.md` | Modified | Correct `Tipo` value prose to match as-built single-CHECK schema |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Confusing this item's InboxEvent-write step with reopening #6's closed transaction | Medium | Explicit separate step/transaction, no edits to #6's tests or commit |
| `InboxEvent.Tipo` ambiguity causes an agent to invent a second literal, breaking the CHECK | Medium | Resolved explicitly above; spec/design must state Estado-based outcome derivation |
| "Datos suficientes" sufficiency rule treated as REGLAS.md validation instead of a structural presence/absence check | Low-Med | Resolved explicitly in scope above: structural check only, no REGLAS.md §1–4 weighing in item #7 |
| Two independent 1-minute polling loops (Python + .NET) add load or drift under high volume | Low | Cadence fixed at 1 minute for both loops, well inside ADR 0005's 15-min visibility budget; revisit only if volume metrics show contention |
| Angular Inbox scope creep into item #13's manual-action workflow | Medium | Explicitly read-only in this item; actions deferred |

## Rollback Plan

All new code lives in new projects/modules (`SmartNet.Inbox.*`, `inbox_event_repo.py`, new SPA
route) plus one new hosted-service registration line in `Program.cs`. Revert = remove the hosted
service registration (stops consumption) and drop the new projects/route; no schema changes to
roll back since DDL predates this item. `InboxEvent` rows already written remain `PENDIENTE` and
are safely reprocessed once re-enabled.

## Dependencies

- Item #6 (extracción y asociación) — closed, provides `Procesamiento`/`DatosExtraidos`.
- Item #3 (catálogos y satélites) — closed, provides lookup data referenced by indicators.
- Existing DDL from item #1 (`fact.InboxEvent`, `fact.Factura`, `fact.FacturaExtraccion`,
  permission grants) — no migration needed.

## Success Criteria

- [ ] Every document `cli_procesamiento.py` finishes (success or failure) produces exactly one
      `InboxEvent` row.
- [ ] Sufficient-data documents produce a `Factura` in `PENDIENTE_VALIDACION` with correct 6
      indicators and `FacturaExtraccion` rows; re-running promotion is idempotent (no duplicate).
- [ ] Insufficient-data documents produce zero `Factura` rows; `InboxEvent.EstadoConsumo='DESCARTADO'`
      with a `MotivoDescarte`, visible in the Inbox screen as pending manual review.
- [ ] Angular Inbox screen lists all InboxEvent outcomes (promoted/discarded) sourced from the API.
- [ ] `SmartNet.Inbox.Core` passes `PurityScanTests`; contract tests confirm .NET reads exactly what
      Python writes to `Payload`.
- [ ] ADR 0005 text corrected to match the single-`Tipo` as-built schema.

## Proposal question round (resolved)

Four product questions were offered before finalizing this proposal, to surface business rules,
edge cases, and scope boundaries. User answers below now govern spec/design.

1. **Polling cadence** — Python's un-notified-`Procesamiento` scan and .NET's `InboxEvent` consumer
   each run on an independent, fixed **1-minute** cadence. No shared scheduler, no dynamic backoff
   in this item; well inside ADR 0005's 15-minute visibility budget.
2. **`InboxEvent.Payload` JSON shape** — must carry everything the .NET consumer and the Angular
   Inbox screen need without re-querying the worker: comprobante data, per-field evidence
   (`Fuente`), `AfectacionMixta`, and association warnings. **Corrected during design review:**
   `confianza` was named here but no component computes or persists a confidence value —
   `FacturaExtraccion` (ADR 0017, item #6, already closed) has no such field, and emitting one would
   fabricate data (ADR 0017 boundary). Dropped; `Fuente` is the complete per-field evidence for this
   item.
3. **"Datos suficientes para promover" criterion** — a **structural** check: presence/absence of
   the fields required to construct `Factura` and `FacturaExtraccion`. It does **not** weigh
   REGLAS.md §1–4 business/validation rules in this item — that remains explicitly out of scope for
   #7 (contradicts and supersedes the earlier "per REGLAS.md" phrasing and the "light REGLAS.md
   §1–4 check" risk mitigation, both updated above).
4. **Angular Inbox screen scope** — includes basic filter (by `EstadoConsumo`/outcome) and sort (by
   fecha) in this same slice, not deferred to item #13. Still read-only: no manual actions
   (approve/edit/re-trigger) on inbox items.
