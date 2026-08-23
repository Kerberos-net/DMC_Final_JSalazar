# Tasks: Detalle y validación de factura (BACKLOG #12)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | 1400–2000 |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR1 → PR2 → PR3 → PR4 → PR5 → PR6 |
| Delivery strategy | ask-on-risk |
| Chain strategy | feature-branch-chain |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: feature-branch-chain
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | Schema 016 + Python payload + EventoInbox mirror | PR1 (base: tracker) | `pytest SmartNet/worker/tests/test_payload_inbox.py`; `dotnet test SmartNet.Inbox.Core.Tests` | Apply 016 against local SQL Server instance | Drop schema 016, revert payload/EventoInbox diffs |
| 2 | Promoción persistence to fact.DocumentoFactura | PR2 (base: PR1) | `dotnet test SmartNet.Inbox.Infrastructure.Tests` | Run promoción against seeded InboxEvent fixture | Revert SqlPromocionRepository/PayloadInboxParser diff |
| 3 | .NET read endpoints (contenido, lista unificada, asiento lectura, TipoCambioVenta) | PR3 (base: PR2) | `dotnet test SmartNet.Api.Tests` | `SmartNetApiFactory`-backed integration run | Remove DocumentoEndpoints.cs, revert Asiento/FacturaEndpoints diffs |
| 4 | SPA auth shell (guard, interceptor, cookie) | PR4 (base: PR3) | `ng test --include='**/auth.guard.spec.ts' --include='**/http-error.interceptor.spec.ts'` | `ng serve` manual 401 redirect check | Remove shared/auth.guard.ts, http-error.interceptor.ts, revert app.config/routes |
| 5 | SPA detalle feature (data-access/feature/ui) | PR5 (base: PR4) | `ng test --include='**/detalle/**/*.spec.ts'` | `ng serve` manual guardar/validar/412 flow | Remove `spa/src/app/detalle/**` |
| 6 | ADR 0013 Rev.3 + spec amendments | PR6 (base: PR5) | N/A (docs) | N/A — documentation only | Revert ADR file |

## Phase 1: Schema & Ingestion Payload Foundation

- [x] 1.1 RED: extend PermissionMatrixTests asserting `fact_api` can SELECT/INSERT `fact.DocumentoFactura`, `fact_worker` is DENIED, `DocumentoRecibido` DENY unchanged
- [x] 1.2 GREEN: CREATE `SmartNet/db/schema/016_documento_factura.sql` — `fact.DocumentoFactura` + GRANT `fact_api` + DENY `fact_worker` + rollback script
- [x] 1.3 RED: `payload_inbox` test asserting `documento` object carries `nombreArchivo`, `mimeType`, `rutaRelativa`, `tamanoBytes`
- [x] 1.4 GREEN: MODIFY `SmartNet/worker/src/smartnet_worker/payload_inbox.py` — add the four metadata fields
- [x] 1.5 RED: `EventoInbox` deserialization test for the new `documento` fields
- [x] 1.6 GREEN: MODIFY `SmartNet/inbox/SmartNet.Inbox.Core/EventoInbox.cs` — mirror payload fields

## Phase 2: Promoción Persistence

- [x] 2.1 RED: `SqlPromocionRepository` test — promoción inserts one `fact.DocumentoFactura` row with mapped metadata
- [x] 2.2 GREEN: MODIFY `PayloadInboxParser.cs` + `SqlPromocionRepository.cs` — parse and persist projection row
- [x] 2.3 RED: test asserting promoción issues no SELECT against `fact.DocumentoRecibido` (ADR 0003 symmetry)
- [x] 2.4 REFACTOR: confirm no cross-partition read path; tidy repository mapping

## Phase 3: .NET Read Endpoints — COMPLETE (11/11, PR3)

- [x] 3.1 RED: threat-matrix — `../` in `RutaRelativa` → 404 (path traversal)
- [x] 3.2 RED: threat-matrix — non-allow-listed MIME → `application/octet-stream` + `X-Content-Type-Options: nosniff`
- [x] 3.3 RED: threat-matrix — unauthenticated `GET /contenido` → 401, no bytes
- [x] 3.4 RED: threat-matrix — orphan row (missing file) → 404, path never echoed
- [x] 3.5 GREEN: CREATE `SmartNet/api/SmartNet.Api/DocumentoEndpoints.cs` — `GET /api/documentos/{id}/contenido` (path containment, MIME allow-list, nosniff, inline disposition — D2)
- [x] 3.6 RED: lista unificada — merges `fact.DocumentoFactura` + `AdjuntoManual`, no duplicates; empty ≠ error; pre-016 factura degrades to `AdjuntoManual`-only
- [x] 3.7 GREEN: extend `DocumentoEndpoints.cs` — unified list, strictly read-only over .NET-owned tables
- [x] 3.8 RED: `GET /api/asientos/{id}` returns body+ETag; unknown id → 404; factura→asiento distinguishes "no vigente asiento" from unknown-factura 404
- [x] 3.9 GREEN: MODIFY `AsientoEndpoints.cs` — add `GET /api/asientos/{id}` + factura→asiento resolution (D3)
- [x] 3.10 RED: `TipoCambioVenta` on `AsientoRespuesta` equals frozen `TipoCambioCongelado.Venta`; PEN-only → null; #11 fields unchanged
- [x] 3.11 GREEN: MODIFY `AsientoEndpoints.cs` response mapping — add `TipoCambioVenta` (D4, no store change)

