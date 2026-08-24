# Tasks: Diseño visual para SPA — login y detalle-validación

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~950–1250 (PR1 ~350–450 backend+SQL; PR2 ~600–800 tokens+theme+6 components) |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 (backend read API) → PR 2 (SPA tokens/theme/visual) |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | Audit index + `IAuditoriaRepository` read slice + `GET /historial` | PR 1 | `dotnet test --filter FullyQualifiedName~Auditoria` | `dotnet run` + `curl -H Authorization ... /api/facturas/{id}/historial` | `DROP INDEX` (`rollback/017_down.sql`) + delete new files, no data change |
| 2 | Widen `FacturaPersistida`/`CargarFacturaAsync`/`FacturaRespuesta` + `POST /confirmar-afectacion` | PR 1 | `dotnet test --filter FullyQualifiedName~FacturaEndpoints\|SqlUnidadDeTrabajo` | `dotnet run` + manual `PATCH`/`POST` against a seeded factura | Revert commit; trailing-default params keep old call sites compiling |
| 3 | Tokens + theme (`styles.css`, `TemaService`, `contraste.ts`, `main.ts`, `app.html/css`) | PR 2 | `ng test --include='**/tema.service.spec.ts' --include='**/contraste.spec.ts'` | `ng serve` + toggle theme manually | Revert commit; no component wiring depends on theme yet |
| 4 | `login-page` + `detalle-page` + `factura-form` + `asiento-lineas` + `visor-documento` + `conflicto-banner` styles + `historial-correccion` component + data wiring | PR 2 | `ng test` (full) | `ng serve` + `ng build --configuration production` (budget check) | Revert commit; pure CSS/presentational, functional logic from #12 untouched |

## Phase 1: Backend — Audit read slice (PR 1)

- [x] 1.1 Create `SmartNet/db/schema/017_indice_auditoria_por_entidad.sql` — `CREATE INDEX IX_AuditoriaCorreccion_Entidad ON fact.AuditoriaCorreccion (EntidadTipo, EntidadId) INCLUDE (...)` (D8)
- [x] 1.2 Create `SmartNet/db/schema/rollback/017_down.sql` — `DROP INDEX`
- [x] 1.3 RED: add `SqlAuditoriaRepositoryTests` (frontera fixture) asserting FACTURA+ASIENTO(incl. ANULADO)+ADJUNTO union, `ORDER BY OcurridoEn DESC`, unauthenticated/parameterized SQL only
- [x] 1.4 Create `SmartNet.Facturacion.Core/IAuditoriaRepository.cs` — `ListarPorFacturaAsync(long facturaId, CancellationToken ct)` (D7)
- [x] 1.5 GREEN: create `SmartNet.Facturacion.Infrastructure/SqlAuditoriaRepository.cs` implementing the D7 union SQL, own `SqlConnection`, no transaction
- [x] 1.6 RED: add `AuditoriaEndpointsTests` — unauthenticated → 401; unknown id → `200 []`; known id → entries newest-first
- [x] 1.7 GREEN: create `SmartNet.Api/AuditoriaEndpoints.cs` — `GET /api/facturas/{id:long}/historial`, `.RequireAuthorization()`, returns `EntradaAuditoriaRespuesta[]`
- [x] 1.8 Register `IAuditoriaRepository` → `SqlAuditoriaRepository` DI in `SmartNet.Api/Program.cs`

## Phase 2: Backend — Indicator projection + confirmación (PR 1)

- [x] 2.1 RED: extend `FakeUnidadDeTrabajo`/`SqlUnidadDeTrabajoTests` fixture — round-trip 4 new columns, confirm a `PATCH`/`GuardarFacturaAsync` does not clobber them
- [x] 2.2 GREEN: add 4 **trailing** default params (`EsProveedorGenerico=false`, `PosibleDuplicado=false`, `TieneCamposNoExtraidos=false`, `AfectacionMixta=null`) to `SmartNet.Facturacion.Core/FacturaPersistida.cs`
- [x] 2.3 GREEN: widen `CargarFacturaAsync`'s SELECT in `SmartNet.Facturacion.Infrastructure/SqlUnidadDeTrabajo.cs` to read the 4 columns; leave `UPDATE` untouched
- [x] 2.4 RED: `FacturaEndpointsTests` — detail response includes the 4 indicator fields with values matching `GET /api/bandeja` parity for the same row
- [x] 2.5 GREEN: extend `FacturaRespuesta` (`SmartNet.Api/FacturaEndpoints.cs`) with the 4 fields, additive projection in `FacturaRespuesta.De`
- [x] 2.6 RED: `FacturaEndpointsTests` — `POST /api/facturas/{id}/confirmar-afectacion` unauthenticated → 401; CAS mismatch (`IfMatch.Requerido`) → 412; success → `AfectacionMixta` set, `RegistrarAuditoriaAsync(CONFIRMACION_AFECTACION)` invoked, `CommitAsync` called
- [x] 2.7 GREEN: implement `POST /api/facturas/{id}/confirmar-afectacion` in `FacturaEndpoints.cs`, same shape as `abrir`/`validar`/`descartar` (D10 — gate stays dormant, do NOT wire `HechosDeConflicto.AfectacionNoVerificada`)
- [x] 2.8 Run `PermissionMatrixTests`/`SchemaShapeTests` unmodified — confirm no permission drift from the new index/columns

