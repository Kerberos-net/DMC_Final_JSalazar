# Design: Bandeja e incidencias (BACKLOG #13)

## Technical Approach

Widen `GET /api/bandeja` in place. `fact.InboxEvent.ProcesamientoId` is `NOT NULL` (`006_contratos.sql`),
so every bandeja row already carries the key that `fact.ProcesamientoError` and
`fact.CommandQueue.Referencia` are indexed by — no new table, no new endpoint, no client-side merge.
`origen` is derived by a pure function in `SmartNet.Inbox.Core` (ADR 0019, same shape as
`CalculoDeIndicadores`); the repository projects, the endpoint sequences.

## BLOCKING precondition — permission matrix

`008_usuarios_y_permisos.sql:85` has `DENY SELECT ... ON fact.ProcesamientoError TO fact_api`.
DENY beats GRANT: **.NET cannot read the error table today**. The proposal/exploration premise
(".NET may only READ it") is false against the DDL. No ratified scenario in
`openspec/specs/esquema-y-permisos/spec.md` requires this DENY (only `fact.Procesamiento` and
`fact.DatosExtraidos` are asserted); it came from 008's defence-in-depth widening. Resolution
requires an **ADR 0003 amendment** reclassifying `fact.ProcesamientoError` from class 1 (Privada)
to asymmetric read (Python writes, both read) — the precedent `fact.Configuracion` already sets.
Apply MUST NOT start until the owner ratifies it.

## Architecture Decisions

| # | Decision | Chosen | Rejected | Rationale |
|---|---|---|---|---|
| D1 | Read channel for errors | New `018_permiso_lectura_procesamiento_error.sql`: `REVOKE` the DENY, `GRANT SELECT`, keep explicit `DENY INSERT, UPDATE, DELETE` | View with ownership chaining; Python-published contract table | The view silently defeats a deliberate DENY (dishonest); a new table needs Python work + a bigger ADR change. The grant is one line and keeps the write boundary engine-enforced |
| D2 | `origen` shape | Flat record, nullable fields, `origen` string discriminator | C# polymorphic union / conditional properties | Matches today's `BandejaItem` (already nullable-by-state). TS still narrows via a discriminated union over the same flat wire — no custom converter on either side |
| D3 | Errors per row | Second result set in the same `SqlCommand` batch, keyed by a `@pagina` table variable | `LEFT JOIN` + collapse in the reader; `FOR JSON PATH` subquery | A JOIN multiplies rows and breaks `OFFSET/FETCH` (it would page over errors, not over bandeja rows). `FOR JSON` avoids that but moves parsing into the repository. The batch keeps one round-trip, zero duplication, ≤20 parent keys |
| D4 | Pagination | `ORDER BY ie.CreadoEn <dir>, ie.InboxEventId <dir> OFFSET/FETCH` + `COUNT(*) OVER()` | Keyset/seek pagination; separate COUNT always | Tiebreaker is mandatory — `CreadoEn` is `DATETIME2(3)`, non-unique, and OFFSET over a non-deterministic order drops/repeats rows. `COUNT(*) OVER()` costs no extra scan. Only when the page is empty **and** `pagina > 1` does one fallback `COUNT(*)` run, so the envelope never lies about `totalRegistros` |
| D5 | 5-minute window | Server computes `reprocesarDisponibleEn` (UTC) from `fact.CommandQueue`; `null` = enabled | Client compares `OcurridoEn` to `Date.now()` | The client must not derive a business rule from its own clock (ADR 0019 principle). The `5` lives in `PoliticaDeReprocesamiento.VentanaBloqueo` in Core and is passed as `@ventanaMinutos` — testable, not buried in a SQL string |
| D6 | Confirmation dialog | New dumb `ui/confirmar-reproceso/` over native `<dialog>` | Angular CDK/Material; `window.confirm` | `SmartNet/spa/package.json` has neither CDK nor Material; adding one for a single dialog is disproportionate. `window.confirm` is untestable under jsdom/vitest |
| D7 | `proveedor` semantics | Identity match: `f.ProveedorCodigo` / `f.RucProveedor`, falling back to `JSON_VALUE(ie.Payload,'$.comprobante.rucProveedor')` for non-promoted rows | Join `fact.DatosExtraidos`; name `LIKE` search | `DatosExtraidos` is DENY'd to `fact_api` and that DENY **is** ratified — it stays. `InboxEvent.Payload` is a contract table `fact_api` already reads and carries the same RUC (`inbox_event_payload.golden.json`) |
| D8 | Error panel placement | New dumb `ui/panel-errores/`, embedded by `inbox-list` inside a native `<details>` | Expansion state as a signal in `inbox-list` | `<details>` keeps `inbox-list` stateless and OnPush-safe; rendering a panel is not an "action", so decision 8 (one new action only) holds |

