# Proposal: Outbox y mensajería (BACKLOG #14)

## Intent

`fact.OutboxEvent`/`CommandQueue`/`InboxEvent` exist since item #1 and ADR 0004 defines a
five-event catalog, but only 2 of 5 events are emitted (`FACTURA_VALIDADA`,
`DOCUMENTACION_ACTUALIZADA`) and no consumer reads the outbox at all. `POST /reabrir` and
`/anular` (item #11) silently produce no downstream fact. Item #15 (Drive) and #16 (Sheets) cannot
be built or tested end-to-end — correction/anulación would have no event to react to, and there is
no batch-claim/obsolescence infrastructure for any future consumer to plug into. Item #14 closes
both gaps now, while the outbox is the explicit focus, rather than discovering the hole mid-#15/#16.

Design (D2, D1) surfaced two further gaps the project owner decided to close inside #14 rather than
defer as technical debt: (a) the 2 already-emitted events (`FACTURA_VALIDADA`,
`DOCUMENTACION_ACTUALIZADA`) send bare scalars, violating ADR 0004's self-sufficient-payload rule —
all 5 catalog events must land with a consistent full-snapshot envelope before #15/#16 build
handlers against them; (b) `ConfirmarAfectacionAsync` (`ServicioDeFacturas.cs:384`) can mutate
`AfectacionMixta` on an already-VALIDADA factura and was missing as a `FACTURA_CORREGIDA` emission
point — there are 4 emission sites, not 3.

A third gap surfaced while designing D8 (design Open Question 4) and was resolved by the project
owner: **nothing in the current codebase ever sets `fact.Factura.Estado = 'VALIDADA'`.**
`ValidarInternoAsync` confirms the asiento but never writes the factura's own `Estado`; the only
`UPDATE fact.Factura` statements are PATCH-driven and the `AfectacionMixta`-only update in
`ConfirmarAfectacionAsync`. Consequence: the `Estado == VALIDADA` guards on `DOCUMENTACION_ACTUALIZADA`
(already existing) and on the new `FACTURA_CORREGIDA` are dead code in production — always false —
and only pass their unit tests because those tests set `Estado` by hand. Two resolutions were on the
table: (a) close the #11 gap inside #14 by making the validar transaction actually write
`Estado = VALIDADA`, or (b) change the outbox predicate itself to something already true today (e.g.
"asiento tiene `NumeroAsiento`"), leaving the factura-state gap unaddressed. The owner chose **(a)**:
fix the root cause — `Factura.Estado` must genuinely reach `VALIDADA` — rather than route the outbox
predicate around a known-wrong domain state. This is contable-correct and closes a gap left open by
item #11, at the cost of a small expansion of #14's scope.

## Scope

### In Scope
- Emit all 3 remaining catalog events across their **4** emission points, each via the existing
  `IUnidadDeTrabajo.EmitirOutboxAsync` port, inside the same transaction as the domain write
  (matching the `FACTURA_VALIDADA` pattern at `ServicioDeFacturas.cs:110`):
  - `ASIENTO_CORREGIDO` in `ServicioDeFacturas.ValidarInternoAsync` — reconfirmation of a factura
    whose `NumeroAsiento` already exists (the only route back to BORRADOR is `ReabrirAsync`, so a
    pre-existing `NumeroAsiento` at reconfirm *is* "reconfirmed after reopening"; ADR 0004's
    catalog wording matches the reconfirm commit, not the reopen).
  - `ASIENTO_ANULADO` in `ServicioDeAsientos.AnularAsync`.
  - `FACTURA_CORREGIDA` in **both** `ServicioDeFacturas.PatchAsync` and
    `ServicioDeFacturas.ConfirmarAfectacionAsync` (`:384`) — any accepted update to an
    already-validated factura, including an `AfectacionMixta` change via confirmation, triggers
    `FACTURA_CORREGIDA` once per correction transaction.
- Self-sufficient payloads for **all 5** catalog events (full state snapshot, never a delta) per
  ADR 0004: correct the 2 already-emitted events (`FACTURA_VALIDADA`, `DOCUMENTACION_ACTUALIZADA`),
  which today send bare scalars (`numeroAsiento`, `NombreArchivo`, `adjuntoId.ToString()`), to the
  same envelope shape as the 3 new events. Fixed now, not deferred, so #15/#16 build handlers
  against one consistent contract instead of two.
- Python worker: generic batch-claim repository for `OutboxEvent`/`CommandQueue` using SQL Server
  `READPAST`, isolated behind an interface (`IReclamoDeLote` or equivalent) so the SQL-Server-only
  dependency (ADR 0002's one declared exception) never leaks into destination-agnostic dispatch
  logic. Claim lease: **5 minutes** (confirmed by the project owner), within ADR 0005's 15-minute
  visibility budget.
- **Close the #11 `Factura.Estado` transition gap**: make `ValidarInternoAsync` actually write
  `fact.Factura.Estado = 'VALIDADA'` in the same transaction as the asiento confirmation, via a
  new state-only port member on `IUnidadDeTrabajo` (`ValidarInternoAsync` holds no factura ETag, so
  it cannot reuse the PATCH-style save path). This makes the existing `DOCUMENTACION_ACTUALIZADA`
  guard and the new `FACTURA_CORREGIDA` guard (both `Estado == VALIDADA`) real, reachable code paths
  in production instead of dead branches that only pass because tests set the state by hand.
- Obsolescence guard: compare incoming `Secuencia` against `OutboxEventIntegracion`'s
  per-destination progress before applying; mark superseded claims `OBSOLETO` — a distinct,
  non-error terminal code path (ADR 0010), never routed through item #17's
  `TRANSITORIO/DIFERIBLE/PERMANENTE` classification.
- Boundary contract tests (ADR 0019 nivel 2) for `OutboxEvent`/`CommandQueue`: .NET writes/Python
  reads and vice versa, against the real applied schema, verifying the `usr_api`/`usr_worker`
  permission matrix (ADR 0003).
- **Close the `abrir → descartar → validar` gap (design Open Question 5, resolved)**: with
  `MarcarFacturaValidadaAsync` (D10) making `Estado` transitions observable, `POST /validar` on a
  factura that is already `DESCARTADA` must return **409** and roll back the asiento confirmation —
  `NoTransicionable` is treated as terminal, mirroring `DescartarAsync`'s own `Estado == VALIDADA`
  guard. This is the documented intent of ADR 0008 ("VALIDADA no puede descartarse", read together
  with "DESCARTADA no puede validarse") finally taking effect on an existing endpoint, not a new
  rule — but it is a visible behavior change on `POST /validar` and must be called out in the item's
  release note.

### Out of Scope
- Item #15 (Google Drive) and #16 (Google Sheets) — no destination handler is implemented; the
  consumer built here is destination-agnostic plumbing with no visible effect on its own.
- Any schema change — `fact.OutboxEvent`, `OutboxEventIntegracion`, `CommandQueue`, `InboxEvent`,
  `SeqOutbox`, and the `OBSOLETO` state already exist (item #1); this item uses them as-is.
- Item #17's retry-classification (`TRANSITORIO`/`DIFERIBLE`/`PERMANENTE`) — only `OBSOLETO`
  marking is in scope here.
- SPA changes — declared "sin efecto visible" in BACKLOG.md.

## Capabilities

### New Capabilities
- `outbox-consumo`: Python batch-claim (`READPAST`) + obsolescence-guard consumer loop over
  `OutboxEvent`/`CommandQueue`, destination-agnostic.

### Modified Capabilities
- `inbox-event-publishing`: none — this capability is Python→fact direction (InboxEvent), not
  touched by outbox emission/consumption; listed only to confirm no overlap.
- (Implicit, no existing spec file) producer-side emission of `ASIENTO_CORREGIDO`,
  `ASIENTO_ANULADO`, `FACTURA_CORREGIDA` — new requirements under `outbox-consumo` or a sibling
  `outbox-emision` capability; sdd-spec decides file split.

## Approach

1. **Producer (.NET)**: add `EmitirOutboxAsync` calls at the 4 confirmed points —
   `ServicioDeFacturas.ValidarInternoAsync` (reconfirm → `ASIENTO_CORREGIDO`),
   `ServicioDeAsientos.AnularAsync` (→ `ASIENTO_ANULADO`), and `ServicioDeFacturas.PatchAsync` +
   `ConfirmarAfectacionAsync` (both → `FACTURA_CORREGIDA`) — reusing the existing port, same
   transaction, same monotonic `Secuencia` via `SeqOutbox`. A pure Core payload serializer
   (`PayloadOutbox`) builds the full-snapshot envelope for all 5 events, including retrofitting the
   2 pre-existing emission call sites onto the same shape. Mechanical, no new abstractions.
2. **Consumer (Python)**: new module(s) in `SmartNet/worker/src/smartnet_worker/`, following the
   existing repo-style pattern (`inbox_event_repo.py`, `estado_integracion.py`). `READPAST` claim
   lives behind an interface so a future non-SQL-Server engine only needs a new implementation, not
   a dispatcher rewrite. Obsolescence check happens before any handler dispatch, comparing
   `Secuencia` to `OutboxEventIntegracion` state; on stale, write `OBSOLETO` and stop — no
   handler call, no error/alert.
3. **Contract tests**: establish ADR 0019 nivel 2 baseline for these tables since nothing exercises
   it today.

## Affected Areas

| Area | Impact | Description |
|------|--------|--------------|
| `SmartNet/facturacion/SmartNet.Facturacion.Core/ServicioDeAsientos.cs` | Modified | Emit `ASIENTO_ANULADO` in `AnularAsync` |
| `SmartNet/facturacion/SmartNet.Facturacion.Core/ServicioDeFacturas.cs` | Modified | Emit `ASIENTO_CORREGIDO` (`ValidarInternoAsync` reconfirm) and `FACTURA_CORREGIDA` (`PatchAsync` + `ConfirmarAfectacionAsync`); rebuild `FACTURA_VALIDADA`/`DOCUMENTACION_ACTUALIZADA` payloads onto the shared envelope |
| `SmartNet/facturacion/SmartNet.Facturacion.Core/PayloadOutbox.cs` | New | Pure full-snapshot payload serializer, shared by all 5 events |
| `IUnidadDeTrabajo` (Core interface) + `SqlUnidadDeTrabajo.cs` (Infrastructure) | Modified | New state-only port member so `ValidarInternoAsync` can write `fact.Factura.Estado = 'VALIDADA'` without an ETag (closes #11 gap) |
| `SmartNet/worker/src/smartnet_worker/` | New | Batch-claim repo (`READPAST` behind interface, 5-min lease), obsolescence guard, dispatcher |
| `SmartNet/db/schema/` | None | Schema already complete (item #1) |
| Contract tests (.NET + pytest) | New | Bidirectional boundary tests for Outbox/CommandQueue/permission matrix |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Regressing item #11's closed contract tests when touching `ServicioDeAsientos.cs`/`ServicioDeFacturas.cs` | Low | Additive calls only, no signature/behavior change to existing paths; run full `#11`/`#7` test suites before/after |
| `OBSOLETO` miscounted as error/alert | Med | Distinct code path from #17's classification, asserted by a dedicated test |
| `READPAST` leaking into destination-agnostic dispatch code | Med | Interface boundary enforced in design phase; dispatcher never imports the SQL-specific implementation directly |
| Payload not self-sufficient for one of the 5 events | Low | Explicit review against ADR 0004 payload rule per event in spec phase; golden fixture per event type |
| Retrofitting `FACTURA_VALIDADA`/`DOCUMENTACION_ACTUALIZADA` payloads breaks a consumer already reading the old scalar shape | Low | No consumer exists yet (item #14 builds the first one); no external contract to preserve |
| Missing `ConfirmarAfectacionAsync` as a 4th `FACTURA_CORREGIDA` site (previously undercounted) | Low | Explicit test asserting exactly one `FACTURA_CORREGIDA` emission when `AfectacionMixta` changes via confirmation on a VALIDADA factura |
| Writing `Factura.Estado = 'VALIDADA'` for the first time flips previously-dead guards (`DOCUMENTACION_ACTUALIZADA`, `FACTURA_CORREGIDA`) live, which could surface latent behavior not exercised before in production | Med | Regression run of #7/#11 suites plus new tests asserting the guards fire correctly once `Estado` is real, before/after comparison of affected endpoints |
| New state-only port member on `IUnidadDeTrabajo` used without an ETag could bypass the optimistic-concurrency discipline the rest of the port enforces | Low | Scope the new member narrowly to the single `Estado` write inside `ValidarInternoAsync`'s existing transaction; no new public write path exposed |
| `POST /validar` on a `DESCARTADA` factura now returns 409 instead of silently confirming an asiento against a discarded factura — this is a **visible behavior change on an existing endpoint** for any caller currently relying on (or unknowingly triggering) the `abrir → descartar → validar` path | Med | Not a bug fix disguised as silent — call out explicitly in the item's release note; dedicated test asserting 409 + rollback of the asiento confirmation on `NoTransicionable` |

## Rollback Plan

Producer emission calls are additive inserts inside existing transactions — revert by removing the
4 new `EmitirOutboxAsync` call sites; no data migration needed (rows already inserted are historical
facts, safe to leave). The payload retrofit on the 2 pre-existing events is revertible independently
by reverting `PayloadOutbox` usage at those 2 call sites back to the old scalar shape (no consumer
depends on the new shape yet). Consumer is a new, independently deployable Python module with no
schema writes outside `OutboxEventIntegracion`/`CommandQueue`'s existing update grants — revert by
disabling/removing the worker process; producer emission is unaffected either way.

## Dependencies

- Item #1 schema (already applied) — `fact.OutboxEvent`, `OutboxEventIntegracion`, `CommandQueue`,
  `InboxEvent`, `SeqOutbox`.
- Item #11's `IUnidadDeTrabajo.EmitirOutboxAsync` port (already implemented).
- ADR 0002, 0003, 0004, 0010, 0016, 0019.

## Proposal question round

Scope (Approach 1: fold producer-gap fix into #14) was already decided by the project owner before
this proposal was drafted. Product questions confirmed by the project owner across two rounds:

1. **`FACTURA_CORREGIDA` trigger** — confirmed: **any** accepted update to an already-validated
   factura (not limited to fiscal/monetary fields) triggers `FACTURA_CORREGIDA` once per correction
   transaction. This is broader than the initial fiscal/monetary-only assumption.
2. **Consumer cadence** — confirmed: 1-minute cadence, independent scheduler, consistent with item
   #11's `InboxEvent` side and ADR 0005's 15-minute visibility budget.
3. **Payload retrofit (design Open Question 1)** — confirmed: fix `FACTURA_VALIDADA`'s and
   `DOCUMENTACION_ACTUALIZADA`'s payloads to the full-snapshot envelope **inside #14**, not deferred
   to #15. All 5 catalog events land self-sufficient and consistent before any consumer handler is
   built.
4. **Fourth emission point (design Open Question 2)** — confirmed: `ConfirmarAfectacionAsync`
   (`ServicioDeFacturas.cs:384`) is a `FACTURA_CORREGIDA` emission point, same rule as decision 1
   (any accepted update to a VALIDADA factura). 4 emission points total, not 3.

Two further design Open Questions were resolved by the project owner after this proposal's first
version:

5. **Lease duration (design Open Question 3)** — confirmed: **5 minutes**, within ADR 0005's
   15-minute visibility budget.
6. **Dead `Estado == VALIDADA` guard (design Open Question 4)** — confirmed: close the #11 gap
   inside #14 by making `ValidarInternoAsync` actually write `fact.Factura.Estado = 'VALIDADA'`
   (root-cause fix), rather than changing the outbox predicate to route around the wrong state.
7. **`validar` on a `DESCARTADA` factura (design Open Question 5)** — confirmed: **reject with 409
   and roll back the asiento confirmation**. `DESCARTADA` is treated as terminal, matching
   `DescartarAsync`'s own `Estado == VALIDADA` guard, consistent with ADR 0008's intent. This is a
   **visible behavior change on an existing endpoint** (`POST /validar`) — previously the
   `abrir → descartar → validar` path silently confirmed an asiento against a discarded factura;
   it must be called out in the item's release note.

This closes the proposal's last open product question. Open Questions 6 (`ReabrirAsync`/
`AnularAsync` do not move `Estado` back — the asymmetry D1 depends on) and 7 (no backfill of
historical validated facturas — forward-only, an operational decision that does not block #14) do
not require a further owner decision: the design already treats them as resolved (design.md, D10
context and Migration/Rollout). **The proposal has no remaining open questions**; #14 is ready for
`sdd-tasks`.

## Success Criteria

- [ ] All 5 catalog event types (`FACTURA_VALIDADA`, `DOCUMENTACION_ACTUALIZADA`,
      `FACTURA_CORREGIDA`, `ASIENTO_CORREGIDO`, `ASIENTO_ANULADO`) are emitted from their
      respective domain operations, across all 4 `FACTURA_CORREGIDA`/`ASIENTO_CORREGIDO`/
      `ASIENTO_ANULADO` emission points (`ValidarInternoAsync`, `AnularAsync`, `PatchAsync`,
      `ConfirmarAfectacionAsync`).
- [ ] All 5 catalog events carry a self-sufficient full-snapshot payload per ADR 0004 — including
      `FACTURA_VALIDADA` and `DOCUMENTACION_ACTUALIZADA`, retrofitted off their current bare-scalar
      payloads.
- [ ] Python worker claims outbox/command rows via `READPAST` behind an interface, with no direct
      SQL-Server-specific code outside that interface's implementation.
- [ ] A stale/superseded event is marked `OBSOLETO` and never surfaces as an error or retry
      candidate.
- [ ] Bidirectional boundary contract tests pass for `OutboxEvent`/`CommandQueue`, including the
      `usr_api`/`usr_worker` permission matrix.
- [ ] Python batch-claim lease is 5 minutes, verified by a test that a claim held past the lease
      becomes reclaimable again.
- [ ] `ValidarInternoAsync` writes `fact.Factura.Estado = 'VALIDADA'` in the validar transaction;
      the previously-dead `Estado == VALIDADA` guards on `DOCUMENTACION_ACTUALIZADA` and
      `FACTURA_CORREGIDA` are exercised by a production-shaped test (no hand-set state) and fire
      correctly.
- [ ] `POST /validar` on a `DESCARTADA` factura returns 409 and rolls back the asiento
      confirmation (`NoTransicionable` treated as terminal), closing the previously-reachable
      `abrir → descartar → validar` path; this behavior change is documented in the item's release
      note.
