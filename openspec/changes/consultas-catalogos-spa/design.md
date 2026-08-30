# Design v2: Read-only catalog queries in the SPA (BACKLOG #22)

> **v2 supersedes v1.** The owner expanded scope after reading v1 (proposal "Scope expansion",
> Engram `sdd/consultas-catalogos-spa/proposal` #238). v1's two canvas deviations are **withdrawn**:
> full pagination, sortable headers and "Exportar a Excel" are now all in scope, so v2 is *more*
> canvas-faithful than v1. Decisions D1-D5 stand except where D6-D10 amend them.
>
> **All open questions ratified by the project owner (2026-08-30). No blockers remain.**
> (1) **ADR 0021 ACCEPTED** with `DocumentFormat.OpenXml` 3.x — authored at
> `adrs/0021-generacion-de-archivos-excel-en-la-api.md`. (2) SPA route prefix = **grouped
> `catalogos/`**. (3) Pagination `total` via `COUNT(*) OVER()` in the same paged `SELECT` — zero extra
> scans, **superseding** the proposal's "accepted second scan" — on the existing `PaginaBandeja<T>`
> envelope. (4) Export sub-path = `exportacion`. (5) The plan-contable filter predicate may be
> expressed twice (SPA client filter + server `q` for the export), asserted on both sides.

## Design handoff source

`handoff/Gestor de Facturas.dc.html` governs `Proveedores` (1034-1082) and `Plan contable`
(1084-1131): title + "solo lectura" subtitle, one search input, card-wrapped grid table, clickable
header labels, `Exportar a Excel` button (green sheet glyph, CSS divs only), and a footer with
`← Anterior · Página X de Y · Siguiente →` plus a `Filas por página` select of `6 / 10 / 20 / 50`.
`Tipo de cambio` has no canvas entry (owner decision 5) and reuses the same language.

**No remaining canvas deviations.** All three canvas controls are implemented.

## Architecture Decisions

### D1 — Proveedores dual-mode on one endpoint (v1, amended by D6/D7)

Explicit `modo` query param on `GET /api/catalogos/proveedores`. Absent or `picker` → existing
`IProveedorRepository.BuscarAsync`, **byte-frozen** (#18: min 2 chars, `P00000` excluded,
`{resultados, hayMas}`). `modo=catalogo` → new `ListarCatalogoAsync`; any other value → 400.

Rejected: overloading blank-`q` (silently flips the #18 no-scan guard and leaks `P00000` into the
picker); a mode enum on `BuscarAsync` (reopens every #18 call site); a second route (owner decision 1).

### D2 — `ListarHistoricoAsync` on `ITipoCambioRepository` (v1, unchanged)

```csharp
Task<IReadOnlyList<TipoCambio>> ListarHistoricoAsync(DateOnly desde, DateOnly hasta, CancellationToken ct);
```

`SELECT Fecha, Origen, Compra, Venta, FechaConsulta FROM fact.TipoCambio WHERE Fecha BETWEEN @desde
AND @hasta AND Origen IN ('SBS','MANUAL') ORDER BY Fecha DESC, Origen ASC;` — a `(Fecha, Origen)` PK
seek, no scan, inclusive bounds. The `Origen IN (...)` predicate is required so the existing private
`Map`'s `(OrigenTipoCambio)(-1)` fallback never reaches a display list. Rejected: returning
`ResultadoTipoCambio` — that closed hierarchy models the one *vigente* rate; a history's empty is `[]`.

Pure: parameters only, no `TimeProvider`/`DateTime.UtcNow` (ADR 0019; `PurityScanTests` guards Core).
Additive member — `SqlUnidadDeTrabajo` / `SqlFacturacionStore` (#8 `Venta` freeze) are untouched.
**Range validation lives in the endpoint, not the port**: how much history one HTTP call may pull is
transport, not accounting; a span limit in Core would invent a rule `REGLAS.md` lacks. 400 for
missing `desde`/`hasta`, unparseable date, `hasta < desde`, span > 366 days (owner decision 9).

### D3 — Plan contable (v1, amended by D9)

`GET /api/catalogos/plan-contable` in `CatalogoEndpoints.cs`, thin over `ListarPlanCompletoAsync`.
DTO `CuentaContableResultado(string Cuenta, string Descripcion, byte? Nivel, bool EsHojaImputable)`
declared beside `ProveedorResultado` (same `internal sealed record` pattern, no new file).
`EsHojaImputable` is **projected** from the domain computed property, never recomputed (ADR 0019).
`ctarefleja` / `ctapuente` are accounting-mapping internals, not browse data. No server pagination
(owner decision 4) — the footer is client-side (D8).

### D4 — SPA `catalogos/` structure (v1, amended by D8)

```
catalogos/
  data-access/  proveedor.service.ts · proveedor.model.ts        (BOTH UNTOUCHED — #18 frozen)
                catalogo-proveedor.service.ts · plan-contable.service.ts · tipo-cambio.service.ts
                descarga-xlsx.ts                                  (shared, D8)
  models/       cuenta-contable.model.ts · tipo-cambio.model.ts · pagina-catalogo.model.ts
  feature/      proveedores-page/ · plan-contable-page/ · tipo-cambio-page/
  ui/           proveedores-tabla/ · plan-contable-tabla/ · tipo-cambio-tabla/
                tabla-paginador/ · boton-exportar/ · orden.ts     (shared, D8)
```

`ProveedorService` is **not** extended: it is the picker's debounced root singleton; sharing
`resultados`/`hayMas`/`buscando` with a browse screen means one clobbers the other and `limpiar()`
wipes the browse list. New `CatalogoProveedorService` hits the same route with `modo=catalogo`.

**Local-date helper**: the TC default range (1st of month → today) must use the local `yyyy-MM-dd`
formatter, not `toISOString`. Move the private helper out of `inbox/feature/inbox-page/inbox-page.ts`
into `shared/formato.ts` and import in both.

**Routing (owner-ratified — grouped, not flat)**: 3 lazy `ShellLayout` children with
`canActivate: [authGuard]`, under the **`catalogos/` prefix**:

| URL | Component |
|---|---|
| `/catalogos/proveedores` | `catalogos/feature/proveedores-page` |
| `/catalogos/plan-contable` | `catalogos/feature/plan-contable-page` |
| `/catalogos/tipo-cambio` | `catalogos/feature/tipo-cambio-page` |

Registered as three sibling children of `ShellLayout` with literal `path: 'catalogos/proveedores'`
etc. — **not** a nested `children` route under a `catalogos` parent: there is no shared catalog
layout or resolver to hoist, and a parent route would add an empty `<router-outlet>` shell plus a
redirect for the bare `/catalogos` path that nothing links to. The URL prefix groups the
destinations; the router tree stays as flat as the existing `bandeja` / `detalle/:id` /
`configuracion` entries. This resolves the v1 design ↔ spec v1 conflict in favour of the spec's
grouped form. `app.routes.spec.ts` uses `arrayContaining` so it stays green; still extend both lists
additively so the three new routes are asserted guarded.

Accepted inconsistency: `proveedor.model.ts` stays under `data-access/` (moving it churns 4 picker
call sites + 3 specs). New models follow the `inbox/` convention under `models/`.

### D5 — Sidebar delta 7 → 8 (v1, unchanged)

`Tipo de cambio` joins the **primary** group after `Plan contable` (a catalog, not a utility):
`{ testid: 'nav-tipo-cambio', etiqueta: 'Tipo de cambio', glifo: 'tipo-cambio',
ruta: '/catalogos/tipo-cambio' }`. `Proveedores` gains `ruta: '/catalogos/proveedores'`,
`Plan contable` gains `ruta: '/catalogos/plan-contable'` (grouped prefix, D4).

Exact ordered list for `sidebar.spec.ts` test 1: `Bandeja principal`, `Registro de compra`,
`Proveedores`, `Plan contable`, `Tipo de cambio`, `Errores y notificaciones`, `Sincronización`,
`Configuración`. Linked (`<a>`) set → `nav-bandeja`, `nav-proveedores`, `nav-plan-contable`,
`nav-tipo-cambio`, `nav-configuracion`; inert loop shrinks to `['nav-registro','nav-errores',
'nav-sincronizacion']`; glyph count 7 → 8. The spec docblock **must** record that the canvas has no
`Tipo de cambio` entry and that this is owner decision 5 — a reviewer must not "restore" 7 (memory
`shell-nav-canvas-replica`; spec reopened a 3rd time).

`.glifo--tipo-cambio` folds into the existing `.glifo--registro, .glifo--plan` bar rules (`div`/`span`
+ pseudo-elements only — no `svg`/`img`, asserted by test 4) plus one rule for opposed arrow ends.
≈150-200 bytes on a ~5.3 kB `sidebar.css` → ~5.5 kB, under the `angular.json` 6 kB warn cap. If a
future edit crosses it, factor shared geometry into a combined selector — **do not raise the budget**.

---

### D6 — Full pagination: reuse the `PaginaBandeja` envelope, and **one** scan, not two

**Choice.** `modo` gates the response *shape*:

| `modo` | 200 body | Consumer |
|---|---|---|
| absent / `picker` | `{ resultados: [{codigo,nombre,ruc}], hayMas }` — **unchanged** | #18 detalle picker |
| `catalogo` | `{ items: [{codigo,nombre,ruc}], pagina, tamanioPagina, totalRegistros, totalPaginas }` | Proveedores screen |

The catalogo shape reuses the project's existing pagination envelope verbatim
(`PaginaBandeja<T>` in `SmartNet.Inbox.Core/IBandejaRepository.cs`, already consumed by
`InboxService`) — same field names, so the SPA gets a `PaginaCatalogo<T>` that mirrors
`PaginaBandeja<T>` and the reviewer reads one convention, not two. The proposal's sketch
(`{resultados, total, pagina, tamano}`) is **not** adopted: it invents a third naming for a shape the
codebase already has.

**The #18 picker keeps working with a zero-line diff.** `ProveedorService` sends no `modo`, gets the
frozen `{resultados, hayMas}`, and `proveedor.model.ts` / `proveedor.service.ts` / the 3 picker specs
are untouched. Rejected: one unified shape for both modes — it would force a diff across the picker,
its model, its specs and the detalle page for zero user-visible gain, and would charge every picker
keystroke the total-count cost. Rejected: an additive nullable `total?` on one envelope — a union
type the SPA must narrow at runtime is worse than two named shapes.

**Cost — OWNER-RATIFIED, and it SUPERSEDES the proposal's "accepted second unindexed scan".**
`totalRegistros` comes from `CAST(COUNT(*) OVER() AS INT)` **inside the same paged `SELECT`**, the
pattern `SqlBandejaRepository.ListarConConexionAsync` already establishes. `OFFSET/FETCH` is applied
logically *after* window functions, so `COUNT(*) OVER()` yields the total over the whole filtered set
in the **same** pass that already had to scan and sort. Net cost of `total` on `dbo.Proveedor`
(~6,600 rows, <1 MB; ADR 0003 forbids an index): **zero extra scans**, not the double scan the
proposal budgeted for. Only the out-of-range-page edge (`pagina` past the last page → no rows → no
window value) needs the conditional fallback `COUNT(*)`, mirrored from `SqlBandejaRepository`
design D4. Sub-10 ms warm, single-operator app. Escalation if it ever hurts = a new ADR (indexed view
/ `fact.*` projection), **never** an index on `dbo.*`.

The catalogo envelope is therefore the **existing `PaginaBandeja<T>` shape verbatim** —
`{ items, pagina, tamanioPagina, totalRegistros, totalPaginas }` — and **not** a new shape. Ratified.

**Page size** becomes a request param `tamanio`, whitelisted to the canvas set `{6, 10, 20, 50}`;
anything else → 400 (an unbounded page is unbounded transfer). `SqlProveedorRepository.TamanoPagina
= 20` stays as the **picker's** fixed size — do not repurpose the constant.

### D7 — Server-side sort on proveedores: closed vocabulary in Core, literal columns in the adapter

**Choice.** `orden` ∈ `{proveedor, ruc, codigo}` (default `proveedor`), `direccion` ∈ `{asc, desc}`
(default `asc`), catalogo mode only. Unknown value → 400.

```csharp
// SmartNet.Catalogos.Core/OrdenProveedor.cs — pure, PurityScan-safe
public static class OrdenProveedor
{
    public static readonly IReadOnlySet<string> Valores =
        new HashSet<string>(StringComparer.Ordinal) { "proveedor", "ruc", "codigo" };
    public static bool EsValido(string? v) => v is not null && Valores.Contains(v);
}
```

The adapter maps key → **compile-time constant** column name in a `switch` (`"ruc" => "rucpro"`,
`"codigo" => "codpro"`, `_ => "proveedor"`); the user's string is never concatenated into SQL. This
is exactly the precedent `SqlBandejaRepository` sets for its `asc|desc` direction and
`EstadoDerivadoBandeja.Valores` sets for endpoint-side vocabulary validation. **No injection
surface** — and no dynamic SQL, so the plan stays cacheable per shape.

**Deterministic tiebreak (correctness, not style)**: every ordering appends `, codpro ASC`.
`proveedor` repeats and `rucpro` is non-unique *and* nullable; without a unique tiebreak,
`OFFSET/FETCH` can drop or duplicate rows across pages. `rucpro` NULLs sort first on `ASC` (SQL
Server default) — accepted, asserted in the adapter test.

Plan contable and Tipo de cambio sort **client-side** (D8): both fetch a bounded set in one response
(full plan; ≤366 days × 2 origins = ≤732 rows), so a round-trip per header click would be pure
latency. Rejected: server sort everywhere — three more param whitelists for zero benefit.

### D8 — SPA: share the chrome, not the table

The three table *bodies* still differ (3 / 2 / 4 columns, different cell formatting), so v1's
rejection of a generic data-table stands — dynamic cell projection for three shapes is
over-engineering. But the *chrome* the expansion adds is identical across all three:

| Piece | Form | Why |
|---|---|---|
| `ui/tabla-paginador/` | presentational component — `input()` `pagina`/`totalPaginas`/`tamanio`/`tamaniosDisponibles`, `output()` `paginaChange`/`tamanioChange` | The canvas footer is byte-identical on all three screens; one component, one spec |
| `ui/boton-exportar/` | presentational component — `input() descargando`, `output() exportar`, CSS-div green sheet glyph | Same button + glyph on all three; no `svg`/`img` (sidebar precedent) |
| `ui/orden.ts` | **pure module functions**, not a component — `type Direccion`, `alternar(estado, columna)`, `sufijoOrden(...)` + a shared `.tabla-catalogo__th--ordenable` class in `styles.css` | Header cells are CSS-grid items; a component host per cell adds a DOM layer and a generic type for zero reuse, while the *logic* (toggle asc/desc, arrow glyph) is ~15 shared pure lines |
| `data-access/descarga-xlsx.ts` | shared root service — `descargar(url, params)` | Identical mechanics for all three; only URL + params differ |

`tabla-paginador` is source-agnostic: proveedores feeds it server-side values, plan contable and TC
feed it client-side `computed()` slices of their in-memory arrays. Client-side sort uses one
module-level `Intl.Collator('es')` (constructing per comparison is slow) and numeric compare for
`nivel` / `compra` / `venta`.

**Download mechanics.** `http.get(url, { params, responseType: 'blob', observe: 'response' })` →
read `Content-Disposition` for the filename → `URL.createObjectURL` → anchor click →
`revokeObjectURL`. **Not** `window.open`: a 401 there renders a blank tab and bypasses
`httpErrorInterceptor`'s session-expiry redirect. `Content-Disposition` is readable from JS because
API and SPA are same-origin behind one reverse proxy (ADR 0012, no CORS). A `descargando` signal
disables the button in flight.

Shared CSS (`.tabla-catalogo*` card/header/hairline/`tabular-nums`) goes into the existing
`@layer primitives` in `src/styles.css` using **semantic tokens only** — `contraste.spec.ts` /
`paleta.spec.ts` fail on any color literal, including the canvas's `#1f8a3d` sheet glyph, which must
resolve to an existing semantic success/positive token.

### D9 — Excel export: three routes, one new infra project, one new dependency (**ADR 0021, ratified**)

**Routes — three, not one parameterized.**

| Route | Params | Notes |
|---|---|---|
| `GET /api/catalogos/proveedores/exportacion` | `q`, `orden`, `direccion` | Same filter+sort as the list (server-side sort applies here), no paging |
| `GET /api/catalogos/plan-contable/exportacion` | `q` | `q` mirrors the SPA's client-side filter; default order, user sorts in Excel (sort is client-side on screen) |
| `GET /api/tipos-cambio/exportacion` | `desde`, `hasta` (required) | Same 400s as the list route; default order (sort is client-side on screen) |

Rejected: one `GET /api/catalogos/exportacion?catalogo=…`. ADR 0008 already rejected the generic
parameterized endpoint by name ("el contrato deja de ser inspeccionable"), and TC lives on a
different resource family, so one route could not cover all three without breaking that grouping.
Sub-path is `exportacion` (Spanish noun sub-resource, **owner-ratified**), matching
`/api/documentos/{id}/contenido` rather than the English `/export` sketched in the brief.

**Every export honors the visible filter *and* sort**, so no screen exports rows in an order the user
did not ask for. Consequence: `plan-contable/exportacion` takes a `q` and applies the same
"contains, case-insensitive, over `cuenta` or `descripcion`" predicate the SPA applies client-side.
That rule is therefore expressed twice (TS + SQL); accepted, and asserted on both sides. The list
route keeps returning the full plan when `q` is absent (owner decision 4 intact).

**Dependency.** Evaluated:

| Option | License | Footprint | Verdict |
|---|---|---|---|
| **`DocumentFormat.OpenXml` 3.x** | MIT, Microsoft-maintained | 1 package, no third-party transitive graph; `OpenXmlWriter` writes rows SAX-style with bounded memory | **CHOSEN** |
| `ClosedXML` | MIT | wraps the same OpenXML SDK + `SixLabors.Fonts`, `ExcelNumberFormat`, `XLParser`; DOM-based, materializes the whole workbook | Rejected — 4+ transitive packages and a higher memory peak to save ~80 lines, against a codebase that chose ADO over an ORM and hand-built CSS glyphs over an icon library |
| `MiniExcel` | Apache-2.0 | tiny, streaming | Rejected — smallest community/maintenance base of the three; no advantage over first-party MIT |
| `EPPlus` ≥ 5 | **Polyform Noncommercial** | — | **Rejected on licensing.** This is a company's purchase ledger; commercial use requires a paid license |
| CSV instead of `.xlsx` | zero deps | — | Rejected — the owner asked for a real `.xlsx` |

**Where the code sits (ADR 0019 boundary).** New project
`SmartNet/SmartNetApi/exportacion/SmartNet.Exportacion.Infrastructure` (+ `.Tests`) holding one
`ExportadorXlsx.Escribir(Stream, IEnumerable<fila>, columnas)` helper and the `PackageReference`;
`SmartNet.Api` adds a `ProjectReference`. Rationale: (a) **no `*.Core` project ever sees the
package** — the accounting cores stay pure and a structural guard test asserts it, the same way
`NoRunnerReferenceGuardTests` asserts the runner boundary; (b) the API host keeps its "bind →
validate → delegate" thinness (ADR 0019) instead of growing workbook-building code; (c) it is not
put in `SmartNet.Catalogos.Infrastructure`, which is SQL adapters over `dbo.*` and would have to be
referenced by the unrelated `tipos-de-cambio` export. Cheaper alternative (`PackageReference`
straight on `SmartNet.Api.csproj`) rejected: that host currently has **zero** NuGet packages, only
project references, and the export code would land in the endpoint file.

There is no `Directory.Packages.props` or `Directory.Build.props` in the repo — every `.csproj` pins
its own version explicitly (e.g. `Microsoft.Data.SqlClient` 7.0.2). Follow that: pin an exact 3.x
version in the new `.csproj`.

> ### ✅ ADR 0021 — RATIFIED by the project owner (2026-08-30)
>
> **`adrs/0021-generacion-de-archivos-excel-en-la-api.md`** — *Generación de archivos Excel en la
> API*. Estado: `Propuesto. Revisión 1. Nace del ítem #22`. Written in Spanish, following the
> `adrs/0020` format (Contexto / Decisión / Alternativas consideradas / Consecuencias / Relacionado).
>
> Its five decisions are the normative source for this design; the summary here must not drift from
> the file:
>
> 1. `DocumentFormat.OpenXml` 3.x (MIT, Microsoft, single package), exact version pinned in the new
>    `.csproj` — the repo has no `Directory.Packages.props`/`Directory.Build.props`, so every project
>    pins its own. It is the **only** file-generation dependency the backend acquires.
> 2. It lives in `SmartNet.Exportacion.Infrastructure`; **no `*.Core` project may reference it**,
>    enforced by a structural test, not a comment (ADR 0019).
> 3. The workbook is built in a `MemoryStream` — a `.xlsx` is a ZIP package and needs a seekable
>    stream — row-at-a-time with `OpenXmlWriter`; ~5-10 MB peak for the ~6,600-row worst case.
> 4. `.xlsx` only, never `.xlsm`; no user input (`q`, `orden`) ever reaches `Content-Disposition`.
> 5. Bounded to catalogs of this size; past ~100k rows the export becomes a background job under a
>    new ADR.
>
> Relacionado: ADR 0002, 0003, 0008, 0009, 0012, 0013, 0016, 0019, BACKLOG #22.
> **No longer a blocker** — slice 1 may start.

**Response mechanics.** `Results.File(bytes, "application/vnd.openxmlformats-officedocument.
spreadsheetml.sheet", fileDownloadName: $"proveedores-{hoy:yyyy-MM-dd}.xlsx")` →
`Content-Disposition: attachment; filename=…`. `hoy` comes from the already-registered `TimeProvider`
singleton (the endpoint may use a clock; Core may not — ADR 0019).

**Honest correction to the brief's word "streamed":** an OOXML file is a ZIP package and
`SpreadsheetDocument.Create` needs a **seekable** stream; the HTTP response body is not seekable.
End-to-end streaming to the socket is therefore impossible for `.xlsx`. The export is built into a
`MemoryStream` and then written. With `OpenXmlWriter` (row-at-a-time, no DOM) the peak for the
worst case — ~6,600 proveedor rows × 3 short columns — is ≈5-10 MB in flight for a ~200 KB file;
plan contable and TC (≤732 rows) are far smaller. Fine for a single-operator app; `Results.File`
also sets a real `Content-Length`, which a chunked stream would not. All validation runs **before**
the first byte, since status cannot change after the response starts.

**Query cost.** Each export re-runs its list query without `OFFSET/FETCH` — for proveedores that is
the same single scan+sort a page already pays, not an extra one; plan contable is already unpaged;
TC is a PK seek bounded to ≤366 days. Default 30 s command timeout is ample; no cap needed at this
data size. If `dbo.Proveedor` ever grows past ~100k rows, export moves to a background job (new ADR).

### D10 — Download boundary safety

The only new boundary is a server-composed file download; the applicable rows:

| Row | Applicable? | Safe behavior | RED test |
|---|---|---|---|
| Filename composition | **Yes** | Filename is `constant + server date`. **No user input** (`q`, `orden`) ever reaches `Content-Disposition` — that would be a header-injection / path-traversal vector | `GET …/exportacion?q=../..%0d%0aX:1` returns `Content-Disposition` equal to the constant form |
| Executable-file classification | **Yes** | Emits `.xlsx` (macro-free format) with `attachment`, never `.xlsm`; the server generates the bytes from DB rows and never echoes uploaded content | Asserted content-type + `attachment` disposition |
| Shell / subprocess / VCS-PR automation / process integration | **N/A** | none exists in this change | — |

Route registration and SPA router config are ordinary authenticated routing, covered by the 401 and
`authGuard` assertions.

## Data Flow

    Sidebar ──route──► catalogos/feature/*-page (container: filtro/orden/pagina/tamanio signals + effect)
             │                          │
             │  export click            │ list
             ▼                          ▼
      descarga-xlsx.ts          catalogos/data-access/*.service (signal server-state)
             │ blob                     │ HttpClient GET /api/*
             ▼                          ▼
      CatalogoEndpoints | TipoCambioEndpoints   (auth · param whitelists · DTO map · TimeProvider)
             │                          │
             ▼                          ▼
      ExportadorXlsx            IProveedorRepository | ICuentaContableRepository | ITipoCambioRepository
      (Exportacion.Infra)               │ SELECT only  (COUNT(*) OVER() — one pass)
             │                          ▼
             └── MemoryStream ──►  dbo.Proveedor · dbo.CuentaContable (ADR 0003) · fact.TipoCambio
                                        ▲ page rows
    *-tabla + tabla-paginador + boton-exportar (presentational) ◄── container passes rows down

## Interfaces / Contracts

| Route | Query | 200 body | Errors |
|---|---|---|---|
| `GET /api/catalogos/proveedores` | `q`, `pagina`, `modo=picker\|catalogo`, `orden`, `direccion`, `tamanio` | picker: `{resultados, hayMas}` · catalogo: `{items, pagina, tamanioPagina, totalRegistros, totalPaginas}` | 400 unknown `modo`/`orden`/`direccion`/`tamanio`; 401 |
| `GET /api/catalogos/plan-contable` | `q?` | `{items:[{cuenta,descripcion,nivel,esHojaImputable}]}` | 401 |
| `GET /api/tipos-cambio` | `desde`, `hasta` (required) | `{items:[{fecha,origen,compra,venta,fechaConsulta}]}` | 400 missing/unparseable/inverted/>366 d; 401 |
| `GET …/{proveedores\|plan-contable}/exportacion`, `GET /api/tipos-cambio/exportacion` | same as their list route | `.xlsx` bytes | same 400s; 401 |

`origen` is serialized as the string `"SBS"`/`"MANUAL"` by an explicit mapper in
`TipoCambioEndpoints` — the `OrigenTipoCambio` enum would otherwise serialize as `0`/`1`. All JSON
payloads camelCase (default `System.Text.Json` policy already in use).

New Core port members (all read-only, all pure signatures):

```csharp
// IProveedorRepository — BuscarAsync untouched
Task<PaginaProveedores> ListarCatalogoAsync(string consulta, string orden, string direccion,
                                            int pagina, int tamanio, CancellationToken ct);
Task<IReadOnlyList<Proveedor>> ListarCatalogoCompletoAsync(string consulta, string orden,
                                            string direccion, CancellationToken ct);  // export
```

`PaginaProveedores` mirrors `PaginaBandeja<T>`'s field set. Plan contable and TC exports reuse
`ListarPlanCompletoAsync` / `ListarHistoricoAsync` — only proveedores needs an export-specific method
(it is the only paged one).

## File Changes

| File | Action | Description |
|---|---|---|
| `catalogos/SmartNet.Catalogos.Core/IProveedorRepository.cs` | Modify | `ListarCatalogoAsync` + `ListarCatalogoCompletoAsync` + `PaginaProveedores`; `BuscarAsync` untouched |
| `catalogos/SmartNet.Catalogos.Core/OrdenProveedor.cs` | Create | Closed sort vocabulary (pure) |
| `catalogos/SmartNet.Catalogos.Infrastructure/SqlProveedorRepository.cs` | Modify | Catalogo mode, `COUNT(*) OVER()`, literal-column sort, `codpro` tiebreak, export query |
| `catalogos/SmartNet.Catalogos.Infrastructure/SqlCuentaContableRepository.cs` | Modify | Optional `q` + `orden`/`direccion` for the export path |
| `exportacion/SmartNet.Exportacion.Infrastructure/{*.csproj, ExportadorXlsx.cs}` | Create | `DocumentFormat.OpenXml` lives here and nowhere else (ADR 0021) |
| `exportacion/SmartNet.Exportacion.Infrastructure.Tests/**` | Create | Workbook round-trip tests |
| `api/SmartNet.Api/CatalogoEndpoints.cs` | Modify | `modo`/`orden`/`direccion`/`tamanio` validation, `plan-contable`, 2 `exportacion` routes, DTOs |
| `api/SmartNet.Api/TipoCambioEndpoints.cs` | Modify | `GET /api/tipos-cambio` + `/exportacion` + validation + origen mapper |
| `api/SmartNet.Api/SmartNet.Api.csproj`, `SmartNet.sln` | Modify | `ProjectReference` to the new project + sln entries |
| `tipos-de-cambio/SmartNet.TiposCambio.Core/ITipoCambioRepository.cs` + SQL adapter | Modify | `ListarHistoricoAsync` |
| `api/SmartNet.Api.Tests/{CatalogoEndpointsTests,TipoCambioEndpointsTests}.cs` | Modify | List + export contract cases |
| `api/SmartNet.Api.Tests/` structural guard | Create | No `*.Core` project references the Excel package |
| `SmartNetWeb/src/app/catalogos/**` | Create | 4 services, 3 models, 3 pages, 3 tables, `tabla-paginador`, `boton-exportar`, `orden.ts` (+ specs) |
| `SmartNetWeb/src/app/app.routes.ts` / `.spec.ts` | Modify | 3 guarded lazy `catalogos/*` routes (additive) |
| `SmartNetWeb/src/app/shared/shell-layout/sidebar/sidebar.{ts,spec.ts,css}` | Modify | 7 → 8 delta + glyph + 3 `/catalogos/*` `ruta` values |
| `SmartNetWeb/src/styles.css` | Modify | `.tabla-catalogo*` primitives, semantic tokens only |
| `SmartNetWeb/src/app/shared/formato.ts` + `inbox-page.ts` | Modify | Move local `yyyy-MM-dd` helper |
| `SmartNet/harnesses/integration-spa-api/README.md` | Modify | Manually record the new flows after re-run |
| `adrs/0021-generacion-de-archivos-excel-en-la-api.md` | **Created (done)** | Ratified by the owner; authored during this design phase, not a slice deliverable |

No new versioned SQL, no new `GRANT`, no `dbo.*` write (ADR 0003 / 0016, CLAUDE.md rules 3-4).

## Testing Strategy

Strict TDD — every row is RED before its production change.

| Layer | What to test | Approach |
|---|---|---|
| Core purity | `OrdenProveedor` + the new port signatures add no clock/DB dependency | Existing `PurityScanTests` stays green |
| Structural | No `*.Core` project references `DocumentFormat.OpenXml`, direct or transitive | New guard test, `NoRunnerReferenceGuardTests` pattern |
| Export unit | Header row text, row count, cell values, date/decimal cells, empty-set workbook still valid | Round-trip: write with `OpenXmlWriter`, reopen with `SpreadsheetDocument.Open` |
| Infra (real DB) | `ListarCatalogoAsync`: lists all incl. `P00000`, `totalRegistros` = full filtered count on **page 1 and page 3**, out-of-range page → `items:[]` + correct total, each of the 3 sort keys × 2 directions, `codpro` tiebreak stable across page boundary, `rucpro` NULLs first ASC, `tamanio` respected. `ListarCatalogoCompletoAsync`: no paging, same order. `ListarHistoricoAsync`: inclusive bounds, both origins, unknown `Origen` filtered, empty range → `[]` | `TestDatabaseFixture` (`fact_test_<guid>`) |
| API (real DB + cookie) | Per list route: 200 shape + camelCase + 401. **Regression**: `modo` absent/`picker` still returns `{resultados,hayMas}`, still excludes `P00000`, still empty for `q=a`. 400 × unknown `modo`/`orden`/`direccion`/`tamanio`. Plan contable `esHojaImputable` true iff `nivel IS NULL`. TC both origins, `fecha desc`, `origen` as string, 400 × (missing `desde`, missing `hasta`, unparseable, inverted, >366 d) | `SmartNetApiFactory` + `HandleCookies=false`, `CatalogoEndpointsTests` style |
| API export | Per export route: 200 with exact `Content-Type`, `Content-Disposition: attachment` + `.xlsx`, non-empty body, and the bytes **open as a workbook** whose row count equals the seeded filtered set + 1 header. 401 without cookie. 400 for the same invalid params as the list route. Filename unaffected by a hostile `q` (D10) | Same factory; test project references the Excel package for read-back only |
| SPA unit (Vitest) | Services: `modo`/`orden`/`direccion`/`pagina`/`tamanio` params sent, envelope mapped, error signal, `descargando` toggles. `tabla-paginador`: prev disabled on page 1, next disabled on last, size change resets to page 1 and emits. `orden.ts`: toggle asc→desc→asc, arrow suffix. `descarga-xlsx`: reads `Content-Disposition`, creates + revokes the object URL. Pages: TC default range = 1st-of-month → today **local, not UTC**; client-side sort + slice for plan/TC; empty/loading | `TestBed` + `HttpTestingController`, mirroring `inbox.service.spec.ts` |
| SPA structural | `sidebar.spec.ts` 8-entry delta; `app.routes.spec.ts` additive guard assertions; `contraste.spec.ts` / `paleta.spec.ts` still green with the new primitives | Existing specs |
| Integration harness | Not auto-covered: after the last slice, re-run `integration-spa-api` and **manually** append the new flows to its report | Explicit task — the harness guardrail forbids unasked test writes |

## Migration / Rollout

No migration. Additive endpoints, routes and one new project; per-slice revert restores prior
behavior. Sidebar entries revert to inert with the sidebar slice. No schema, `GRANT`, or data change
to undo. ADR 0021 is the only non-revertible artifact and **is already ratified and on disk** before
any code lands.

## Delivery Slicing (v2 — `size:exception` accepted by the owner)

Stacked chain, each PR targeting the previous branch. Shared UI lands early (PR3) and no screen ever
ships an inert control — the export endpoint always precedes the button that calls it (#21 dead-link
lesson).

| # | Slice | Authored (est.) |
|---|---|---|
| 1 | Export infrastructure: new project + `ExportadorXlsx` + sln/csproj wiring + Core-purity guard | ~250 |
| 2 | API plan contable: list + `exportacion` routes, DTOs, endpoint tests | ~230 |
| 3 | SPA shared chrome: `tabla-paginador`, `boton-exportar`, `orden.ts`, `descarga-xlsx`, `.tabla-catalogo*` primitives + specs | ~320 |
| 4 | SPA plan contable screen: service, model, page, tabla, route, `nav-plan-contable` link + specs | ~330 |
| 5 | API proveedores catalogo mode: `OrdenProveedor`, 2 port methods, adapter (`COUNT(*) OVER()`, sort, tiebreak), `modo`/`orden`/`tamanio` validation, `exportacion` route + infra/API tests | ~380 |
| 6 | SPA proveedores screen: `CatalogoProveedorService` (server sort/paging), page, tabla, route, `nav-proveedores` link + specs | ~330 |
| 7 | API tipo de cambio: `ListarHistoricoAsync` port + adapter, `GET` + `exportacion` + range validation + tests | ~330 |
| 8 | SPA tipo de cambio screen + sidebar 7 → 8 delta (entry, glyph, spec rewrite) + specs | ~360 |
| 9 | Harness re-run + manual `integration-spa-api` report append | ~40 |

**Decision needed before apply: No** — ADR 0021 is ratified and the owner accepted `size:exception`.
**Chained PRs recommended: Yes** — 9 stacked slices.
**400-line budget risk: Medium** — every slice is under 400, but slice 5 at ~380 has the least
headroom; if it crosses, split its `exportacion` route into a 5b.

**Forecast ≈ 2,570 authored lines** (v1 was ~1,510). Owner accepted `size:exception` (proposal
scope-expansion item 1); no slice exceeds the 400-line review budget. The ADR itself is not counted:
it was authored during this design phase and is already on disk.

## Open Questions

None — all four were ratified by the project owner on 2026-08-30 and folded into the decisions above:

| Question | Resolution |
|---|---|
| ADR 0021 / Excel dependency | **Ratified.** `DocumentFormat.OpenXml` 3.x; `adrs/0021-generacion-de-archivos-excel-en-la-api.md` written |
| SPA route paths | **Grouped `catalogos/`** — `/catalogos/proveedores`, `/catalogos/plan-contable`, `/catalogos/tipo-cambio` (D4) |
| Export sub-path | **`exportacion`** (D9) |
| Plan-contable filter expressed twice | **Accepted** — SPA client filter + server `q` for the export, asserted on both sides (D9) |

Downstream consequence for `sdd-spec`: spec v1's `catalogos/*` route form is now confirmed correct
and needs no change; spec v1's proveedores envelope wording does need the D6 update
(`{items, pagina, tamanioPagina, totalRegistros, totalPaginas}` for `modo=catalogo`, frozen
`{resultados, hayMas}` for the picker).