## Data Flow

    inbox-filter ──(outputs)──→ inbox-page (signals: estado/desde/hasta/proveedor/orden/pagina)
                                     │  effect() ─→ InboxService.cargar(filtros)
                                     │                    │ GET /api/bandeja?...
                                     ▼                    ▼
    confirmar-reproceso ←─── inbox-list ──→ panel-errores   BandejaEndpoints → IBandejaRepository
              │                                                      │
              └── confirmar ─→ inbox-page ─→ InboxService.reprocesar(procesamientoId)
                                             POST /api/incidencias/{id}/reprocesar (ya existe, #11)
                                             └─→ cargar(ultimosFiltros)   [refetch: server re-derives
                                                                            reprocesarDisponibleEn]

SQL (one command, three statements, two result sets):

    INSERT @pagina  ← filtered + ordered + OFFSET/FETCH over fact.InboxEvent
                       LEFT JOIN fact.Factura (indicadores, proveedor)
                       + COUNT(*) OVER() as TotalRegistros
    SELECT ... FROM @pagina p JOIN fact.InboxEvent/Factura      -- result set 1 (≤20 rows)
    SELECT ... FROM fact.ProcesamientoError e
      JOIN @pagina p ON p.ProcesamientoId = e.ProcesamientoId
      ORDER BY e.OcurridoEn DESC                                -- result set 2

## File Changes

| File | Action | Description |
|------|--------|-------------|
| `SmartNet/db/schema/018_permiso_lectura_procesamiento_error.sql` | Create | D1: revoke DENY, `GRANT SELECT`, re-`DENY INSERT/UPDATE/DELETE`. Recommended in the same script: `IX_ProcesamientoError_ProcesamientoId`, `IX_InboxEvent_CreadoEn (InboxEventId)`, `IX_CommandQueue_Referencia (Tipo, Estado)` — none exist today |
| `adrs/0003-particion-de-propiedad-de-datos-entre-net-y-python.md` | Modify | Reclassify `ProcesamientoError` (needs owner ratification) |
| `openspec/specs/esquema-y-permisos/spec.md` | Modify | New scenario: `usr_api` can SELECT but not write `fact.ProcesamientoError` |
| `SmartNet/db/runner/.../PermissionMatrixTests.cs` | Modify | RED test for the scenario above; existing DENY tests untouched |
| `SmartNet.Inbox.Core/IBandejaRepository.cs` | Modify | `BandejaItem` widened; new `ErrorProcesamiento`, `PaginaBandeja<T>`, `FiltrosBandeja`; `ListarAsync(FiltrosBandeja, ct)` |
| `SmartNet.Inbox.Core/OrigenBandeja.cs` | Create | Pure derivation + `PoliticaDeReprocesamiento.VentanaBloqueo` (ADR 0019, no DB/clock) |
| `SmartNet.Inbox.Infrastructure/SqlBandejaRepository.cs` | Modify | D3/D4/D5/D7 batch query |
| `SmartNet.Api/BandejaEndpoints.cs` | Modify | Bind + validate the new params; stays a thin delegator |
| `spa/.../models/bandeja-item.model.ts` | Modify | Discriminated union + `PaginaBandeja` envelope |
| `spa/.../data-access/inbox.service.ts` | Modify | Object-arg `cargar()`, envelope signals, `reprocesar()`, cached `ultimosFiltros` |
| `spa/.../feature/inbox-page/inbox-page.{ts,html}` | Modify | New filter signals, `pagina`, confirmation state, `reprocesandoId` |
| `spa/.../ui/inbox-filter/` | Modify | Date range + proveedor (emits on `change`/Enter, never per keystroke) |
| `spa/.../ui/inbox-list/inbox-list.{ts,html}` | Modify | One new action (`reprocesarSolicitado`), `reprocesandoId` input, `<details>` panel |
| `spa/.../ui/panel-errores/`, `spa/.../ui/confirmar-reproceso/` | Create | D8, D6 |

## Interfaces / Contracts

```csharp
public sealed record FiltrosBandeja(string? Estado, DateOnly? Desde, DateOnly? Hasta,
    string? Proveedor, string Orden, int Pagina, int TamanioPagina = 20);

public sealed record ErrorProcesamiento(long ProcesamientoErrorId, string Integracion,
    string Mensaje, string Clasificacion, DateTime OcurridoEn);

public sealed record BandejaItem(long InboxEventId, string Origen, long ProcesamientoId,
    string EstadoConsumo, DateTime CreadoEn, long? FacturaId, string? ProveedorCodigo,
    string? RucProveedor, IndicadoresFactura? Indicadores, string? MotivoDescarte,
    IReadOnlyList<ErrorProcesamiento> Errores,          // never null; [] when no history
    DateTime? ReprocesarDisponibleEn);                  // null => reprocesar enabled

public sealed record PaginaBandeja(IReadOnlyList<BandejaItem> Items, int Pagina,
    int TamanioPagina, int TotalRegistros, int TotalPaginas);
```

```ts
export type BandejaItem =
  | (BandejaItemBase & { readonly origen: 'FACTURA'; readonly facturaId: number })
  | (BandejaItemBase & { readonly origen: 'INCIDENCIA'; readonly facturaId: null });
```

Derivation rules (pure, `SmartNet.Inbox.Core`, unit-tested without DB):
- `origen` = `FACTURA` when `EstadoConsumo == "PROMOVIDO" && FacturaId is not null`, else `INCIDENCIA`.
- Default view (no `estado` supplied) = `EstadoConsumo = 'PENDIENTE'` **OR** ≥1 error with
  `Clasificacion <> 'OBSOLETO'`. `DESCARTADO` and error-free `PROMOVIDO` rows are terminal.
- `reprocesarDisponibleEn` = `DATEADD(MINUTE, @ventanaMinutos, MAX(cq.CreadoEn))` over
  `fact.CommandQueue` rows with `Tipo='REPROCESAR_DOCUMENTO'`, `Referencia = ie.ProcesamientoId`,
  `Estado IN ('PENDIENTE','EN_PROCESO')`, `CreadoEn > DATEADD(MINUTE, -@ventanaMinutos, SYSUTCDATETIME())`.

## Edge cases (design-level, must become RED tests)

| Case | Behavior |
|---|---|
| `pagina < 1` or non-numeric | `400` ProblemDetails; never silently coerced to 1 |
| `pagina > totalPaginas` | `200`, `items: []`, truthful totals via the D4 fallback COUNT |
| `desde > hasta` | `400` ProblemDetails |
| `hasta` inclusive | `ie.CreadoEn < DATEADD(DAY, 1, @hasta)` — `CreadoEn` is `DATETIME2`, `@hasta` a `DATE`; a naive `<=` drops that day's later rows |
| Empty filters | Default-view predicate above, `pagina=1`, `orden=desc` |
| Row with no error history | `errores: []`; `<details>` not rendered |
| Filter changed while on page N | Container resets `pagina` to 1 in every filter handler (signal writes batch, effect fires once) |
| Double click on reprocesar | 3 guards: the dialog, `reprocesandoId` (optimistic, container-owned), then server `reprocesarDisponibleEn` after refetch |
| `estado` domain | `PENDIENTE\|PROMOVIDO\|DESCARTADO` only (`CK_InboxEvent_EstadoConsumo`). `spec.md`'s `estado=VALIDADO` scenario has no column behind it and must be amended to `PROMOVIDO` |

## Testing Strategy

| Layer | What | How |
|---|---|---|
| Unit (Core) | `origen` derivation, default-view predicate, `VentanaBloqueo`, envelope math (`totalPaginas = ceil(total/20)`) | xUnit, no DB/HTTP/clock (ADR 0019 level 1) |
| Integration (Infra) | Batch query, no row duplication with N errors, OFFSET tiebreaker stability, `hasta` boundary, `reprocesarDisponibleEn` transitions | ADR 0019 level 2 via `TestDatabaseFixture`, running **as `usr_api`** so the D1 grant is proven by the engine |
| Permission | `usr_api` SELECT succeeds, INSERT/UPDATE/DELETE denied on `fact.ProcesamientoError` | `PermissionMatrixTests` |
| API | Param validation, envelope shape, 400s | `WebApplicationFactory` |
| SPA | Filter→refetch, pagina reset, dialog gate, double-click guard, `errores: []` | vitest + jsdom |

## Threat Matrix

N/A — no routing, shell, subprocess, VCS/PR automation, executable-file classification, or
process-integration boundary. `orden` remains the only interpolated token and stays a fixed
`ASC`/`DESC` choice; every filter is a `SqlParameter`.

## Migration / Rollout

No DDL change: `fact.ProcesamientoError` and `fact.CommandQueue` already exist. One permission
migration (`018`, ADR 0016 versioned SQL, never EF Core). Rollback = revert `018` (restore the
DENY) plus the code slice; no data migration.

## Open Questions — resolved

- [x] **Owner ratified the ADR 0003 amendment behind D1.** `fact_api` gets `GRANT SELECT` on
  `fact.ProcesamientoError` via `018_permiso_lectura_procesamiento_error.sql`;
  `INSERT/UPDATE/DELETE` stay explicitly `DENY`d. `adrs/0003-particion-de-propiedad-de-datos-entre-net-y-python.md`
  must be amended accordingly before/with apply.
- [x] `018` also adds the three indexes (`IX_ProcesamientoError_ProcesamientoId`,
  `IX_InboxEvent_CreadoEn`, `IX_CommandQueue_Referencia`) in the same script — they support exactly
  the queries this permission change unblocks, and REGLAS del proyecto ya trata el esquema como SQL
  versionado; no hay razón para partirlo en una migración de performance aparte.
- [x] `spec.md`'s `estado=VALIDADO` scenario amended to `estado=PROMOVIDO` (matches
  `CK_InboxEvent_EstadoConsumo`).
