# Design: Bandeja shell navigation + enriched bandeja data (BACKLOG #21)

## Technical Approach

Three chained PR slices over one change. PR1 is pure SPA chrome with **zero API contact**:
`ShellLayout` gains a presentational `Sidebar` child plus a `SidebarService` that mirrors
`TemaService` exactly (pure read fn + signal service + `localStorage`). PR2 widens the existing
`GET /api/bandeja` projection in place — the `SqlBandejaRepository` multi-resultset batch grows one
`dbo.Proveedor` LEFT JOIN and one **new, filter-independent** aggregate resultset, and the two
records gain fields; no new endpoint, no new SQL script, no new grant. PR3 consumes PR2's merged
contract: the `.ts` mirror, `InboxService`, the `inbox-list` row and a new `inbox-resumen` card
strip. ADR 0019 is N/A — nothing here is accounting logic; `REGLAS.md` is not touched.

## Architecture Decisions

### D1 — Envelope shape: `resumen` sibling on `PaginaBandeja<T>`

**Choice**: `PaginaBandeja<T>` gains a required positional `ResumenBandeja Resumen`. No new endpoint.
**Alternatives considered**: separate `GET /api/bandeja/resumen`.
**Rationale**: the batch, the connection and the auth path already exist; the aggregate becomes one
extra resultset on a round trip the SPA is already making. Cards can never disagree with the list
because both come from one snapshot. Cost is recomputation on every filter/pagination change — at
10–50 invoices/day (~3.6k–18k `fact.InboxEvent` rows/year) that is one pass over a small table plus
one index seek per row on `IX_ProcesamientoError_ProcesamientoId` (created by `018`): single-digit
ms. Option B would add a full HTTP round trip plus a `fact.Sesion` ticket lookup per card refresh —
strictly more work for the same numbers, plus a second spec surface and a mid-refresh inconsistency
window. `PaginaBandeja<T>` is bandeja-only (verified: 3 `.cs` sites, all in `SqlBandejaRepository`/
`IBandejaRepository`/`BandejaEndpointsTests`), so a **required** positional param is safe and turns
every stale construction site into a compile error — the compiler as the first RED test.

### D2 — The wider aggregate: own predicate, no filters, 3rd resultset

**Choice**: a new resultset placed **between** the errors resultset and the conditional fallback
`COUNT(*)`, with its own `FROM`/`WHERE`. It ignores `@estado`, `@desde`, `@hasta` and `@proveedor`
entirely — it has **no `WHERE` clause at all**. Buckets come from a single `CASE` whose `WHEN` order
is the chip's first-match precedence.

```sql
SELECT
    SUM(CASE WHEN b.Bucket = 'PENDIENTE'  THEN 1 ELSE 0 END) AS Pendientes,
    SUM(CASE WHEN b.Bucket = 'VALIDADA'   THEN 1 ELSE 0 END) AS Validadas,
    SUM(CASE WHEN b.Bucket = 'ERROR'      THEN 1 ELSE 0 END) AS ConError,
    SUM(CASE WHEN b.Bucket = 'ALERTA'     THEN 1 ELSE 0 END) AS Alertas,
    SUM(CASE WHEN b.Bucket = 'DESCARTADA' THEN 1 ELSE 0 END) AS Descartadas,
    COUNT(*) AS Total
FROM (
    SELECT CASE
        WHEN ie.EstadoConsumo = 'DESCARTADO' THEN 'DESCARTADA'
        WHEN EXISTS (SELECT 1 FROM fact.ProcesamientoError pe
                     WHERE pe.ProcesamientoId = ie.ProcesamientoId) THEN 'ERROR'
        WHEN f.FacturaId IS NOT NULL
             AND (f.EsProveedorGenerico = 1 OR f.PosibleDuplicado = 1) THEN 'ALERTA'
        WHEN ie.EstadoConsumo = 'PROMOVIDO' THEN 'VALIDADA'
        ELSE 'PENDIENTE'
    END AS Bucket
    FROM fact.InboxEvent ie
    LEFT JOIN fact.Factura f ON f.FacturaId = ie.FacturaId
) b;
```

