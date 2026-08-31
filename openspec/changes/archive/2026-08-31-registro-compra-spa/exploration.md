# Exploration: registro-compra-spa (BACKLOG #23)

Read-only "Registro de compra" SPA screen: header list of validated invoices + their asiento,
read-only detail of asiento lines, period filter, visual cabecera↔detalle inconsistency marker.
New read-only `GET` listado endpoint + route + sidebar wiring (`spa-shell-nav` amendment).
No new SQL, no new `GRANT`, no núcleo contable (ADR 0019).

## Current State — API (SmartNetApi, .NET, Minimal API, ADO puro)

- `AsientoEndpoints.cs` (`SmartNet/SmartNetApi/api/SmartNet.Api/`): 7 routes, the only read is
  `GET /api/asientos/{id:long}` → `GetAsientoAsync` → `IFacturacionStore.AbrirAsync` →
  `uow.CargarAsientoAsync(id)` + `uow.CargarLineasPersistidasAsync(id)` → `AsientoRespuesta.De(...)`,
  ETag in header. All `.RequireAuthorization()`.
- `AsientoRespuesta` record (`AsientoEndpoints.cs:242`): `AsientoContableId, Estado, NumeroAsiento,
  ProveedorCodigo, FechaContable, MotivoDescripcion, TipoCambioVenta, BasePEN, IgvPEN, Lineas[]`.
  `LineaRespuesta`: `LineaId, Orden, Bloque, Tipo, Debe, Haber, CuentaCodigo, CuentaDescripcion,
  CtaReflejaCodigo, CtaPuenteCodigo`. Does NOT expose `NumeroComprobante`, `OrigenLibro`, `Glosa`,
  `NetoPEN`, or a proveedor display name.
- Reads today all go through the per-command transaction (`IFacturacionStore` → `IUnidadDeTrabajo`,
  new `SqlConnection`+`SqlTransaction` per call, `SqlFacturacionStore.AbrirAsync`). There is NO
  read-only repository in the facturación module. `CargarLineasPersistidasAsync`
  (`SqlUnidadDeTrabajo.cs:641`) is a plain `SELECT ... FROM fact.AsientoContableDetalle`.
- Closest precedent for a read-only list endpoint = inbox module: `SqlBandejaRepository`
  (`SmartNet/SmartNetApi/inbox/SmartNet.Inbox.Infrastructure/`) — plain `AddSingleton` repo taking
  a connection string, `ListarAsync(FiltrosBandeja, ct)` → `PaginaBandeja<BandejaItem>`,
  `COUNT(*) OVER()` totals, proveedor-name join added by #21.
- `PaginaBandeja<T>` envelope `{ items, pagina, tamanioPagina, totalRegistros, totalPaginas }` is
  the project-standard paginated shape, reused by `catalog-queries-api`.
- `CatalogoEndpoints.cs` = #22 precedent for thin read-only endpoints delegating to a Core
  repository port, no SQL/rule in endpoint (ADR 0019).
- `ServicioDeFacturas` has `private const string OrigenLibro = "02"`; `fact.AsientoContable.OrigenLibro`
  `CHAR(2) NOT NULL DEFAULT '02'`.

## Current State — Data (SmartNetBD, schema `fact`)

- `fact.AsientoContable` (`005_negocio.sql:108`) already carries every cabecera field:
  `NumeroComprobante, NumeroAsiento, OrigenLibro (DEFAULT '02'), ProveedorCodigo, Glosa,
  FechaContable (DATE), TipoCambioVenta DECIMAL(12,6), BasePEN, IgvPEN, NetoPEN DECIMAL(18,2),
  MotivoDescripcion, Estado (BORRADOR|CONFIRMADO|ANULADO), Version ROWVERSION`.
- `fact.AsientoContableDetalle`: `LineaId, Orden, Bloque (PRINCIPAL|DESTINO), Tipo (D|H), Debe,
  Haber, CuentaCodigo, CuentaDescripcion`. FK `AsientoContableId` → `fact.AsientoContable` →
  `fact.Factura`. `UQ_Asiento_Vigente` = at most one non-ANULADO asiento per factura.
  `fact.Factura.Estado IN ('PENDIENTE_VALIDACION','VALIDADA','DESCARTADA')`.
- GRANT check (`008_usuarios_y_permisos.sql`): `fact_api` (role of `usr_api`) has
  `GRANT SELECT,INSERT,UPDATE` on `fact.AsientoContable`, `fact.AsientoContableDetalle`,
  `fact.Factura`, and `GRANT SELECT` on `dbo.Proveedor`. So a listado joining
  `AsientoContable + AsientoContableDetalle + Factura + dbo.Proveedor` needs NO new GRANT and
  NO new versioned SQL — matches BACKLOG #23.

## Current State — SPA (SmartNetWeb, Angular signals, no state lib)

