# Proposal: Bandeja e incidencias (BACKLOG #13)

## Intent

`GET /api/bandeja` exists but is deliberately the #7-shaped partial version — it only supports
`estado`/`orden`. Users working the purchase-invoice queue today cannot filter by date range or
provider, cannot page through results, cannot see *why* a document failed processing (the error
detail lives in `fact.ProcesamientoError`, which no UI surfaces), and cannot retry a failed
document without going around the application. This blocks the operational workflow the bandeja
is supposed to serve: triage what needs attention, understand why, and act on it — without ever
letting Angular combine facturas and processing incidents itself (ADR 0008/0003 forbid that).
This change completes ADR 0008's literal contract for `GET /api/bandeja` and gives the incidencias
workflow (filter → inspect error → reprocesar) a real UI, reusing the `reprocesar` command endpoint
#11 already shipped.

## Scope

### In Scope

- **`GET /api/bandeja` widened in place** (Approach 1, not a new endpoint) to the full ADR 0008
  contract: `estado`, `desde`, `hasta`, `proveedor`, `pagina`, alongside the existing `orden`.
- **Explicit `origen` discriminator per row** (`FACTURA` vs `INCIDENCIA`), combined server-side in
  `SqlBandejaRepository`, per ADR 0008 line 50 ("cada elemento declara su origen. Angular nunca
  combina fuentes") and ADR 0003 (Python owns `fact.ProcesamientoError`; .NET only reads it, never
  writes it, to build this view).
- **Pagination**: fixed by product-owner decision — **20 items per page**, envelope
  `{ items, pagina, tamanioPagina, totalRegistros, totalPaginas }`. `sdd-design` inherits this
  shape, does not invent it.
- **Panel de errores**: exposes `fact.ProcesamientoError` history (`Mensaje`, `Clasificacion`,
  `OcurridoEn`) for **both** `INCIDENCIA` rows and already-promoted `FACTURA` rows — a promoted
  factura can be reprocessed (e.g. via an integration retry) and fail again, and that history must
  stay visible. `origen` still discriminates the row; error history is a projection available on
  either origin, not exclusive to `INCIDENCIA`.
