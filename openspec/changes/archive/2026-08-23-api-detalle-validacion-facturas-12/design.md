# Design: Detalle y validación de factura (BACKLOG #12)

## Technical Approach

Extend the existing thin minimal-API host (`FacturaEndpoints`/`AsientoEndpoints` pattern: deserialize
→ delegate → translate via `IfMatch`/`ProblemasDeNegocio`) with read-only routes, and extend — not
bootstrap — the existing Angular app, which already ships a working `inbox` feature under
`app/{feature}/{data-access,feature,ui,models}`. No accounting rule changes (ADR 0019).

## Blocking Architecture Finding

`008_usuarios_y_permisos.sql:81-86` runs `DENY SELECT ... ON fact.DocumentoRecibido TO fact_api`.
ADR 0003 §Privadas ("un solo componente escribe **y** lee"), invariant 3, and its stated consequence
("`usr_api` no puede leer `fact.Procesamiento`. Aunque alguien escriba ese SELECT, falla") make
*reading* the violation, not just writing. The proposal and both specs
(`documentos-lista-unificada-api`, `documento-contenido-api`) assume a "read-only from .NET" SELECT
on `DocumentoRecibido`. **That is structurally impossible and normatively forbidden.** ADR 0013's
".NET lee ese volumen y sirve los bytes" is intent-level and does not override ADR 0003's matrix.

Resolution mirrors the mechanism ADR 0013 already chose for the opposite direction (Python cannot
read `AdjuntoManual`, so paths travel in the event payload): document metadata travels in the
`InboxEvent` payload and is persisted .NET-side at promotion.

## Architecture Decisions

| # | Decision | Alternatives rejected | Rationale |
|---|---|---|---|
| D1 | Document metadata (`nombreArchivo`, `mimeType`, `rutaRelativa`, `tamanoBytes`) added to the `InboxEvent` payload `documento` object; persisted at promotion into a new .NET-owned `fact.DocumentoFactura` (schema `016`). The unified list reads only .NET-owned tables. | (a) `GRANT SELECT` on `DocumentoRecibido` — guts ADR 0003's strongest guarantee; (b) view + ownership chaining — bypasses the DENY covertly, worse than (a); (c) `AdjuntoManual`-only — screen loses its entire reason to exist (ADR 0013 §Contexto) | Symmetric with the already-decided Drive-packaging payload mechanism. Keeps the DENY intact and verifiable. |
| D2 | Byte serving resolves `RutaRelativa` against a configured root (`ApiConnectionOptions.Resolve` pattern), canonicalizes, and asserts containment under the root. `Content-Type` from a stored-MIME allow-list (`application/pdf`, `image/png`, `image/jpeg`); anything else → `application/octet-stream`. Always `X-Content-Type-Options: nosniff` + `Content-Disposition: inline`. | Echo stored MIME verbatim | A DB string reaching `Path.Combine` is a traversal sink; `text/html`/`image/svg+xml` in a same-origin `<iframe>` is stored XSS against the session cookie. |
| D3 | `GET /api/facturas/{id}/asiento` returns the full asiento **with the asiento's own ETag**, plus `GET /api/asientos/{id}`. | Return a bare `asientoContableId` | The screen needs the asiento ETag anyway for line edits; a bare id costs a second round trip and a torn read. |
| D4 | `TipoCambioVenta` exposed on `AsientoRespuesta` **only**, from the already-persisted `AsientoContable.TipoCambioVenta`. Not on `FacturaRespuesta`. | Also expose `Factura.TipoCambioAplicado` | Correction to the proposal: persistence already exists (`SqlUnidadDeTrabajo.cs:53,107,128,136`) — `SqlFacturacionStore` needs **no** change. `FacturaPersistida` lacks the column, and surfacing a *different* column beside the frozen one lets two rates diverge on screen. Correctness > convenience. |
| D5 | SPA extends the existing convention (standalone, `OnPush`, `templateUrl`, service = private `signal` + `asReadonly()`). Adds `withInterceptors` 401 handler + `CanActivate` session guard to `app.config.ts`/`app.routes.ts`. | New app shell | `spa/src` already exists with a working `inbox` feature — the proposal's "empty scaffold" premise is stale. |
| D6 | Conflict UX discriminates on the RFC 9457 `type` URI, not the status code. | Switch on status | `409` is produced by both `CasoConflicto` and Global 3/4 invariants (`ProblemasDeNegocio.cs:92-106`); status alone is ambiguous. |
| D7 | "Guardar avance" sequences writes and threads the returned ETag forward. | Parallel line writes | Every line route CASes `fact.AsientoContable.Version`; concurrent writes 412 each other. |