**Alternatives considered**: (a) reuse `FiltroWhere` — rejected, `Validadas` is then structurally 0
because the default predicate excludes `PROMOVIDO`/`DESCARTADO`; (b) a 4th, separate round trip —
rejected, same reasoning as D1; (c) append the aggregate after the conditional fallback — rejected,
it would put an always-present resultset behind an `IF`-guarded one and force the reader to branch.
**Rationale**: placing it 3rd keeps the invariant "the conditional resultset is always last", so the
reader stays: rows → `NextResult` → errores → `NextResult` → resumen → (only if the page was empty)
`NextResult` → fallback count. `Descartadas` and `Total` are returned even though only 4 cards
render: they are what makes the partition invariant
(`Pendientes+Validadas+ConError+Alertas+Descartadas = Total`) assertable in a test, and they cost
nothing.

### D2b — ⚠ The `OBSOLETO` asymmetry (discovered while designing)

**Choice**: the aggregate's `ERROR` bucket uses `EXISTS(... WHERE pe.ProcesamientoId = ...)` with
**no `Clasificacion` filter**.
**Alternatives considered**: `AND pe.Clasificacion <> 'OBSOLETO'`, matching `FiltroWhere`.
**Rationale**: the two existing surfaces already disagree, and this is not a new divergence — it is
an existing one that the cards must not amplify. `FiltroWhere` filters `<> 'OBSOLETO'` (which rows
deserve attention in the default view), but the errors resultset selects **all** `ProcesamientoError`
rows for the page, and `chipEstadoDe()` branches on `item.errores.length > 0`. Since the locked
decision is "cards use the row Estado chip's first-match precedence", the aggregate must match the
chip, not `FiltroWhere`. A row whose only error history is `OBSOLETO` therefore shows the `Error`
chip **and** is counted in `Con error` — consistent. If the product later wants `OBSOLETO` excluded,
`chipEstadoDe()` and this `CASE` must change **in the same change** (project rule 1: no silent
divergence). Record this in the `bandeja` spec.

### D3 — The `dbo.Proveedor` join: projection only, never the filter

