# Proposal: Detalle y validación de factura (BACKLOG #12)

## Intent
BACKLOG #12 needs a working screen where a user reviews a scanned invoice next to
its auto-generated accounting entry, edits it, saves progress, and validates it.
Item #11 built the domain API but the SPA is an empty scaffold and three read
endpoints the screen structurally depends on (document bytes, unified document
list, asiento lookup by factura) were never added. Building the screen without
them is impossible, so this proposal folds that minimal API extension into #12.

## Scope

### In Scope
- Angular detail screen: document viewer (left) + editable factura/asiento form (right), per DESIGN_BRIEF.md pattern "documento + formulario".
- SPA foundational wiring: routing guard, HTTP interceptor for 401, `__Host-` session cookie handling (item #2 integration) — needed for the screen to function at all, since `SmartNet/spa/` has no `src/` yet.
- "Guardar avance" (PATCH factura + PATCH/POST/DELETE líneas asiento, no state transition) and "Validar" (POST validar, single transaction) wired to existing #11 endpoints.
- New minimal API additions (ADR 0013 contract, .NET-side only, read/orchestration — no new accounting rules):
  - `GET /api/documentos/{id}/contenido` — serves raw bytes with real MIME type, same-origin, for `<iframe>`.
  - Unified documents-list endpoint per factura — merges `DocumentoRecibido` (Python-owned, read-only from .NET) with `AdjuntoManual` (.NET-owned), per ADR 0013/ADR 0003 partition.
  - A way to resolve `FacturaId → AsientoContableId` over HTTP (exposing existing `IUnidadDeTrabajo.ObtenerAsientoVigenteIdAsync`), plus `GET /api/asientos/{id}`.
  - `TipoCambioCongelado.Venta` (frozen sale rate, ADR 0018 pt.1) added to `FacturaRespuesta`/`AsientoRespuesta` DTOs — confirmed via code trace: domain models it, no store/DTO exposes it yet, and DESIGN_BRIEF.md requires showing "tipo de cambio **venta** aplicado" (not compra — see Decision 1 below).
- Line-editing UX for the asiento (add/edit/reorder), decided explicitly in this proposal (Decision 2).
- Explicit 412 conflict UX (Decision 4).

### Out of Scope
- Any new accounting invariant or rule (core logic stays in `SmartNet.Contable.Core`/`SmartNet.Facturacion.Core`, ADR 0019 untouched).
- Bandeja (list/queue) screen — item #13.
- Configuración screen for allowed file types/max upload size — item #17 (ADR 0013 explicitly defers this).
- Any accounting-system integration or data migration (project-wide rule).
- Python-side changes; .NET reads `DocumentoRecibido` read-only, never writes it (ADR 0003).

## Decisions (resolved during this proposal, not deferred)

1. **"Tipo de cambio compra aplicado"** — resolved as a mischaracterization in the exploration note. DESIGN_BRIEF.md §3 literally says "tipo de cambio **venta** aplicado"; ADR 0018 pt.1 forbids using Compra for a foreign-currency liability. The field is `TipoCambioCongelado.Venta`, already modeled in `SmartNet.Contable.Core` but not yet persisted in `SqlFacturacionStore`/exposed in `FacturaRespuesta`/`AsientoRespuesta`. This proposal adds that plumbing; no new business rule.
2. **Asiento line-editing UX**: inline edit-in-place per row (matches existing `PATCH lineas/{lineaId}`), an explicit "add line" affordance appending a row (`POST lineas`), and delete-in-place (`DELETE lineas/{lineaId}`) with a confirm dialog (deletion is not logically audited at the line level, unlike `AdjuntoManual`). No drag-reorder in this slice — línea order is display-only, not a persisted invariant; deferred if a future need arises.
3. **Auth wiring**: guard checks session state before entering the detail route; a 401 interceptor redirects to login and preserves the return URL; cookie is `__Host-`-scoped per item #2, no token in localStorage/signals.
4. **412 conflict UX**: on `VersionEnConflicto`, the screen does NOT auto-merge. It shows a blocking banner ("alguien más lo cambió, recarga" per DESIGN_BRIEF.md §3) and offers a single "recargar" action that refetches factura+asiento+If-Match tokens, discarding local unsaved edits. This is distinct from a 422/409 invariant violation message, per DESIGN_BRIEF.md's explicit two-message requirement.

## Capabilities

### New Capabilities
- `documento-contenido-api`: serve raw document bytes with correct MIME for same-origin iframe viewing.
- `documentos-lista-unificada-api`: merged read view of DocumentoRecibido + AdjuntoManual per factura.
- `asiento-lectura-api`: GET asiento by id, and factura→asiento resolution over HTTP.
- `pantalla-detalle-validacion`: Angular screen — viewer + form, guardar avance, validar, edición de líneas.
- `spa-auth-shell`: routing guard, 401 interceptor, session cookie wiring (foundational, reusable by future screens).

### Modified Capabilities
- `factura-respuesta-asiento-respuesta`: add `TipoCambioVenta` field to existing `FacturaRespuesta`/`AsientoRespuesta` DTOs (additive, non-breaking).

## Approach
Backend: extend existing minimal-API endpoint files (`FacturaEndpoints.cs`, `AsientoEndpoints.cs`, new `DocumentoEndpoints.cs`) following the established RFC 9457 problem-detail pattern (`ProblemasDeNegocio.cs`) and existing auth middleware from #2. All new endpoints are read-only or thin orchestration over existing repositories/`IUnidadDeTrabajo` methods — no new domain logic, respecting ADR 0019 and ADR 0003.

Frontend: bootstrap `SmartNet/spa/src` with Angular signals per ADR 0009 — services hold private writable signals with `asReadonly()` exposure, `computed()` for derived cuadre/totales, `effect()` only for real side effects (e.g., syncing on guardar avance). Detail screen is the one screen with real local edit state before an explicit save.

## Affected Areas

| Area | Impact | Description |
|------|--------|-------------|
| `SmartNet/api/SmartNet.Api/DocumentoEndpoints.cs` | New | Contenido + lista unificada de documentos |
| `SmartNet/api/SmartNet.Api/AsientoEndpoints.cs` | Modified | Add GET by id, TipoCambioVenta field |
| `SmartNet/api/SmartNet.Api/FacturaEndpoints.cs` | Modified | Add asiento resolution, TipoCambioVenta field |
| `SmartNet/facturacion/SmartNet.Facturacion.Infrastructure/SqlFacturacionStore.cs` | Modified | Persist/read TipoCambioCongelado.Venta |
| `SmartNet/spa/src/**` | New | Full Angular app bootstrap: routing, auth guard, interceptor, detail screen |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| API extension scope creep beyond #12's minimal need | Med | Capabilities section above is the hard contract for sdd-spec; no endpoint beyond the 3 listed + 1 DTO field |
| Cross-partition read (DocumentoRecibido from .NET) misread as write access | Low | Explicit read-only repository, tests assert no write path (pattern already used for `NoWriteToDboStructuralTests`) |
| 412 handling UX diverges from ADR 0009's undecided polling interval | Low | Scoped narrowly to reload-on-conflict; polling interval remains a separate future decision |
| SPA foundational wiring (auth) done ad hoc without reuse in mind | Med | `spa-auth-shell` is called out as its own capability, reusable by item #13 |

## Rollback Plan
Backend additions are net-new endpoints/fields (additive, no breaking changes to #11's contracts) — revert via git revert of the endpoint/DTO commits with no data migration needed. Frontend is a new `src/` tree with no prior state — deleting/reverting the commit fully removes it with zero downstream impact.

## Dependencies
- Item #11 (done) for factura/asiento CRUD and auth (item #2).
- ADR 0013 (documentos), ADR 0009 (frontend signals), ADR 0018 (tipo de cambio venta), ADR 0003 (data partition), ADR 0008/0019 (concurrency, core purity).

## Success Criteria
- [ ] User can open a factura, see its document rendered in an iframe, and see the auto-generated asiento side by side.
- [ ] "Guardar avance" persists edits without changing estado.
- [ ] "Validar" transitions factura→VALIDADA and asiento→CONFIRMADO atomically, surfaces invariant violations distinctly from concurrency conflicts.
- [ ] 412 conflict shows the reload banner, never silently overwrites.
- [ ] Tipo de cambio venta aplicado is visible and traceable to `TipoCambioCongelado`.