- `catalogos/` feature (#22) is the exact template: `feature/*-page` (container, OnPush, owns
  filter/sort/paging signals), `ui/*` (presentational `input()`/`output()`),
  `data-access/*.service.ts` (`providedIn:'root'`, private writable signal + `asReadonly()`,
  `firstValueFrom(http.get)`, `cargando`/`error` signals), `models/*.model.ts`.
- Reusable shared UI from #22: `ui/tabla-paginador`, `ui/orden.ts`
  (`alternarOrden`/`ordenarPor`/`EstadoOrden`), `ui/boton-exportar`, `data-access/descarga-xlsx.ts`.
- `plan-contable-page.ts` = fetch-once + client-side filter/sort/paginate via `computed()`.
  `proveedores-page` = server-side paginated consuming `PaginaBandeja<T>`. `tipo-cambio-page` =
  date-range filter with month-to-date local-date defaults + 400 handling.
- `app.routes.ts`: `catalogos/*` are lazy `loadComponent` children of the `ShellLayout` route,
  each `canActivate: [authGuard]`. `inbox/` has the row→detail nav precedent (`detalle/:id`).

## Current State — Shell nav (`openspec/specs/spa-shell-nav/spec.md`)

8 destinations; 5 routed links; `Registro de compra`, `Errores y notificaciones`, `Sincronización`
render inert (`aria-disabled="true"`, title "Disponible próximamente"). `sidebar.spec.ts` asserts
exact list + exactly-five-links + 8 hand-built glyphs. #22 moved Proveedores/Plan contable
inert→routed by editing "Requirement: Sidebar mirrors the handoff navigation". #23 amendment: move
`Registro de compra` into routed set (→ 6 links, 2 inert), update 2 scenarios + the
`sidebar.spec.ts` scenario. Glyph already exists.

## Current State — Inconsistency check (`handoff/DESIGN_BRIEF.md` §4 + REGLAS.md)