**Choice**: `LEFT JOIN dbo.Proveedor pr ON pr.codpro = f.ProveedorCodigo`, added **only** to the page
projection (resultset #2). It is added to neither the `@pagina` `INSERT`, nor `FiltroWhere`, nor the
fallback `COUNT(*)`, nor the aggregate.
**Alternatives considered**: joining in the paging `INSERT` too (harmless but pointless extra work),
or extending the `proveedor` filter to also match the display name.
**Rationale**: the existing `proveedor` filter matches identity (`f.RucProveedor` / `f.ProveedorCodigo`
/ `JSON_VALUE(ie.Payload,'$.comprobante.rucProveedor')`). Touching `FiltroWhere` would silently
change filter semantics and break existing green tests — an explicit non-goal. Keeping the join out
of the `INSERT` also keeps the page/fallback totals provably identical (the comment on `FiltroWhere`
demands both agree on exactly which rows match). `codpro` and `f.ProveedorCodigo` are **both
`CHAR(6)`** so there is no padding mismatch. `pr.proveedor` is `NVARCHAR(80)` → no `TrimEnd()` needed
(unlike `ProveedorCodigo`). It is `NULL` when the row has no factura, or when `codpro` is absent from
the external catalog; `P00000`/"Varios" resolves normally to its catalog name.

**Grants — no new script needed.** Verified in source, not assumed:
`008_usuarios_y_permisos.sql:149` = `GRANT SELECT ON OBJECT::dbo.Proveedor TO fact_api`;
`018_permiso_lectura_procesamiento_error.sql:19` = `GRANT SELECT ON OBJECT::fact.ProcesamientoError
TO fact_api` (after `REVOKE`, with `INSERT/UPDATE/DELETE` re-DENY'd). `PermissionMatrixTests.cs:435`
already asserts `SELECT COUNT(*) FROM dbo.Proveedor` succeeds for that principal. **Therefore: no new
`NNN_*.sql`, no `rollback/NNN_down.sql`, no ADR 0016 delta.** The change is read-only over already
granted objects — ADR 0003 is respected: no `dbo.*` write anywhere in shipped code.

**How it is proven under the real login**: `SqlBandejaRepositoryTests` already has
`ListarAsync_RunsAsUsrApi_ProvingTheD1PermissionGrant` using
`TestDatabaseFixture.ExecuteAsUserAsync`. Extend that test's fixture so the promoted row's `codpro`
exists in `dbo.Proveedor` and assert `ProveedorNombre`. `TestDatabaseFixture:173-176` already
**creates** `dbo.Proveedor (codpro CHAR(6), proveedor NVARCHAR(80), coddocide, rucpro)` but does not
seed it; the test seeds it with a plain `INSERT` (test-only DML, precedent:
`CatalogoEndpointsTests.cs:26`, `DboCatalogSeedHelper.cs:47`). `DboWriteLintTests` lints **schema
scripts**, not test code, so this is not a violation.

### D4 — Record shape: 6 nullable fields on the shared base

**Choice**: all 6 fields go on `BandejaItemBase` (`.ts`) / the flat `BandejaItem` record (`.cs`), all
nullable, inserted after `RucProveedor`. `Resumen` is appended last on `PaginaBandeja<T>`.
**Alternatives considered**: putting them on the `'FACTURA'` variant of the discriminated union only.
**Rationale**: they are not all non-null on a `FACTURA` row — `Numero` is `NULL`-able in
`005_negocio.sql:30`, and `ProveedorNombre` is `NULL` whenever `codpro` is missing from the external
catalog. Typing them non-null on the `FACTURA` variant would be a lie the compiler enforces. The
existing `.ts` header already commits to "flat wire shape … narrowed here on the client via
`origen`", so this follows the ratified #13 pattern. It also makes the "`—` for factura-only cells"
rule a template concern (`?? '—'`) that correctly covers a `FACTURA` row with no `numero`, not just
`INCIDENCIA` rows. Field names are 1:1 `.cs` PascalCase ↔ `.ts` camelCase; all six are Spanish
accounting nouns matching the SQL columns verbatim (CONVENTIONS), no accents in identifiers.

### D5 — Sidebar as its own presentational component + no pre-bootstrap applier

**Choice**: `shared/shell-layout/sidebar/sidebar.{ts,html,css}` — presentational, `OnPush`,
`input.required<boolean> colapsado`, `output<void> alternar`, `RouterLink` + `RouterLinkActive`.
`ShellLayout` stays the only injector (`SidebarService`, `TemaService`). New
`shared/sidebar.service.ts` mirrors `tema.service.ts` structurally.
**Alternatives considered**: sidebar markup inline in `ShellLayout`.
**Rationale**: (1) the 4kB `anyComponentStyle` budget is **per component** — splitting keeps both
`shell-layout.css` and `sidebar.css` comfortably under, where one merged file with a full sidebar
plus glyphs would approach it; (2) it preserves the ratified container/presentational split
(services injected in the container only); (3) `shell-layout.spec.ts` stays about the shell and the
sidebar gets its own focused spec.

**No pre-bootstrap applier.** `aplicarTemaInicial()` exists in `main.ts` because `index.html`'s body
paints with the wrong background **before** Angular boots. The sidebar has no pre-bootstrap markup
at all — it does not exist in the DOM until `ShellLayout` renders. `SidebarService` reads
`localStorage` synchronously in its field initializer, i.e. before the first render, so there is no
collapse flash to prevent. Adding an applier would be pure ceremony; `main.ts` is untouched.

`SidebarService` contract (ADR 0009 — private writable signal + `asReadonly()`, no state library):

```ts
export type EstadoSidebar = 'expandido' | 'colapsado';
const CLAVE_ALMACENAMIENTO = 'fact.sidebar';
export function leerEstadoAlmacenado(storage: Pick<Storage,'getItem'> = localStorage): EstadoSidebar;
@Injectable({ providedIn: 'root' })
export class SidebarService {
  readonly estado: Signal<EstadoSidebar>;
  readonly colapsado: Signal<boolean>;   // computed
  alternar(): void;                      // writes localStorage + sets the signal
}
```

Any stored value outside the allowlist — tampered, empty, wrong case, `null` — falls back to
`'expandido'`, never throws, never reaches the DOM raw (same rule `tema.service.ts` documents as its
"client input trust" threat-matrix row).

### D5b — Glyphs: 3 inline `<div>`s, no `shared/icon/` primitive

**Choice**: three glyphs (`bandeja`, `configuracion`, `chevron` for the toggle) as inline
`<div class="glifo glifo--x" aria-hidden="true">` in `sidebar.html`, shaped in `sidebar.css`. Hard
budget: **max 1 element + its `::before`/`::after` per glyph**. Existing inline `<svg>` in
`.alerta`/`.banner` stays untouched (noted debt, out of scope).
**Alternatives considered**: a `shared/icon/` component with a name→class map.
**Rationale**: three glyphs is below the threshold where the abstraction pays — the map plus the
component would be strictly more code than three CSS classes, and DESIGN.md's rule is about the
markup primitive (`<div>`, not `<svg>`), not about componentization. Revisit at ≥6 glyphs. If a
faithful gear cannot be drawn inside the 1+2 element budget, use a three-dot "ajustes" mark rather
than blowing the budget or reaching for `<svg>`.
**Accessibility when collapsed**: the label text is hidden by a CSS class, never `display:none` on
the anchor, and each `<a>` always carries `aria-label` — the accessible name survives collapse.

### D6 — "Text on sidebar": NO new token, zero `styles.css` delta

**Choice**: reuse `--texto-principal` / `--texto-secundario` for sidebar labels, `--accento-suave` +
`--accento-texto` (or `--accento` + `--accento-contraste`) for the active item, `--borde-hairline`
for the divider, `--fondo-sidebar` for the surface. **No new token; `styles.css` is not modified in
this change; `spa-design-tokens` gets no delta.**
**Alternatives considered**: minting a `--texto-sidebar` pair.
**Rationale**: this is already proven, not estimated. `contraste.spec.ts:20-25` lists
`--fondo-sidebar` as one of the four `SUPERFICIES`, and `TINTAS_TEXTO` (which includes
`--texto-principal`, `--texto-secundario`, `--accento-texto`) is asserted ≥4.5:1 **over every
surface in both themes** by the currently-green suite. `--accento-texto` over `--accento-suave` and
`--accento-contraste` over `--accento` are likewise already asserted (`PARES_TINTA_FONDO`, and the
"etiqueta blanca sobre el fill" case). So AA on the sidebar is guaranteed by construction with zero
new assertions. PR1's obligation is purely negative: **introduce no color literal** — `paleta.spec.ts`
parses `styles.css` and would go RED on a new hue or an un-aliased literal, and component CSS must
reference tokens only.

### D7 — Row layout: additive, not a rewrite

**Choice**: keep every existing column and cell behaviour (`Recibido` = `creadoEn`, `Estado` chip,
`Detalle` cell with `motivoDescarte` / drill-in link / errores `<details>`, `Indicadores`,
`Acciones`) and **add** five columns: `F. emisión`, `Proveedor`, `Tipo`, `Número`, `Monto`. Final
order: `Recibido | F. emisión | Proveedor | Tipo | Número | Monto | Estado | Detalle | Indicadores |
Acciones`; the empty-state `colspan` goes 5 → 10. Monto is right-aligned with
`font-variant-numeric: tabular-nums` and renders `{{ monto }} {{ moneda }}`. Wrapper gets
`overflow-x: auto`.
**Alternatives considered**: replacing `Detalle`/`Recibido` to hold the column count down.
**Rationale**: every removal breaks an existing green test and expands scope past "add the §2
fields". `Recibido` specifically must stay: `desde`/`hasta` filter on `ie.CreadoEn`, so removing the
column would make the filter's effect invisible — a real usability regression. Ten columns is wide;
horizontal scroll is the accepted tradeoff, and it is reversible later without touching the API.

**Comprobante map** (client-side, API keeps returning the code): a module-level pure function
`nombreComprobante(codigo: string | null): string` beside `chipsDe()`/`chipEstadoDe()` in
`inbox-list.ts` — exactly the precedent those two set. `01 → Factura`, `03 → Boleta`,
`07 → Nota de crédito`. Unknown non-null code renders the raw code (never blank, never a guess);
`null` renders `—`.

### D8 — Summary cards

**Choice**: new presentational `inbox/ui/inbox-resumen/inbox-resumen.{ts,html,css}`,
`input.required<ResumenBandeja>()`, **no `output`** (display-only, locked). Renders exactly 4 cards:
`Pendientes` / `Validadas` / `Con error` / `Alertas`. `InboxService` gains
`private resumenSignal = signal<ResumenBandeja | null>(null)` + `readonly resumen = ...asReadonly()`;
`InboxPage` renders `@if (resumen(); as r)`. `Descartadas`/`Total` travel on the wire but are not
rendered.
**Rationale**: ADR 0009 service pattern verbatim; the `null`-before-first-load state avoids showing
four zeros during the initial fetch.

### D9 — BACKLOG.md treatment

**Choice**: leave item #21's checkbox line byte-identical; append **one** indented line directly
under it, in PR1 (the slice that delivers the unbacklogged part):

    (Incluye el shell de navegación lateral — sidebar con `Bandeja` y `Configuración`,
    colapsable y persistente — plegado aquí en lugar de abrir un ítem propio: se entrega
    junto con estos datos y solo cubre destinos con ruta existente. Cambio SDD
    `item-21-bandeja-shell-nav`.)

**Alternatives considered**: opening a new BACKLOG item for the shell.
**Rationale**: user-selected. The shell is a small gap against the already-ratified `DESIGN.md`, and
the two ship together — a separate item would imply an independent delivery that does not exist.

## Data Flow

    PR1 (no API):
      localStorage 'fact.sidebar' ──→ SidebarService (signal) ──→ ShellLayout
                                            ▲                        │ [colapsado]
                                            └──── alternar() ────── Sidebar (presentational)

    PR2/PR3:
      InboxPage effect (estado/orden/desde/hasta/proveedor/pagina signals)
            │
            ▼  InboxService.cargar()  →  GET /api/bandeja?...
      BandejaEndpoints ──→ SqlBandejaRepository (ONE batch, ONE connection, usr_api)
            │
            ├─ #1 page rows   ← FiltroWhere  (filters APPLY)   + LEFT JOIN dbo.Proveedor
            ├─ #2 errores     ← page's ProcesamientoIds
            ├─ #3 resumen     ← NO WHERE  (filters DO NOT apply)   ◄── global cards
            └─ #4 fallback COUNT(*)  (only when the page was empty and pagina > 1)
            │
            ▼  PaginaBandeja<BandejaItem>{ items[], ..., resumen }
      InboxService: itemsSignal + resumenSignal
            ├──→ InboxList     (10 columns, chip precedence unchanged)
            └──→ InboxResumen  (4 cards, GLOBAL — unaffected by the active filter)

## File Changes

| PR | File | Action | Description |
|----|------|--------|-------------|
| 1 | `src/app/shared/sidebar.service.ts` | Create | `EstadoSidebar`, pure `leerEstadoAlmacenado()`, signal service, `fact.sidebar` |
| 1 | `src/app/shared/sidebar.service.spec.ts` | Create | RED first: default, persistence, tampered-value fallback |
| 1 | `src/app/shared/shell-layout/sidebar/sidebar.{ts,html,css}` | Create | Presentational nav: 2 items, hairline divider, toggle, 3 `<div>` glyphs |
| 1 | `src/app/shared/shell-layout/sidebar/sidebar.spec.ts` | Create | RED first: nav items/order, divider, `aria-label` when collapsed |
| 1 | `src/app/shared/shell-layout/shell-layout.{ts,html,css}` | Modify | Injects `SidebarService`, renders `<app-sidebar>`, grid shell |
| 1 | `src/app/shared/shell-layout/shell-layout.spec.ts` | Modify | RED first: sidebar present, toggle wired, state survives re-create |
| 1 | `BACKLOG.md` | Modify | One indented note under #21 (D9) |
| 1 | `openspec/specs/spa-shell-nav/spec.md` | Create | New capability |
| 2 | `inbox/SmartNet.Inbox.Core/IBandejaRepository.cs` | Modify | 6 fields on `BandejaItem`, new `ResumenBandeja`, `Resumen` on `PaginaBandeja<T>` |
| 2 | `inbox/SmartNet.Inbox.Infrastructure/SqlBandejaRepository.cs` | Modify | `dbo.Proveedor` join + 6 columns in resultset #2; new resultset #3; reader |
| 2 | `inbox/SmartNet.Inbox.Infrastructure.Tests/SqlBandejaRepositoryTests.cs` | Modify | RED first: enriched columns, aggregate partition, `usr_api` proof |
| 2 | `api/SmartNet.Api.Tests/BandejaEndpointsTests.cs` | Modify | RED first: envelope carries `resumen`; filters do not change it |
| 2 | `openspec/specs/bandeja/spec.md` | Modify | Enriched fields, aggregate, D2b `OBSOLETO` note |
| 2 | *(none)* `SmartNetBD/schema/**` | **Unchanged** | No new grant needed — see D3 |
| 2 | `api/SmartNet.Api/BandejaEndpoints.cs` | **Unchanged** | Thin delegator; passes the widened envelope through untouched |
| 3 | `src/app/inbox/models/bandeja-item.model.ts` | Modify | 6 fields on `BandejaItemBase`, `ResumenBandeja`, `resumen` on `PaginaBandeja<T>` |
| 3 | `src/app/inbox/data-access/inbox.service.{ts,spec.ts}` | Modify | `resumenSignal`/`resumen`; spec's `paginaVacia` literal gains `resumen` |
| 3 | `src/app/inbox/ui/inbox-list/inbox-list.{ts,html,css,spec.ts}` | Modify | 5 columns, `nombreComprobante()`, `colspan` 5→10, tabular monto |
| 3 | `src/app/inbox/ui/inbox-resumen/inbox-resumen.{ts,html,css,spec.ts}` | Create | 4 display-only cards |
| 3 | `src/app/inbox/feature/inbox-page/inbox-page.{ts,html,spec.ts}` | Modify | Wire `<app-inbox-resumen [resumen]="...">` |
| 3 | `openspec/specs/spa-visual-bandeja/spec.md` | Modify | Unfreeze the #13 query/service/columns **and** update Out of Scope (rule 1) |

`src/styles.css`, `openspec/specs/spa-design-tokens/spec.md`, `main.ts`, `app.routes.ts`: unchanged
(D6, D5).

## Interfaces / Contracts

```csharp
public sealed record ResumenBandeja(
    int Pendientes, int Validadas, int ConError, int Alertas, int Descartadas, int Total);

public sealed record BandejaItem(
    long InboxEventId, string Origen, long ProcesamientoId, string EstadoConsumo,
    DateTime CreadoEn, long? FacturaId, string? ProveedorCodigo, string? RucProveedor,
    string? ProveedorNombre,        // NEW  dbo.Proveedor.proveedor   NVARCHAR(80)
    string? TipoComprobante,        // NEW  fact.Factura.TipoComprobante CHAR(2), code only
    string? Numero,                 // NEW  fact.Factura.Numero        VARCHAR(20)
    decimal? TotalOrig,             // NEW  fact.Factura.TotalOrig      DECIMAL(18,2)
    string? Moneda,                 // NEW  fact.Factura.Moneda         CHAR(3)
    DateOnly? FechaEmision,         // NEW  fact.Factura.FechaEmision   DATE
    IndicadoresFactura? Indicadores, string? MotivoDescarte,
    IReadOnlyList<ErrorProcesamiento> Errores, DateTime? ReprocesarDisponibleEn);

public sealed record PaginaBandeja<T>(
    IReadOnlyList<T> Items, int Pagina, int TamanioPagina, int TotalRegistros, int TotalPaginas,
    ResumenBandeja Resumen);        // NEW, required — stale call sites become compile errors
```

`.ts` mirror: the same six as `readonly …: T | null` on `BandejaItemBase`, plus
`export interface ResumenBandeja { readonly pendientes: number; readonly validadas: number;
readonly conError: number; readonly alertas: number; readonly descartadas: number;
readonly total: number }` and `readonly resumen: ResumenBandeja` on `PaginaBandeja<T>`.
`fechaEmision` is `string | null` (`yyyy-MM-dd`, `DateOnly`'s default JSON form); `totalOrig` is
`number | null`.

## Testing Strategy (Strict TDD — every row is RED first)

| PR | Suite | RED test |
|----|-------|----------|
| 1 | `sidebar.service.spec.ts` | default is `expandido` with empty storage; `alternar()` persists to `fact.sidebar`; tampered/unknown value falls back to `expandido` without throwing |
| 1 | `sidebar.spec.ts` | renders exactly `Bandeja` then divider then `Configuración`; `routerLink` targets `/bandeja` and `/configuracion`; collapsed state keeps the accessible name |
| 1 | `shell-layout.spec.ts` | sidebar rendered inside the shell; toggle emits and flips `colapsado`; a fresh instance re-reads the persisted state |
| 1 | `app.routes.spec.ts`, `paleta.spec.ts`, `contraste.spec.ts` | **unchanged and green** — the proof that no route and no token moved |
| 1 | `npm run build` | `anyComponentStyle` under 4kB for both `shell-layout.css` and `sidebar.css` |
| 2 | `SqlBandejaRepositoryTests` | promoted row returns all 6 enriched fields, `ProveedorNombre` from a seeded `dbo.Proveedor`; row whose `codpro` is absent → `ProveedorNombre` null; `INCIDENCIA` row → all 6 null |
| 2 | `SqlBandejaRepositoryTests` | buckets partition: sum of 5 = `Total`; a `PROMOVIDO` row with no errors and no flags **is counted in `Validadas`** (the anti-regression for the "structurally 0" risk) |
| 2 | `SqlBandejaRepositoryTests` | `resumen` is byte-identical across `estado=PENDIENTE`, `desde`/`hasta`, `proveedor` and `pagina=2` — filters do not touch the aggregate |
| 2 | `SqlBandejaRepositoryTests` | precedence: `DESCARTADO` + error history counts as `Descartadas`, not `ConError`; error + `esProveedorGenerico` counts as `ConError`, not `Alertas` |
| 2 | `SqlBandejaRepositoryTests` (`ExecuteAsUserAsync`) | the whole widened batch runs impersonating `usr_api` — the ADR 0003 gate for `dbo.Proveedor` + `fact.ProcesamientoError` |
| 2 | `BandejaEndpointsTests` | `GET /api/bandeja` JSON carries `resumen` with all six camelCase keys, and the 6 enriched keys per item |
| 3 | `inbox.service.spec.ts` | `resumen()` is null before load, populated after; enriched fields survive the round trip |
| 3 | `inbox-list.spec.ts` | §2 columns present in order; `INCIDENCIA` row shows `—` in all 5 new cells; `nombreComprobante('01') === 'Factura'` and an unknown code renders raw; empty state `colspan="10"` |
| 3 | `inbox-page.spec.ts` | exactly 4 cards; the numbers come from `resumen`, **not** derived from `items()`; changing a filter does not change the card numbers |
| 3 | `integration-spa-api` harness | contract seam for the widened envelope; BLOCKED (never a fabricated PASS) if local SQL Server is absent |

## Threat Matrix

**N/A for the routing/shell/subprocess boundary** — this change adds no route, no guard, no redirect,
no shell command, no subprocess, no VCS/PR automation and no executable-file classification.
`app.routes.ts` is untouched and `app.routes.spec.ts` staying green unchanged is the evidence; the
sidebar only renders `routerLink`s to two destinations that already exist.

One row is carried forward from the codebase's own precedent (`tema.service.ts` documents it):

| Threat | Applicable | Expected behavior | RED test |
|---|---|---|---|
| Client input trust — `localStorage['fact.sidebar']` is attacker-writable | Yes | Any value outside `{'expandido','colapsado'}` — tampered, empty, wrong case, absent — falls back to `'expandido'`; never throws; never reaches the DOM raw | `sidebar.service.spec.ts` tampered-value case (PR1) |

## Migration / Rollout

No migration. No new SQL script, no new grant, no schema DDL, no data backfill, no feature flag —
the DB change is zero and the API change is an additive read projection. Rollback is per slice, in
reverse dependency order: PR3 (SPA falls back to the minimal row and no cards), then PR2 (envelope
returns to its pre-#21 shape), then PR1 (header-only `ShellLayout`). PR1 is independently revertible
at any time. PR3 must not merge before PR2 is merged.

## Open Questions

None. Every question delegated to this phase is decided above.