## Phase 4: SPA Auth Shell — COMPLETE (5/5, PR4)

- [x] 4.1 RED: guard unit test — unauthenticated navigation redirected, authenticated allowed
- [x] 4.2 GREEN: CREATE `SmartNet/spa/src/app/shared/auth.guard.ts`
- [x] 4.3 RED: interceptor unit test — 401 response clears session/redirects, no body leak
- [x] 4.4 GREEN: CREATE `SmartNet/spa/src/app/shared/http-error.interceptor.ts` + `problema.model.ts`
- [x] 4.5 GREEN: MODIFY `app.config.ts` / `app.routes.ts` — wire interceptor + guard on `bandeja`. `detalle/:id` route deferred to PR5 (Phase 5, out of this batch's scope) since it would import a component that does not exist yet.

## Phase 5: SPA Detalle Feature — COMPLETE (16/16, PR5)

- [x] 5.0 GREEN (gap closure, not in original scope): `AsientoRespuesta` never exposed `Lineas` (Phase 3 gap) — added `LineaRespuesta`/`Lineas` to `AsientoEndpoints.cs`'s `AsientoRespuesta`, wired via `IUnidadDeTrabajo.CargarLineasPersistidasAsync` in `GetAsientoAsync`, `ResponderConAsientoActualizadoAsync`, and `FacturaEndpoints.GetAsientoDeFacturaAsync` — Phase 5 cannot edit líneas by `LineaId` without it
- [x] 5.1 RED: data-access service tests (factura/asiento/documento clients) — request shape, `If-Match` header, ETag propagation
- [x] 5.2 GREEN: CREATE `SmartNet/spa/src/app/detalle/data-access/**` services (signals, `providedIn: 'root'`), extending `inbox` pattern
- [x] 5.3 RED: `computed()` cuadre recompute on línea edit; type→UX mapping test (412 vs 422 vs 409 — D6)
- [x] 5.4 GREEN: CREATE `SmartNet/spa/src/app/detalle/feature/detalle-page` — side-by-side layout, guardar avance, validar orchestration (dual-ETag sequencing — D7)
- [x] 5.5 GREEN: CREATE `SmartNet/spa/src/app/detalle/ui/{visor-documento,factura-form,asiento-lineas,conflicto-banner}` — inline edit, delete-with-confirm, same-origin iframe viewer, conflict banner (D6)
- [x] 5.6 RED→GREEN: delete-confirm cancel leaves state unchanged; 412 → reload discards local edits; 422/409 → edits kept, inline/banner errors shown
- [x] 5.7 RED→GREEN (owner-added task, PR5): CREATE `SmartNet/spa/src/app/login/feature/login-page` — `/login` form (`NombreUsuario`/`Clave`), calls `POST /api/sesion` via `SessionService.iniciarSesion()`, on success navigates to `?returnUrl=` or `/bandeja`, on 401 shows the `ProblemaDetails.detail`; extended `SessionService`, `authGuard` (now carries `returnUrl`), and `httpErrorInterceptor` (exempts the login POST's own 401 from the global session-expiry handling) to support it

## Phase 6: Documentation — COMPLETE (2/2, PR6)

- [x] 6.1 Write ADR 0013 Revisión 3 — `fact.DocumentoFactura` is a .NET-owned projection, not a cross-partition read
- [x] 6.2 Amend `documentos-lista-unificada-api` / `documento-contenido-api` specs if residual language implies a forbidden SELECT

## Phase 7: Verification — 1/2 (PR6; 7.2 explicitly deferred to the user, see below)

- [x] 7.1 Run full suite (.NET, Python, SPA) — PermissionMatrixTests + all threat-matrix RED tests green
- [ ] 7.2 Manual/E2E smoke — guardar avance → validar → 412 reload flow (cannot be executed by the apply agent — no browser available; explicit pending steps documented in apply-progress for the user to run)
