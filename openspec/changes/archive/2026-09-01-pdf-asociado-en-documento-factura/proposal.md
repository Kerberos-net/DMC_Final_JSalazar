# Proposal: Associated PDF reaches the factura document viewer

## Intent

For a SUNAT XML+PDF pair, only the XML becomes a `fact.DocumentoFactura`. The
viewer MIME allow-list never renders XML inline, so the human validator sees an
empty/download-only viewer. Worse, the PDF's independent InboxEvent either
creates a spurious second `fact.Factura` (`PosibleDuplicado=1`) when OCR got all
4 fields, or is silently discarded when OCR is incomplete. Owner ruling
(Engram #278): fix via **Design B** — de-duplicate at promotion, no payload
contract change, no schema change, no coordinated deploy.

## Scope

### In Scope
- `.NET SmartNet.Inbox.Infrastructure`: `PromocionBackgroundService`,
  `SqlPromocionRepository`, `IPromocionRepository` port (+ `FakeUnidadDeTrabajo`
  / test doubles). New promotion branch: when the event's `documentoAsociadoId`
  is non-null, resolve the partner's already-promoted factura and project THIS
  event's `fact.DocumentoFactura` onto that `FacturaId` — no second `fact.Factura`,
  no sufficiency check.
- Defer path: partner factura not found yet → leave event `EstadoConsumo='PENDIENTE'`,
  retry next `PromocionBackgroundService` cycle (self-heal).
- Discard-with-XML: if the paired XML fails `PoliticaDePromocion.Decidir`
  (`Descarta`), the associated PDF does not promote on its own.
- Small SPA `VisorDocumento` tweak: default selection to first renderable (PDF)
  instead of earliest fecha.

### Out of Scope / Non-goals
- Design A (one event per comprobante, versioned payload) — note as possible
  future direction.
- Worker Python, `InboxEvent` payload (`_VERSION` stays 1), DB schema,
  `PoliticaDePromocion` (pure policy unchanged), any REGLAS.md / accounting core.
- Backfill/cleanup of pre-fix duplicate or lost facturas (no migration, ADR 0003;
  one-off SQL delivered separately, outside this change's code scope).
- Stale `SIN_PAREJA` on an early XML event — left as-is (cosmetic).
- XML inline rendering / viewer MIME allow-list changes.

## Capabilities

### New Capabilities
- None

### Modified Capabilities
- `factura-promotion`: promotion of an InboxEvent carrying a non-null associated
  document projects its `fact.DocumentoFactura` onto the partner's existing
  factura; defers when partner not yet promoted; does not self-promote when the
  partner XML is discarded.
- `pantalla-detalle-validacion`: document viewer default selection prefers the
  first inline-renderable document.

## Approach

1. Promotion branch keyed on `documentoAsociadoId != null`. Lookup within
   `usr_api` grants:
   `SELECT f.FacturaId FROM fact.DocumentoFactura df JOIN fact.Factura f ON
   f.FacturaId = df.FacturaId WHERE df.DocumentoRecibidoId = @documentoAsociadoId
   AND f.Estado <> 'DESCARTADA'`.
2. Found → single-tx insert of this event's `fact.DocumentoFactura` on that
   `FacturaId`, `MarcarPromovidoAsync`. Not found → defer (`PENDIENTE`).
3. Merge insert still hits `UQ_DocumentoFactura_DocumentoRecibidoId`; the 2601/2627
   idempotent-catch skips on re-emitted events (`reprocesar`).
4. SPA: `VisorDocumento.seleccionado()` default picks first row whose mime is in
   the inline allow-list, falling back to `documentos[0]`.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `SmartNet.Inbox.Infrastructure/PromocionBackgroundService` | Modified | Route associated-document events to merge/defer branch |
| `SmartNet.Inbox.Infrastructure/SqlPromocionRepository` | Modified | Partner-factura lookup + project DocumentoFactura onto existing FacturaId |
| `SmartNet.Inbox.Application/IPromocionRepository` (+ fakes) | Modified | New port method(s) for partner resolution / merge projection |
| SPA `VisorDocumento` | Modified | Default selection to first renderable |
| `openspec/specs/factura-promotion`, `openspec/specs/pantalla-detalle-validacion` | Modified | Delta specs |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Non-deterministic event order (`foreach` over unordered SELECT) | High | Defer/self-heal branch is order-independent; unit test both orders |
| Association lags event emission; XML promoted with `SIN_PAREJA` | Med | Merge branch keyed on `documentoAsociadoId`, independent of warning; stale warning accepted |
| Paired PDF stranded if its XML never promotes | Low | Accepted bounded risk (owner); such XMLs rarely associate (need `DatosExtraidos`) |
| `reprocesar` re-emits events → double projection | Med | `UQ_DocumentoFactura_DocumentoRecibidoId` + 2601/2627 catch; boundary contract test |
| Lookup exceeds `usr_api` grants | Low | Query restricted to `fact.DocumentoFactura` + `fact.Factura` (016 grants) |

## Test Strategy (ADR 0019, Strict TDD — `dotnet test`, `pytest`)

- Unit (`PromocionBackgroundService` / policy-free branch): partner found → merge;
  partner not found → defer `PENDIENTE`; partner `DESCARTADA` → treated as not
  found / no self-promotion; re-emitted associated event → idempotent no-op.
- Boundary contract test: merge insert path respects
  `UQ_DocumentoFactura_DocumentoRecibidoId`.
- Evaluate whether the single ADR-0019 E2E test needs an added assertion (pair →
  one `fact.Factura`, two `fact.DocumentoFactura`, viewer lists both).
- SPA: `VisorDocumento` default-selection unit test.

## Size Estimate

~80–150 LOC production + tests, single runtime. Well within the 800-line review
budget; likely a single PR.

## Rollback Plan

Revert the branch. No schema/payload/deploy coordination changed, so revert is a
pure code rollback; any facturas already merged remain valid (no second factura
created). Pre-existing pre-fix duplicates are unaffected either way.

## Dependencies

None. Relies only on `fact.DocumentoFactura.DocumentoRecibidoId` already stored.

## Affected BACKLOG items

Gap spans #6 (association), #7 (inbox/promotion projection), #12 (detail + viewer);
no existing item covers it. Recommend a NEW item, sibling to #24 (same
discovered-during-implementation pattern as #19 → #24):

> **#25 — Proyección del PDF asociado sobre la factura del par.** Al promover un
> InboxEvent con `documentoAsociadoId`, .NET adjunta el `fact.DocumentoFactura`
> del PDF a la factura ya promovida del XML (sin segunda factura), difiere si el
> par aún no promovió y no auto-promociona si el XML fue descartado; el visor de
> detalle prioriza el documento renderizable.

## Proposal question round

Owner decisions 1–3 already ruled (Engram #278); 4–6 defaulted as above. No open
product questions — proceed to spec/design. Raise Design A with the owner later
as the cleaner long-term model.

## Success Criteria

- [ ] A promoted XML+PDF pair yields exactly one `fact.Factura` and two
      `fact.DocumentoFactura` rows on it.
- [ ] `GET /api/facturas/{id}/documentos` lists both; viewer renders the PDF inline
      by default.
- [ ] No spurious `PosibleDuplicado` factura from the PDF event.
- [ ] PDF-only (no pair) promotion path unchanged.
- [ ] Associated PDF event defers cleanly when the XML is not yet promoted and
      self-heals on the next cycle.
