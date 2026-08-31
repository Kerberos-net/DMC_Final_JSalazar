# Design: Registro de compra en la SPA (solo lectura) — BACKLOG #23

## Technical Approach

Additive, read-only slice on both sides. API: a dedicated read port in `SmartNet.Facturacion.Core`
(pure, ADR 0019) + an ADO adapter in `SmartNet.Facturacion.Infrastructure` following the
`SqlBandejaRepository` / `SqlProveedorRepository` precedent, behind three thin
`.RequireAuthorization()` GET routes. SPA: a new `registro-compra/` feature cloned from
`catalogos/` (container + `ui/` + `data-access/` + `models/`), reusing `tabla-paginador`,
`boton-exportar` and `descarga-xlsx`. **No new versioned SQL and no new GRANT** — `008` already
grants `fact_api` SELECT on `fact.AsientoContable`, `fact.AsientoContableDetalle`, `fact.Factura`
and `dbo.Proveedor` (ADR 0016 untouched; ADR 0003 respected: no `dbo.*` write, no Python).
`SmartNet.Contable.Core` is not referenced.

## Architecture Decisions

| # | Decision | Alternatives rejected | Rationale |
|---|---|---|---|
| D1 | **`PaginaBandeja<T>` is NOT reused.** New `PaginaRegistroCompra` record in `Facturacion.Core` with the identical wire field names `{ items, pagina, tamanioPagina, totalRegistros, totalPaginas }`. | Reuse `SmartNet.Inbox.Core.PaginaBandeja<T>` (proposal's assumption) | **Correction to the proposal**: `PaginaBandeja<T>` carries a mandatory `ResumenBandeja` (5 inbox buckets) that is meaningless here, and would make `facturacion` depend on `inbox`. #22 hit the same wall and answered with a local `PaginaProveedores`. The SPA envelope is byte-identical either way. |
| D2 | Period travels as a pure `PeriodoContable(int Anio, int Mes)` Core value with `TryParse("YYYY-MM")`; the **adapter** derives a half-open `[primerDia, primerDiaMesSiguiente)` range. | `BETWEEN` on `FechaContable`; passing raw strings to SQL | `FechaContable` is `DATE`, but half-open is the only form that stays correct if the column ever widens, and it needs no month-length table. `TryParse` is pure (no clock) so PurityScan stays green. Bad input → `400`. |
| D3 | Line detail is a **separate route** `GET /api/registro-compra/{asientoId}` served by the same port, and it **re-applies the row predicate in its own SQL** (`JOIN fact.Factura`). | Embed `lineas[]` in the list; reuse `GET /api/asientos/{id}` | Embedding inflates a 200–1000-row month. Re-applying the predicate means the detail route cannot be used as a side channel to read an `ANULADO` or non-`VALIDADA` asiento; a filtered-out id returns `404`, indistinguishable from a nonexistent one. `api-asientos` is untouched (proposal Decision 3). |
| D4 | Money/rate DTO fields are **`decimal?`**, not `decimal`. | Non-nullable with `0` default | `BasePEN`, `IgvPEN`, `NetoPEN`, `TipoCambioVenta`, `NumeroAsiento`, `NumeroComprobante`, `Glosa` are all `NULL`-able in `005_negocio.sql`. Coercing `NULL`→`0` would manufacture a fake descuadre in the badge. Absent renders as `—` (`importeOpcional`). |
| D5 | Export filename is rebuilt from the **parsed** `Anio`/`Mes` ints, never from the raw query string. | `$"registro-compra-{periodo}.xlsx"` | ADR 0021 decision 4: no user input reaches `Content-Disposition`. Validate-then-reconstruct is the only form that honours it while still naming the period. |
| D6 | Inconsistency badge = pure `computed()` in the SPA, exact to the cent, no epsilon. | A domain rule / a server-computed flag | REGLAS.md §6 "no hay tolerancia"; ADR 0019 keeps the accounting core free of presentation concerns. The screen shows amounts already frozen at confirm — it never recomputes them. |
| D7 | Export reuses a **dedicated unpaged** `ListarPeriodoCompletoAsync`. | Loop the paged method; `tamanio=int.MaxValue` | Same shape as `IProveedorRepository.ListarCatalogoCompletoAsync` (#22). One pass, one `ORDER BY`, no `COUNT(*) OVER()` overhead. |
| D8 | SPA route is top-level `/registro-compra` (not under `catalogos/`). | `/catalogos/registro-compra` | The sidebar `nav-registro` entry sits in the **primary** group, above the catalog group; it is a fiscal report, not a catalog. |

## SQL Design

`UQ_Asiento_Vigente` (`UNIQUE ON (FacturaId) WHERE Estado <> 'ANULADO'`) guarantees at most one
non-`ANULADO` asiento per factura — **confirmed: no `DISTINCT`/dedup and no window de-duplication
is needed**; the join is 1:1.

```sql
-- ListarPeriodoAsync
SELECT a.AsientoContableId, a.NumeroComprobante, a.NumeroAsiento, a.OrigenLibro,
       a.ProveedorCodigo, pr.proveedor AS ProveedorNombre, a.Glosa, a.FechaContable,
       a.TipoCambioVenta, a.BasePEN, a.IgvPEN, a.NetoPEN,
       CAST(COUNT(*) OVER() AS INT) AS TotalRegistros
FROM fact.AsientoContable a
JOIN fact.Factura f       ON f.FacturaId = a.FacturaId
LEFT JOIN dbo.Proveedor pr ON pr.codpro = a.ProveedorCodigo
WHERE f.Estado = 'VALIDADA' AND a.Estado <> 'ANULADO'
  AND a.FechaContable >= @desde AND a.FechaContable < @hasta
ORDER BY a.FechaContable, a.NumeroAsiento, a.AsientoContableId
OFFSET @offset ROWS FETCH NEXT @tamanioPagina ROWS ONLY;
```

`ORDER BY` ends in `AsientoContableId` because `NumeroAsiento` is nullable — without a unique
tiebreak `OFFSET/FETCH` can repeat or skip a row across pages. The `COUNT(*) OVER()` total is
correct for an in-range page; an out-of-range page yields `0` items and `totalRegistros = 0`
(accepted: unlike `SqlBandejaRepository` there is no fallback `COUNT(*)` — the SPA never requests a
page past `totalPaginas`). `LEFT JOIN dbo.Proveedor` → `proveedorNombre: null` when absent.

Detail (`ObtenerAsync`) is the same `SELECT` narrowed by `a.AsientoContableId = @id` (no paging,
**same `WHERE` predicate**), followed by:

```sql
SELECT d.Orden, d.Bloque, d.Tipo, d.Debe, d.Haber, d.CuentaCodigo, d.CuentaDescripcion
FROM fact.AsientoContableDetalle d WHERE d.AsientoContableId = @id ORDER BY d.Orden;
```

Both result sets ride one `ExecuteReaderAsync` + `NextResultAsync`. Zero cabecera rows → `404`;
cabecera with zero lines → `200` with `lineas: []` (SPA renders "sin líneas contables").

## Excel Generation

`ExportadorXlsx.Escribir(Stream, IEnumerable<IReadOnlyList<string>>, IReadOnlyList<string>)` from
`SmartNet.Exportacion.Infrastructure` (ADR 0021, already referenced by `CatalogoEndpoints`). Every
cell is an **inline string**, so the endpoint formats: money `ToString("F2", InvariantCulture)`,
`TipoCambioVenta` `"F6"`, `null` → `""`, `FechaContable` `"yyyy-MM-dd"`. Sheet name is fixed at
`"Datos"` by the helper — not parameterised, do not add a parameter for this change. Columns =
the cabecera set: `Fecha contable | Numero de asiento | Origen libro | Numero de comprobante |
Codigo proveedor | Proveedor | Glosa | Tipo de cambio venta | Base PEN | IGV PEN | Neto PEN`.
Returned via `Results.File(buffer.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
fileDownloadName: $"registro-compra-{p.Anio:D4}-{p.Mes:D2}.xlsx")` — buffered, not streamed (the
helper needs a seekable stream; a month is ≪ the ~6,600-row case ADR 0021 sized for).

## DI and Purity

`Program.cs`, immediately after the `ICuentaContableRepository` block, same lazy factory shape as
every repo there:

```csharp
builder.Services.AddSingleton<IRegistroCompraRepository>(sp =>
    new SqlRegistroCompraRepository(ApiConnectionOptions.Resolve(sp.GetRequiredService<IConfiguration>())));
```

Singleton is safe: the only field is a `readonly string`; each call opens and disposes its own
`SqlConnection` (connection-per-call, pooled) — identical to `SqlBandejaRepository`.

**PurityScanTests needs no edit.** `SmartNet.Facturacion.Core.Tests/PurityScanTests.cs` scans the
whole `SmartNet.Facturacion.Core.dll` assembly (`Types.InAssembly` + `ModuleDefinition`
`AssemblyReferences`), so a new port and its records are covered the moment they compile. A task
that "extends PurityScan" would be a no-op — instead, task the *verification* that the suite still
passes after the port lands.

## Contracts

```csharp
// SmartNet.Facturacion.Core — pure, no infra types
public sealed record PeriodoContable(int Anio, int Mes)
{ public static bool TryParse(string? valor, out PeriodoContable? periodo); }

public sealed record RegistroCompraCabecera(
    long AsientoContableId, string? NumeroComprobante, string? NumeroAsiento, string OrigenLibro,
    string ProveedorCodigo, string? ProveedorNombre, string? Glosa, DateOnly FechaContable,
    decimal? TipoCambioVenta, decimal? BasePEN, decimal? IgvPEN, decimal? NetoPEN);

public sealed record LineaRegistro(
    short Orden, string Bloque, string Tipo, decimal Debe, decimal Haber,
    string? CuentaCodigo, string? CuentaDescripcion);

public sealed record RegistroCompraDetalle(
    RegistroCompraCabecera Cabecera, IReadOnlyList<LineaRegistro> Lineas);

public sealed record PaginaRegistroCompra(
    IReadOnlyList<RegistroCompraCabecera> Items, int Pagina, int TamanioPagina,
    int TotalRegistros, int TotalPaginas);

public interface IRegistroCompraRepository
{
    Task<PaginaRegistroCompra> ListarPeriodoAsync(PeriodoContable p, int pagina, int tamanio, CancellationToken ct);
    Task<RegistroCompraDetalle?> ObtenerAsync(long asientoId, CancellationToken ct);
    Task<IReadOnlyList<RegistroCompraCabecera>> ListarPeriodoCompletoAsync(PeriodoContable p, CancellationToken ct);
}
```

Serialization is the Minimal-API default (`JsonSerializerDefaults.Web`) → camelCase on the wire, as
in every existing endpoint. `OrigenLibro` is echoed verbatim from the column (never the
`ServicioDeFacturas` `"02"` constant). Endpoint response records mirror these 1:1 as
`internal sealed record`s in `RegistroCompraEndpoints.cs` (a **new file**, not `AsientoEndpoints.cs`
— that file is already 250+ lines of write routes and this is a separate capability).

## Data Flow

    RegistroCompraPage ──periodo/pagina/tamanio──→ RegistroCompraService ──GET /api/registro-compra──┐
         │  computed(inconsistente)                                                                  │
         ├─ fila expandida ──→ GET /api/registro-compra/{asientoId} ──→ IRegistroCompraRepository ───┤
         └─ BotonExportar ──→ DescargaXlsx ──→ GET .../export ──→ ExportadorXlsx ──→ .xlsx           │
                                                                                                     ▼
                                          fact.AsientoContable ⋈ fact.Factura ⟕ dbo.Proveedor (usr_api, SELECT)

## Frontend Architecture

`src/app/registro-compra/` mirroring `catalogos/`:

| Path | Role |
|---|---|
| `feature/registro-compra-page/` | Container, `OnPush`. Owns `periodo` signal (default = current local month) and delegates paging to the service, exactly like `ProveedoresPage`. |
| `data-access/registro-compra.service.ts` | `providedIn:'root'`, private writable signals + `asReadonly()`, `firstValueFrom(http.get)`, `cargando`/`error`; server-side paging state (`periodo`, `paginaSolicitada`, `tamanio`) coalesced through the `programar(delay)` timer of `CatalogoProveedorService` (it also absorbs the paginador's `tamanioChange`+`paginaChange(1)` burst). |
| `data-access/registro-compra-detalle.service.ts` | Per-`asientoId` lazy fetch **memoised in a `Map<number, LineaRegistro[]>`**; re-expanding a row issues no second request. The cache is cleared on any `periodo`/page change. |
| `ui/registro-compra-tabla/` | Presentational rows + expand toggle + the badge; `input()`/`output()` only. |
| `ui/asiento-detalle/` | Read-only line table ordered by `orden`; empty → "sin líneas contables". |
| `models/registro-compra.model.ts` | Mirrors the API records; all money fields `number \| null`. |

Reused as-is: `ui/tabla-paginador`, `ui/boton-exportar`, `data-access/descarga-xlsx` (imported from
`catalogos/` — do NOT fork them), `shared/formato.ts` (`dosDecimales`, `importeOpcional`).

**Current month default**: add a pure `mesActual(hoy = new Date()): string` to `shared/formato.ts`
returning local `` `${y}-${MM}` `` — mirroring `rangoMesActual`'s "LOCAL, never `toISOString`"
rule; the browser clock is read in the container only (ADR 0019 parity).

**Badge** — pure `computed()` per row, no epsilon:

```ts
const r2 = (n: number) => Math.round(n * 100) / 100;
// null in any term => NOT inconsistent (absence is not a mismatch); renders "—"
cabeceraDescuadrada = base != null && igv != null && neto != null && r2(base + igv) !== r2(neto);
detalleDescuadrado  = lineas != null && r2(sum(debe)) !== r2(sum(haber));
```

Routing: a lazy `loadComponent` child of the `ShellLayout` route with `canActivate: [authGuard]`,
`path: 'registro-compra'`, placed before the `catalogos/*` entries. `app.routes.spec.ts` gains one
additive assertion.

Error/empty: service `catch` → `error` signal ("No se pudo cargar el registro de compras."), items
cleared; a `400` from a malformed period is prevented client-side by validating `YYYY-MM` before
the request.

## spa-shell-nav Amendment Mechanics

Verified against the real files — the proposal's "exactly-N-links assertion 5→6" does **not**
exist; `sidebar.spec.ts` asserts destinations by `data-testid`:

| File | Change |
|---|---|
| `shared/shell-layout/sidebar/sidebar.ts` | Add `ruta: '/registro-compra'` to the `nav-registro` entry in `primarios`. Nothing else — the glyph `registro` and the label already exist. |
| `sidebar.spec.ts` | In `'links only the destinations with a real route; the rest are inert'`: remove `'nav-registro'` from the inert loop array (`3 → 2`: `nav-errores`, `nav-sincronizacion`) and add a routed assertion asserting `href`/`ng-reflect-router-link` contains `registro-compra`. Update the file-header comment's routed/inert sentence. The 8-label list, the 1-divider test and the 8-glyph test are **unchanged**. |
| `openspec/specs/spa-shell-nav/spec.md` | Move `Registro de compra` into the routed set in the two "Sidebar mirrors the handoff navigation" scenarios + the `sidebar.spec.ts` scenario (→ 6 routed, 2 inert). Same edit shape as #22. |
| `shell-layout.ts` / `.html` / `shell-layout.spec.ts` | **No change** — the container passes inputs only. |

## Testing Strategy (ADR 0019, strict TDD)

| Layer | What | How |
|---|---|---|
| Contract (API) | `401` without cookie (all 3 routes); camelCase keys; period filter includes first/last day of month and excludes the adjacent months' edge days; `f.Estado='DESCARTADA'`/`'PENDIENTE_VALIDACION'` row excluded; `a.Estado='ANULADO'` row excluded; pagination envelope + `totalRegistros` across 2 pages; stable order across pages; empty period → `items: []`, `totalRegistros: 0`; `periodo` missing/malformed (`2026-13`, `agosto`, `2026-8`) → `400`; `proveedorNombre` null when `codpro` absent; `OrigenLibro` echoed verbatim; detail of an `ANULADO` / non-`VALIDADA` asiento → `404`; detail with zero lines → `200` + `lineas: []`; lines ordered by `orden`; export → `200`, xlsx content-type, `Content-Disposition` filename `registro-compra-2026-08.xlsx` even when `periodo` carries junk that failed validation (`400` instead). | `CatalogoEndpointsTests` style: `SmartNetApiFactory` (`WebApplicationFactory<Program>`), `TestDatabaseFixture` → `fact_test_<guid>` with the real versioned schema, real `/api/sesion` cookie. Per the `integration-spa-api` harness doctrine: **never** an in-memory repo, never an injected principal. |
| Purity | Core stays infra-free | Existing `SmartNet.Facturacion.Core.Tests/PurityScanTests.cs`, no edit (see DI section). |
| Unit (SPA) | Badge `computed()`: cabecera formula, detalle formula, boleta `IGV=0` (`base==neto`, no badge), exact-to-cent boundary (`100.00+18.00` vs `118.01` → badge; vs `118.00` → none), any-null → no badge. `mesActual()` local-time default incl. a `31 Dec 23:00` local instance that UTC would roll into January. Service: params sent, envelope mapped, error path, detail cache hit issues one request. Sidebar: routed/inert split. | `ng test` (Vitest + jsdom), `HttpTestingController` for the service — component-level only; it does **not** count as integration (harness §"Prohibido"). |
| Integration harness | New flow "Registro de compra" added to `.claude/skills/integration-spa-api/SKILL.md` §"Flujos en alcance" | Recorded **manually** (the harness runs and reports; it does not author tests), same as the #22 precedent. |

**Test data**: `FacturaTestDataHelper.InsertarFacturaAsync(estado: "VALIDADA")` +
`InsertarAsientoBorradorBalanceadoAsync` then `UPDATE ... SET Estado='CONFIRMADO'` — or a new
`InsertarAsientoConfirmadoAsync(facturaId, fechaContable, base, igv, neto, estado)` overload in the
same helper. Because these helpers insert with **raw SQL, bypassing the domain**, an *inconsistent*
asiento (`100 + 18 ≠ 999`) IS persistable and can be used to prove the API echoes the amounts
verbatim without "fixing" them. The badge itself is never asserted server-side — it is presentation
(D6), so its truth table lives in the SPA unit tests with synthetic data.

## Threat Matrix

| Row | Status | Safe behavior | RED test |
|---|---|---|---|
| Authz | Applicable | All 3 routes `.RequireAuthorization()`; anonymous → flat `401`, no body | Anonymous GET on each route |
| Broken object-level authz (detail route as a read side channel) | Applicable | `{asientoId}` re-applies the row predicate in SQL; filtered-out → `404` | `ANULADO` and `DESCARTADA` ids → `404` |
| Header injection via `Content-Disposition` | Applicable | Filename rebuilt from parsed ints (D5); invalid period never reaches the header | `periodo=2026-08%0D%0AX` → `400`, no header echo |
| SQL injection | Applicable | Every filter is a `SqlParameter`; no identifier interpolation (unlike `SqlBandejaRepository`, there is no user-chosen sort here) | `periodo="2026-08'; DROP"` → `400` |
| Data-partition violation (ADR 0003) | Applicable | Read-only `SELECT` under existing `fact_api` grants; no `dbo.*` write, no Python | Existing `PermissionMatrixTests` still green |
| Shell / subprocess / VCS automation / executable classification | N/A | No process boundary in this change | — |

## Migration / Rollout

No migration. No versioned SQL, no GRANT, no schema object. Every change is additive; `git revert`
returns the system to the post-#22 state (sidebar entry back to inert, route and feature folder
gone, three routes + one `AddSingleton` + the port/adapter gone).

## Review-Budget Slicing (input to sdd-tasks)

Forecast: port + adapter (~200) + endpoints/DTOs (~150) + contract tests (~250) + SPA feature
(~450 incl. HTML/CSS/specs) + shell-nav amendment (~30) ≈ **1,050–1,150 changed lines** — over the
800-line budget cached for this change and far over the 400-line default.

- `400-line budget risk: High`
- `Chained PRs recommended: Yes`
- `Decision needed before apply: Yes`

Recommended slices, each independently shippable, verifiable and revertible:

1. **PR1 — API** (`~600`): `PeriodoContable` + `IRegistroCompraRepository` + records (Core),
   `SqlRegistroCompraRepository`, `RegistroCompraEndpoints.cs`, `Program.cs` DI, contract tests.
   Ships behind no UI; verified by `dotnet test`.
2. **PR2 — SPA feature** (`~450`): `registro-compra/` feature + route + specs. Reachable by URL
   only; verified by `ng test` + `tsc --noEmit`.
3. **PR3 — shell-nav amendment** (`~30`): `sidebar.ts` route, `sidebar.spec.ts`, spec delta.
   Trivially revertible; makes the destination discoverable.

PR2 targets PR1's branch, PR3 targets PR2's, per the Feature Branch Chain rule.

## Open Questions

- [ ] `tamanioPagina`: adopt #22's `{6,10,20,50}` allow-list with default `20`, rejecting anything
      else with `400`? Assumed yes (paginador default) — confirm during tasks.
- [ ] Export cap: a period is bounded (~200–1,000 rows), so no row limit is imposed. If a
      pathological period is ever possible, ADR 0021's ~6,600-row sizing is the ceiling.