## Phase 3: SPA — Tokens and theme (PR 2)

- [x] 3.1 RED: create `spa/src/app/shared/tema.service.spec.ts` — `resolverTema` per D1 truth table; invalid `localStorage` value → `'sistema'`; stub `matchMedia` (jsdom gotcha)
- [x] 3.2 RED: create `spa/src/app/shared/contraste.spec.ts` — asserts every documented token pair (light + dark) meets its 4.5:1/3:1 floor from design.md's palette table
- [x] 3.3 GREEN: create `spa/src/app/shared/contraste.ts` — pure WCAG 2.x `contraste(hexA, hexB)` function
- [x] 3.4 GREEN: create `spa/src/app/shared/tema.service.ts` — signal-based `TemaService`, `resolverTema()`, `aplicarTemaInicial()`, writes `document.documentElement.dataset.tema`
- [x] 3.5 Wire `aplicarTemaInicial()` in `spa/src/main.ts` before `bootstrapApplication` (no-flash requirement)
- [x] 3.6 Rewrite `spa/src/styles.css` with `@layer tokens, base, primitives` — both theme token blocks (hex values from design.md), `color-scheme`, shared primitives (`.btn`, `.campo`, `.chip`, `.tabla`, `.alerta`, `.panel`, `.banner`)
- [x] 3.7 Update `spa/src/index.html` — `lang="es"`, real `<title>`
- [x] 3.8 Update `spa/src/app/app.html`/`app.css` — minimal shell header with native `<select>` theme control bound to `TemaService`
- [x] 3.9 RED+GREEN: `localStorage` tampered-value test in `tema.service.spec.ts` — confirms fallback to `'sistema'` (threat-matrix client-input-trust row)

## Phase 4: SPA — Component visuals and data wiring (PR 2)

- [x] 4.1 Apply `styleUrl` CSS to `login-page.{ts,html,css}` — centered card, 401 message uses `.alerta`
- [x] 4.2 Create `spa/.../detalle/models/historial.model.ts` mirroring `EntradaAuditoriaRespuesta`; extend `factura.model.ts` with the 4 indicator fields
- [x] 4.3 RED: create `spa/.../detalle/data-access/historial.service.spec.ts` (ADR 0009 signals pattern)
- [x] 4.4 GREEN: create `spa/.../detalle/data-access/historial.service.ts` calling `GET /api/facturas/{id}/historial`
- [x] 4.5 Create presentational `spa/.../detalle/ui/historial-correccion/*` — native `<details>`/`<summary>` panel, closed by default, no history → non-error empty state
- [x] 4.6 RED: component test — `.alerta--bloqueante` iff `posibleDuplicado || esProveedorGenerico`; `.alerta--informativa` iff `tieneCamposNoExtraidos || afectacionMixta === null`
- [x] 4.7 GREEN: apply `styleUrl` + indicator bindings to `factura-form.{ts,html,css}` — estado chip, P00000/duplicado → bloqueante, campos no extraídos → informativo
- [x] 4.8 RED+GREEN: add afectación-confirmation control to `factura-form` — visible iff `AfectacionMixta === null`; confirm action calls `POST /confirmar-afectacion`
- [x] 4.9 Apply `styleUrl` to `asiento-lineas.{ts,html,css}` — tabular alignment (`tabular-nums`), wire `historial-correccion` panel
- [x] 4.10 Apply `styleUrl` to `visor-documento.{ts,html,css}` — iframe fill, native `<select>` follows theme via `color-scheme`
- [x] 4.11 RED: component test — `.banner--conflicto` (412, violeta) vs `.banner--error` (422, rojo), distinct icon shape
- [x] 4.12 GREEN: apply `styleUrl` to `conflicto-banner.{ts,html,css}` per D3/threat-matrix redundancy (placement + shape + label, color never sole carrier)
- [x] 4.13 Wire `detalle-page.{ts,html,css}` — fetch historial via `historial.service.ts`, pass `[historial]`/indicators down; 2-col grid, sticky visor, collapses <1100px
- [x] 4.14 Review `spa/angular.json` `anyComponentStyle` budgets; run `ng build --configuration production` and confirm no component CSS exceeds 8kB (D6)

## Phase 5: Cross-cutting verification

- [ ] 5.1 Run `dotnet test` full suite (Core + Infrastructure + Api) — confirm no regression in existing `FacturaEndpointsTests`/`SqlUnidadDeTrabajoTests`
- [x] 5.2 Run `ng test` full suite — confirm no regression in existing detalle/login specs (140/140 pass)
- [ ] 5.3 Manual E2E smoke: login → detalle with real audit history + indicators, light/dark toggle, 412 vs 422 distinction, confirmar-afectación round-trip
- [ ] 5.4 Confirm `PermissionMatrixTests` and `SchemaShapeTests` pass unmodified (no privilege/schema drift)