- **`{id}` in `POST /api/incidencias/{id}/reprocesar` is fixed as `ProcesamientoId`** — the natural
  key, since `fact.ProcesamientoError` is indexed by it, not by `InboxEventId`/`FacturaId`. This
  proposal documents the semantics; #11 already shipped the route and the enqueue-only behavior
  (`fact.CommandQueue(Tipo=REPROCESAR_DOCUMENTO)`, no direct Python call, no `AuditoriaCorreccion`
  row — reprocesar is not in the `Accion` enum, matching #11's ratified answer #2).
- **Reprocesar UX with mandatory confirmation** (business decision, already made by the product
  owner): the SPA must show an explicit confirmation step before enqueuing a reprocess command —
  this guards against accidental/duplicate reprocessing of costly incidents. The reprocesar
  control is disabled while a `CommandQueue` row for that `ProcesamientoId` is still pending, and
  **re-enables after a fixed 5-minute timeout** even if #14 (Outbox y mensajería) has not yet
  claimed the row — an explicit interim behavior, not a permanent design (see Dependencies).
- **`inbox-list.ts` read-only restriction relaxed, narrowly**: the presentational component's
  current contract ("read-only... the template never renders a button") is amended to allow
  exactly one new action — reprocesar (with confirmation) — surfaced on incidencia rows. No other
  action (edit, validate, discard, etc.) is added to this component; those remain #12's
  territory (`detalle-page`).
- Filters (`desde`, `hasta`, `proveedor`) extending `ui/inbox-filter/` and the `data-access/`
  service, following the existing container/presentational + signals pattern (no parallel module).
- **Default view (empty filters)**: fixed by product-owner decision — with no filters applied, the
  bandeja shows only non-terminal items (pending / with an open incidencia). Already-validated
  `FACTURA` rows with no open error appear only when the user explicitly filters for that state.

### Out of Scope (non-goals)

- **The 6th indicator, `EsReferenciaExterna`** — stays at its DDL default. Already decided (D5,
  ADR 0005/WU6 of #7): no reference-note data exists until #10 (Notas de crédito) ships. Not
  reopened here.
- **#18 (Ajuste visual SPA)** — purely visual/token work depending on #12. Not touched here.
- **A new, separate `GET /api/incidencias` endpoint** — rejected (see exploration Approach 2): it
  would contradict ADR 0008's literal contract and require amending the ADR for no functional
  gain.
- **Multi-role authorization for reprocesar** — ADR 0007 already rules out multi-role auth
  (single user, full access) for this system. `RequireAuthorization()` (session-cookie presence)
  is the only gate; no incident-specific permission model is introduced.
- **Notas de crédito domain logic** — #10's territory, unrelated to this change.
- **Any change to how Python writes `fact.ProcesamientoError`** — .NET only reads it here.
- **Retrying/backoff policy for `TRANSITORIO` vs `PERMANENTE` classes** — ADR 0010 already governs
  classification; this change surfaces the classification, it does not change retry behavior.

## Capabilities

### Modified Capabilities
- `bandeja` (currently #7-shaped): widen query surface (filters, pagination), add `origen`
  discriminator, add processing-error projection for `INCIDENCIA` rows.
- `inbox` SPA module (`SmartNet/spa/src/app/inbox/`): add filter fields, panel de errores, and the
  reprocesar action with confirmation; relax `inbox-list.ts`'s read-only contract for that one
  action only.

### Unchanged (explicitly)
- `POST /api/incidencias/{id}/reprocesar` route and enqueue-only behavior — already shipped by
  #11; this proposal only fixes the meaning of `{id}` and adds the SPA confirmation flow around it.

## Approach

Approach 1 (ampliar `GET /api/bandeja` in place), per the exploration's recommendation:

1. `IBandejaRepository`/`BandejaItem` gain the new filter parameters and an `origen`-discriminated
   projection shape (exact shape is `sdd-design`'s job).
2. `SqlBandejaRepository` extends its SQL to accept `desde`/`hasta`/`proveedor`/`pagina`, and joins
   against `fact.ProcesamientoError` server-side to produce `INCIDENCIA` rows — combining stays
   entirely in .NET, never in Angular (ADR 0003/0008).
3. `BandejaEndpoints.cs` accepts and validates the new query parameters.
4. SPA: `inbox.service.ts` passes the new filters through; `inbox-filter` gains date-range and
   provider inputs; `inbox-list.ts` renders the panel de errores for `INCIDENCIA` rows and a
   reprocesar action gated by a confirmation dialog and a "pending command" disabled state.

This keeps a single source of truth for the combined view (ADR 0008's literal contract), reuses
the command endpoint #11 already built, and extends rather than forks the existing `inbox/` module.

## Affected Areas

| Area | Impact | Description |
|------|--------|--------------|
| `SmartNet/inbox/SmartNet.Inbox.Core/IBandejaRepository.cs`, `BandejaItem` | Modified | New filter params, `origen`-discriminated projection |
| `SmartNet/inbox/SmartNet.Inbox.Infrastructure/SqlBandejaRepository.cs` | Modified | Extended SQL, join to `fact.ProcesamientoError`, pagination |
| `SmartNet/api/SmartNet.Api/BandejaEndpoints.cs` | Modified | Accept/validate `desde`, `hasta`, `proveedor`, `pagina` |
| `SmartNet/spa/src/app/inbox/feature/inbox-page/inbox-page.ts` | Modified | New filter signals, refetch `effect()` (ADR 0009) |
| `SmartNet/spa/src/app/inbox/data-access/inbox.service.ts` | Modified | Pass new filters, expose reprocesar call |
| `SmartNet/spa/src/app/inbox/ui/inbox-filter/*` | Modified | Date-range and provider inputs |
| `SmartNet/spa/src/app/inbox/ui/inbox-list/inbox-list.ts` | Modified | Relax read-only contract for reprocesar; render panel de errores |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| `origen` discriminator shape drifts between `sdd-design` and SPA consumption if left ambiguous | Low | `sdd-spec`/`sdd-design` must fix the exact union type before `sdd-tasks` |
| Relaxing `inbox-list.ts`'s read-only contract is a documented contract change, not a silent patch | Low | Called out explicitly in Scope and Affected Areas |
| Confirmation + pending-disable UX for reprocesar adds client-side state not previously modeled in `inbox/` | Low | Existing signals pattern (ADR 0009) already supports derived/loading state; no new pattern needed |
| 5-minute re-enable timeout can let a user reprocesar again while the original command is still genuinely in flight (no real claim signal until #14) | Medium | Accepted interim tradeoff per product-owner decision; confirmation dialog + explicit disable window already reduce accidental duplicate reprocessing |

## Rollback Plan

Additive: new query parameters have defaults that preserve current behavior (`estado`/`orden`
only), and the `origen` discriminator is a superset of today's factura-only rows. Reverting the PR
slice (or feature-flagging the new filters/panel) does not require a schema rollback — no new
tables, only reads against `fact.ProcesamientoError`, which already exists.

## Dependencies
- #11 — closed and merged; `POST /api/incidencias/{id}/reprocesar` and `GET /api/bandeja` (partial)
  already exist in code.

## Success Criteria
- [ ] `GET /api/bandeja` accepts and correctly applies `estado`, `desde`, `hasta`, `proveedor`,
      `pagina`, `orden`
- [ ] Every returned row declares `origen` (`FACTURA` or `INCIDENCIA`); Angular performs no
      client-side merge of separate calls
- [ ] Both incidencia and already-promoted factura rows show their `fact.ProcesamientoError`
      history in a panel de errores
- [ ] Reprocesar requires explicit user confirmation before enqueuing, is disabled while a
      `CommandQueue` row for that `ProcesamientoId` is pending, and re-enables after 5 minutes
- [ ] `inbox-list.ts`'s contract change (read-only → read-only + reprocesar) is documented in code,
      not silently removed
- [ ] `SmartNet.Inbox.Core`/`SmartNet.Contable.Core` purity tests still pass unmodified (ADR 0019)

## Proposal question round — resolved

The product owner answered all four open questions before `sdd-spec`/`sdd-design`. These are now
fixed requirements, not assumptions:

1. **Page size for `pagina=`**: **20 items per page**, envelope
   `{ items, pagina, tamanioPagina, totalRegistros, totalPaginas }`.
2. **Panel de errores scope**: shows `fact.ProcesamientoError` history for **both** `INCIDENCIA`
   and already-promoted `FACTURA` rows.
3. **"Pending command" disable window**: the reprocesar button re-enables after a **fixed 5-minute
   timeout**, independent of #14 (Outbox y mensajería) claiming the `CommandQueue` row. This is an
   explicit interim behavior for #13; #14 may later replace the timeout with a real claim-based
   signal, but that is #14's concern, not this change's.
4. **Default filter state**: with empty filters, the bandeja shows **only non-terminal items**
   (pending / with an open incidencia). Already-validated facturas require an explicit filter.