DESIGN_BRIEF §4: "Señala visualmente inconsistencias entre cabecera y detalle (ej. base imponible +
IGV no cuadra con el neto)." #23 narrows the brief to consulta only — NO editar/anular/reactivar
(those stay in #12). REGLAS.md §5/§10: gravada factura identity — cargos 6x/1x = base imponible,
cargo 401111 = IGV, abono proveedor = total + percepción; sum of PRINCIPAL lines must equal base
imponible. `fact.AsientoContable` persists `BasePEN`/`IgvPEN`/`NetoPEN` as frozen header amounts.
⇒ The marker is a PURE presentation check computed from data already returned (header
`BasePEN+IgvPEN` vs `NetoPEN`, and/or vs summed detalle `Debe`/`Haber`). Does NOT need a domain
rule, does NOT touch `SmartNet.Contable.Core` / `nucleo-contable` (ADR 0019 preserved). Propose
must pin: exact formula, whether percepción participates, rounding/tolerance (`DECIMAL(18,2)`).

## Affected Areas

- `SmartNet/SmartNetApi/api/SmartNet.Api/AsientoEndpoints.cs` — new list route + response records.
- `SmartNet/SmartNetApi/facturacion/SmartNet.Facturacion.Core/` — new read-only port
  (e.g. `IRegistroCompraRepository`) mirroring inbox `SqlBandejaRepository`; PurityScanTests-guarded.
- `SmartNet/SmartNetApi/facturacion/SmartNet.Facturacion.Infrastructure/` —
  `SqlRegistroCompraRepository`: `SELECT` join `fact.AsientoContable + fact.Factura + dbo.Proveedor`
  (+ detalle), `COUNT(*) OVER()`, period filter on `FechaContable`, `AddSingleton` DI.
- `SmartNet/SmartNetApi/api/SmartNet.Api/Program.cs` — DI wiring.
- `SmartNet.Api.Tests` — `CatalogoEndpointsTests`-style contract tests (real DB, real cookie):
  401, camelCase, period filter, pagination envelope, empty period.
- `SmartNet/SmartNetWeb/src/app/registro-compra/` (new feature): `feature/registro-compra-page`,
  `ui/registro-compra-tabla` (+ inconsistency marker), `ui/asiento-detalle` (read-only lines),
  `data-access/registro-compra.service.ts`, `models/registro-compra.model.ts`.
- `SmartNet/SmartNetWeb/src/app/app.routes.ts` — new lazy child route + `authGuard`;
  `app.routes.spec.ts` stays additive.
- `SmartNet/SmartNetWeb/src/app/shared/shell-layout/` + sidebar + `sidebar.spec.ts` —
  `Registro de compra` inert → routed link.
- `openspec/specs/spa-shell-nav/spec.md` — MODIFIED "Sidebar mirrors the handoff navigation".
- New capability specs: `registro-compra-api` + `registro-compra-spa` (or extend `api-asientos` +
  `factura-respuesta`/`asiento-respuesta`).
- Harness `integration-spa-api` README — new flow recorded manually (#22 precedent).

## Approaches

**A. New dedicated read-only repository in facturación (inbox `SqlBandejaRepository` pattern).**
New `IRegistroCompraRepository` Core port + `SqlRegistroCompraRepository` infra adapter,
`AddSingleton`, returns `PaginaBandeja<RegistroCompraCabecera>` + per-asiento lines. Pros: matches
established read-list precedent (#13, #22); leaves transactional `IUnidadDeTrabajo` untouched;
clean ADR 0019 story; no risk to write paths. Cons: one more repo type + DI; detalle read
duplicates `CargarLineasPersistidasAsync` SELECT (acceptable). Effort: Medium. **RECOMMENDED.**

**B. Add `ListarRegistroAsync` to `IUnidadDeTrabajo`.** Pros: reuses seam. Cons: `IUnidadDeTrabajo`
is explicitly a per-command WRITE transaction; browse query does not belong there; opens a tx per
list request; 25+ implementors/fakes to update. Contradicts module design intent. Effort: Medium
(worse fit).

**C. Sub-decision:** extend `api-asientos` `GET /api/asientos?periodo=` vs a new `registro-compra`
capability + `/api/registro-compra` route. Lean to NEW `registro-compra-api` capability: it is a
reporting projection (libro de compras), not the editable ADR 0008 aggregate.

## Recommendation

Approach A + new `registro-compra-api` capability. Dedicated read-only `IRegistroCompraRepository`
(Core port, PurityScan-guarded) + `SqlRegistroCompraRepository` adapter joining
`fact.AsientoContable + fact.Factura + dbo.Proveedor`, returning `PaginaBandeja<T>` filtered by
accounting period over `FechaContable`; thin `GET /api/registro-compra` list route + line detail
(embed per row, or new `GET /api/registro-compra/{asientoId}`, or reuse `GET /api/asientos/{id}`
after adding missing cabecera fields). SPA: new `registro-compra/` feature copied from `catalogos/`
structure, server-side period filter (month-to-date local-date default like `tipo-cambio-page`),
row→expand read-only line detail, computed inconsistency badge from returned amounts. Amend
`spa-shell-nav` to route `Registro de compra`. Zero SQL/GRANT/núcleo changes.

## Risks / Open Questions for sdd-propose

1. "Facturas validadas": predicate = `fact.Factura.Estado='VALIDADA'` vs
   `fact.AsientoContable.Estado='CONFIRMADO'` vs both? Exclude `ANULADO` asientos?
2. Period filter semantics: `periodo=YYYY-MM` single param vs `desde`/`hasta` range; over
   `FechaContable`; default current month-to-date (local date not UTC); required or optional?
3. "Estado del asiento" column: if only validated invoices, is it always `CONFIRMADO`? Is the
   column even variable?
4. Pagination: server-side `PaginaBandeja<T>` (recommended, ~200–1000 rows/month) vs fetch-all
   client-side.
5. `origen del libro 02`: surface `fact.AsientoContable.OrigenLibro` verbatim (not hard-code '02');
   can any non-'02' value exist today?
6. Proveedor column: name via join `dbo.Proveedor` on `ProveedorCodigo` (LEFT JOIN, handle P00000
   Varios and missing rows) vs code vs both.
7. Inconsistency formula: `round(BasePEN+IgvPEN,2) != round(NetoPEN,2)`? Does percepción
   participate (REGLAS §10.4)? Or header vs sum of detalle `Debe(PRINCIPAL)`/`Haber(DESTINO)`?
   Tolerance exact-to-cent or epsilon? Pin against REGLAS.md §5/§7/§10 — stays a display
   computation.
8. Line detail delivery: embed `lineas[]` per row vs lazy `GET /api/registro-compra/{asientoId}`
   vs reuse `GET /api/asientos/{id}` (needs `NumeroComprobante/OrigenLibro/Glosa/NetoPEN` added to
   `AsientoRespuesta` → MODIFIED `api-asientos` with the "field addition does not break existing
   consumers" scenario).
9. Empty states: period with no asientos; asiento with zero detalle lines; proveedor not found.
10. Capability naming: new `registro-compra-api` vs extend `api-asientos`; confirm whether
    `GET /api/asientos/{id}` response gains missing cabecera fields.
11. Excel export: #22 added "Exportar a Excel" to every catalog screen (owner scope expansion).
    #23 BACKLOG text does not mention it — confirm with owner whether libro de compras needs
    `.xlsx` export (ADR 0021 path exists).
12. Review budget: API repo+endpoint+tests + full new SPA feature + shell-nav amendment likely
    > 400 changed lines — `sdd-tasks` should forecast chained PRs (API slice / SPA slice /
    shell-nav slice).

## Ready for Proposal: YES

All cabecera/detalle data already exists in `fact.*` with existing `usr_api` SELECT grants; clear
SPA + endpoint precedents from #22/#13; known `spa-shell-nav` amendment pattern from #22. Open
questions are refinement, not blockers.
