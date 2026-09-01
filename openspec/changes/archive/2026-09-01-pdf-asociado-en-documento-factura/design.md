# Design: Associated PDF reaches the factura document viewer

## Technical Approach

Design B (owner ruling, Engram #278): the paired PDF's `InboxEvent` is routed, **before**
`PoliticaDePromocion.Decidir`, to a merge/defer/discard branch that projects its
`fact.DocumentoFactura` onto the partner XML's already-promoted `fact.Factura`. No payload change
(`_VERSION` stays 1), no schema change, no coordinated deploy. The decision stays pure in
`SmartNet.Inbox.Core` (ADR 0019); only SQL lives in `SmartNet.Inbox.Infrastructure`.

`EventoInbox.DocumentoAsociadoId` and `TipoDocumento` **already exist** and
`PayloadInboxParser` already parses both (`PayloadInboxParser.cs:34-35`). **No parser change.**

## Architecture Decisions

### Decision 1 — Branch predicate is `DocumentoAsociadoId != null` **AND** `TipoDocumento == "PDF"`

| Option | Tradeoff | Decision |
|---|---|---|
| `DocumentoAsociadoId != null` alone (as written in the proposal) | **Broken**: `asociar_documentos` writes the FK on **both** `Procesamiento` rows (`procesamiento_repo.py:144-150`), so the XML event also carries it. Both sides would defer forever — nothing ever creates the `Factura`. | Rejected |
| Add `TipoDocumento == "PDF"` | `comprobante.asociar` only ever emits a `Par` when exactly one XML and exactly one PDF share the key (`comprobante.py:124-132`), so "paired + PDF" uniquely identifies the secondary side. XML stays primary (ADR 0017). | **Chosen** |

Rationale: the XML is the authoritative comprobante; the PDF is evidence. PDF-only (no pair) keeps
`DocumentoAsociadoId == null` and is untouched.

### Decision 2 — Partner resolution: two SELECTs, both inside `usr_api` grants

| Query | Purpose | Grant |
|---|---|---|
| A: `SELECT TOP(1) f.FacturaId FROM fact.DocumentoFactura df JOIN fact.Factura f ON f.FacturaId = df.FacturaId WHERE df.DocumentoRecibidoId = @documentoAsociadoId AND f.Estado <> 'DESCARTADA'` | Merge target (owner's SQL, verbatim) | `016:45` GRANT SELECT on `fact.DocumentoFactura`; `008:50` on `fact.Factura` |
| B (only when A is empty): `SELECT TOP(1) EstadoConsumo FROM fact.InboxEvent WHERE TRY_CAST(JSON_VALUE(Payload, '$.documento.documentoRecibidoId') AS BIGINT) = @documentoAsociadoId` | Distinguishes *not yet* from *never* | `008:115` GRANT SELECT, UPDATE on `fact.InboxEvent` |

**This closes the ADR 0003 knowability wall — no owner ruling needed.** .NET cannot map
`DocumentoRecibidoId → ProcesamientoId` (DENY on `fact.Procesamiento`, `008:81/84`), but the
partner event's own payload carries its `documentoRecibidoId`, and `fact.InboxEvent` is readable.
Query B reuses the exact JSON path `PayloadInboxParser` already depends on — same contract, no
new coupling.

Rejected: adding a persisted computed column + index on `InboxEvent` (schema change, out of
scope). Accepted cost: B is a table scan; it runs only on the defer/discard path, never on the
happy merge path.

### Decision 3 — Defer = do nothing

`PENDIENTE` is the row's existing state; `ListarPendientesAsync` (`SqlEventoInboxRepository.cs:24-29`)
selects it unconditionally and nothing else marks it failed. Deferring therefore issues **zero**
SQL — no new `EstadoConsumo` value, no `CK_InboxEvent_EstadoConsumo` change, no retry counter.
Rejected: a `DIFERIDO` state (schema change) and an in-memory retry queue (lost on restart).

### Decision 4 — Merge is its own port method, not a flag on `PromoverAsync`

`PromoverAsync`'s contract (insert `Factura` + `FacturaExtraccion` + `DocumentoFactura` + mark
`PROMOVIDO`) is unchanged; `ResultadoPromocion` is unchanged. A separate
`FusionarDocumentoAsync` avoids a `FacturaPromovida` argument that has no meaning on this path.

## Interfaces / Contracts

```csharp
// SmartNet.Inbox.Core — pure, no JSON, no SQL (PurityScanTests)
public abstract record ResolucionPar
{
    private protected ResolucionPar() { }
    public sealed record Fusionable(long FacturaId) : ResolucionPar;      // query A hit
    public sealed record ParNoPromovible(string Motivo) : ResolucionPar;  // partner DESCARTADO, or PROMOVIDO with no vigente Factura
    public sealed record NoDisponible : ResolucionPar;                    // partner event absent or PENDIENTE
}

public abstract record DecisionDocumentoAsociado
{
    private protected DecisionDocumentoAsociado() { }
    public sealed record Fusiona(long FacturaId) : DecisionDocumentoAsociado;
    public sealed record Difiere : DecisionDocumentoAsociado;
    public sealed record Descarta(string Motivo) : DecisionDocumentoAsociado;
}

public static class PoliticaDeDocumentoAsociado
{
    public static bool EsDocumentoAsociado(EventoInbox e) =>
        e.DocumentoAsociadoId is not null && e.TipoDocumento == "PDF";
    public static DecisionDocumentoAsociado Decidir(ResolucionPar r);      // pure 1:1 map
}

// IPromocionRepository — two additions
Task<ResolucionPar> ResolverParAsync(long documentoAsociadoId, CancellationToken ct);
Task FusionarDocumentoAsync(long inboxEventId, long facturaId, DocumentoPromovido documento, CancellationToken ct);
```

`FusionarDocumentoAsync` = one `SqlTransaction`: existing private
`InsertarDocumentoFacturaAsync` (with the 2601/2627 catch) + existing private
`MarcarPromovidoAsync`. It never calls `InsertarFacturaAsync` / `InsertarExtraccionesAsync`.

## Data Flow

```
ProcesarPendientesAsync
  └─ PayloadInboxParser.Parse ──► EventoInbox
       ├─ EsDocumentoAsociado? ── no ──► PoliticaDePromocion.Decidir  (UNCHANGED path)
       └─ yes
            └─ ResolverParAsync(DocumentoAsociadoId)   [A, then B if A empty]
                 ├─ Fusionable(id)   ──► FusionarDocumentoAsync ──► DocumentoFactura + PROMOVIDO
                 ├─ NoDisponible     ──► (no SQL) row stays PENDIENTE ──► next cycle
                 └─ ParNoPromovible  ──► DescartarAsync(motivo)
```

## Ordering / Idempotency Proof

| Scenario | Outcome |
|---|---|
| XML event first | XML promotes; PDF: A hits → merge. 1 `Factura`, 2 `DocumentoFactura`. |
| PDF event first (same cycle) | A empty, B = partner `PENDIENTE` → `Difiere` (no writes). XML promotes. Next cycle merges. |
| Partner event not yet emitted | B empty → `NoDisponible` → defer. Self-heals. |
| Partner XML `Descarta`d | B = `DESCARTADO` → `Descarta`. PDF never self-promotes (owner decision 3). |
| Partner `Factura` later `DESCARTADA` by a human | A empty (`Estado <> 'DESCARTADA'`), B = `PROMOVIDO` → `ParNoPromovible` → discard. **Terminates; no infinite defer.** |
| `reprocesar` re-emits the PDF event | A hits; merge INSERT violates `UQ_DocumentoFactura_DocumentoRecibidoId` → 2601/2627 catch skips; `MarcarPromovidoAsync` still runs → `PROMOVIDO`. No duplicate row. |

Defer is a pure no-op, so cycle order is irrelevant and re-entry is free.

## File Changes

| File | Action | Description |
|---|---|---|
| `inbox/SmartNet.Inbox.Core/ResolucionPar.cs` | Create | Closed hierarchy (mirrors `DecisionPromocion.cs`) |
| `inbox/SmartNet.Inbox.Core/DecisionDocumentoAsociado.cs` | Create | Closed hierarchy |
| `inbox/SmartNet.Inbox.Core/PoliticaDeDocumentoAsociado.cs` | Create | Pure predicate + pure map |
| `inbox/SmartNet.Inbox.Core/IPromocionRepository.cs` | Modify | +`ResolverParAsync`, +`FusionarDocumentoAsync` |
| `inbox/SmartNet.Inbox.Infrastructure/SqlPromocionRepository.cs` | Modify | Queries A/B + merge transaction; reuses both existing private helpers |
| `inbox/SmartNet.Inbox.Infrastructure/PromocionBackgroundService.cs` | Modify | Branch in `ProcesarPendientesAsync` before `PoliticaDePromocion.Decidir` |
| `SmartNetWeb/src/app/detalle/ui/visor-documento/visor-documento.ts` | Modify | Default selection prefers first inline-renderable |

## SPA change (minimal)

```ts
// Mirrors SmartNet.Api DocumentoContenido.MimeAllowList — anything else is served
// application/octet-stream and cannot render in the iframe.
private static readonly MIMES_RENDERIZABLES = new Set(['application/pdf', 'image/png', 'image/jpeg']);

readonly seleccionado = computed<DocumentoRespuesta | null>(() => {
  const documentos = this.documentos();
  if (documentos.length === 0) return null;
  const id = this.seleccionadoIdSignal();
  const explicito = documentos.find((d) => d.id === id);
  if (explicito) return explicito;
  return documentos.find((d) => VisorDocumento.MIMES_RENDERIZABLES.has(d.mimeType)) ?? documentos[0];
});
```

Explicit user selection still wins; only the *default* changes.

## Testing Strategy (ADR 0019, Strict TDD — RED first)

| Level | File | Cases |
|---|---|---|
| 1 — pure Core (no DB) | `SmartNet.Inbox.Core.Tests/PoliticaDeDocumentoAsociadoTests.cs` (new) | `EsDocumentoAsociado`: PDF+asociado → true; **XML+asociado → false** (Decision 1 regression guard); PDF sin asociado → false. `Decidir`: each `ResolucionPar` → its `DecisionDocumentoAsociado`. |
| 1 — purity guard | `SmartNet.Inbox.Core.Tests/PurityScanTests.cs` | Passes unchanged (new Core types touch no SQL/JSON/clock). |
| 2 — boundary contract (real versioned schema) | `SmartNet.Inbox.Infrastructure.Tests/SqlPromocionRepositoryTests.cs` | `ResolverParAsync`: A hit → `Fusionable`; partner factura `DESCARTADA` + event `PROMOVIDO` → `ParNoPromovible`; partner event `DESCARTADO` → `ParNoPromovible`; partner event `PENDIENTE`/absent → `NoDisponible`. `FusionarDocumentoAsync`: inserts one row on the given `FacturaId`, creates **no** `fact.Factura`, marks `PROMOVIDO`; second call is an idempotent no-op (`UQ_DocumentoFactura_DocumentoRecibidoId`). |
| 2 — permission matrix | `SmartNet.Inbox.Infrastructure.Tests/PermissionSufficiencyTests.cs` | Queries A and B execute under the `usr_api`/`fact_api` role; no `fact.Procesamiento`/`fact.DocumentoRecibido` reference (`NoWriteToDboStructuralTests` also stays green). |
| 3 — E2E cycle | `SmartNet.Inbox.Infrastructure.Tests/PromocionBackgroundServiceTests.cs` | **Yes, add assertions.** New payload pair (XML `documentoRecibidoId:1/asociado:2`, PDF `documentoRecibidoId:2/asociado:1`, PDF `comprobante` incomplete). Both orders → exactly 1 `fact.Factura`, 2 `fact.DocumentoFactura` on it, both events `PROMOVIDO`. PDF-first single cycle → PDF still `PENDIENTE`, 0 discards. XML `Descarta` → PDF `DESCARTADO` after two cycles. Existing three tests stay green (note: current `PayloadCompleto` is XML+asociado — Decision 1 keeps it on the unchanged path). |
| SPA unit | `detalle/ui/visor-documento/visor-documento.spec.ts` | XML-first list with a PDF → PDF selected; all-XML list → `documentos[0]`; explicit `onSeleccionar` overrides. |

`dotnet test SmartNet.sln` (needs local SQL Server) and `npm test` in `SmartNetWeb`. No `pytest`
change — the worker is untouched.

## Threat Matrix

`N/A` — this change adds no routing, shell command, subprocess, VCS/PR automation,
executable-file classification, or process-integration boundary.

Boundary notes (not matrix rows): every new SQL parameter is a `SqlParameter`, never interpolated
(existing convention in `SqlPromocionRepository`). `DocumentoContenido.MimeAllowList` is
**unchanged**, so the SPA default-selection tweak cannot cause new content to render in the
same-origin iframe — it only reorders among documents the server would already serve inline
(D2/ADR 0013 XSS boundary intact).

## Size Estimate

~195 lines production + ~370 lines test ≈ **565 changed lines**. Under the 800-line budget.
Single PR. `400-line budget risk: Medium` — if `sdd-tasks` forecasts overrun, the natural slice is
PR 1 (.NET promotion) / PR 2 (SPA viewer).

## Migration / Rollout

No schema change, no payload version bump, no coordinated deploy. Rollback is a pure code revert;
already-merged `DocumentoFactura` rows stay valid. Backfill/cleanup of pre-fix duplicate or lost
facturas is **out of code scope** — a one-off SQL script is delivered separately (owner decision 5).

## Open Questions

- [ ] None blocking. Decision 1 corrects the proposal's stated predicate; it is a mechanical
      correctness refinement inside Design B, not a change to owner decisions 1–3.