## Data Flow

    Python ─payload─→ fact.InboxEvent ─promoción─→ fact.DocumentoFactura ┐
                                                   fact.AdjuntoManual  ─┴→ lista unificada
                                                          │
    volumen compartido ←── RutaRelativa ──────────────────┘ → /contenido → <iframe>

    DetallePage (draft signals) ──PATCH/POST/DELETE + If-Match──→ API
          ↑ computed(cuadre)          ←── nuevo ETag ──┘

## File Changes

| File | Action | Description |
|---|---|---|
| `SmartNet/db/schema/016_documento_factura.sql` | Create | `fact.DocumentoFactura` + GRANT to `fact_api`, DENY to `fact_worker` |
| `SmartNet/worker/src/smartnet_worker/payload_inbox.py` | Modify | Four metadata fields in `documento` |
| `SmartNet/inbox/SmartNet.Inbox.Core/EventoInbox.cs` | Modify | Mirror payload fields |
| `SmartNet/inbox/SmartNet.Inbox.Infrastructure/{PayloadInboxParser,SqlPromocionRepository}.cs` | Modify | Parse + insert projection in the promotion transaction |
| `SmartNet/api/SmartNet.Api/DocumentoEndpoints.cs` | Create | Lista unificada + `/contenido` |
| `SmartNet/api/SmartNet.Api/{Asiento,Factura}Endpoints.cs` | Modify | `GET` asiento, factura→asiento, `TipoCambioVenta` |
| `SmartNet/spa/src/app/{app.config.ts,app.routes.ts}` | Modify | Interceptor + guard + `detalle/:id` route |
| `SmartNet/spa/src/app/shared/{auth.guard.ts,http-error.interceptor.ts,problema.model.ts}` | Create | `spa-auth-shell` + RFC 9457 parsing |
| `SmartNet/spa/src/app/detalle/**` | Create | `data-access/` services; `feature/detalle-page` container; `ui/` visor-documento, factura-form, asiento-lineas, conflicto-banner |

## Testing Strategy

| Layer | What | How |
|---|---|---|
| Unit | Path containment, MIME allow-list, `computed()` cuadre, `type`→UX mapping | xUnit / Vitest, no I/O |
| Contract | `fact_api` still cannot SELECT `DocumentoRecibido` | Extend `PermissionMatrixTests` |
| Integration | Endpoints, 401/404/412/422/409 | `SmartNetApiFactory` |
| E2E | Guardar avance, validar, 412 reload | Deferred to apply |

## Threat Matrix

Applicable (file serving over a DB-supplied path).

| Row | Status | Safe behavior | RED test |
|---|---|---|---|
| Path traversal | Applicable | Canonicalized path outside root → 404 | `../` in `RutaRelativa` → 404 |
| MIME confusion / stored XSS | Applicable | Non-allow-listed MIME → `octet-stream` + `nosniff` | `text/html` row not served as HTML |
| Authz | Applicable | Unauthenticated → 401, no bytes | Anonymous `/contenido` |
| Missing file | Applicable | Row without file → 404, path never echoed | Orphan row |
| Shell / subprocess / VCS automation | N/A | No process boundary | — |

## Migration / Rollout

Schema `016` is additive with a rollback script. Documents ingested before `016` have no projection
row: the list degrades to `AdjuntoManual` only. A backfill is impossible under ADR 0003 (the source
is unreadable) — accepted, since production ingestion has not started.

## Open Questions

- [ ] **Scope amendment required**: D1 forces a Python payload change and a schema migration, both
      excluded by the proposal. Owner must accept the wider slice or defer #12.
- [ ] **ADR required**: ADR 0013 Revisión 3 recording that the unified view is a .NET-owned
      projection, not a cross-partition read. Specs `documentos-lista-unificada-api` and
      `documento-contenido-api` must be amended — they currently mandate a forbidden SELECT.
- [ ] Polling interval for the detail screen remains open (ADR 0009 riesgo abierto).
