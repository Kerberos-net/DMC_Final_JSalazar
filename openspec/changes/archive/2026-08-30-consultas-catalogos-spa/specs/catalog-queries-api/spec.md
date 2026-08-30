# catalog-queries-api Specification

## Purpose

Expose read-only, authenticated endpoints so the SPA can browse the proveedor
catalog, the full chart of accounts (plan contable), and the exchange rate
history (tipo de cambio), and export any of the three as an Excel file. All
access is pure catalog read: no accounting logic, no writes, no new versioned
SQL, no new `GRANT`, no `dbo.*` write, and no `fact.*` domain-table access beyond
the already-granted `fact.TipoCambio` SELECT (CLAUDE.md rules 2-4, ADR 0003).

Scope was expanded by the project owner after the first design pass: proveedores
gains full pagination metadata and server-side sort, and every catalog gains an
Excel export path (owner decisions 6-9, proposal "Scope expansion").

## Requirements

### Requirement: Proveedores endpoint serves both picker and browse-all modes

The system SHALL keep serving `GET /api/catalogos/proveedores` on its existing
route (no new route) and SHALL select behavior by an explicit mode parameter
(exact name is a design decision):

- **Picker mode** (default — mode parameter absent, or set to the picker value):
  behavior and response shape MUST match BACKLOG #18 exactly — minimum query
  length 2, `P00000` excluded, `proveedor LIKE @q OR rucpro LIKE @q`, order by
  `proveedor`, blank/short `q` returns empty `resultados` with no broad scan,
  page size 20, response `{ resultados: [{ codigo, nombre, ruc }], hayMas }`.
  The #18 detalle picker MUST NOT need any code change.
- **Catalogo (browse-all) mode** (mode parameter set to the catalogo value):
  lists ALL proveedores including `P00000`, ignores the minimum-length rule,
  applies the same `q` text filter when `q` is supplied, and supports full
  pagination and server-side sort as defined below. Its response uses the
  project's existing `PaginaBandeja<T>` envelope, not the picker shape.

An unknown mode value SHALL return `400`. An unauthenticated request SHALL return
`401` before any query runs. Rows in both modes carry `codigo` (proveedor code),
`nombre` (razón social), and `ruc` (nullable).

#### Scenario: Catalogo mode lists every proveedor including P00000

- GIVEN an authenticated request in catalogo mode with no `q`
- WHEN `GET /api/catalogos/proveedores` is sent with the catalogo mode value
- THEN the response is `200`, the first page is proveedores ordered by `proveedor`
  ascending, `P00000` is eligible to appear, and the payload is the
  `PaginaBandeja<T>` envelope with `totalRegistros`/`totalPaginas` populated

#### Scenario: Catalogo mode text filter

- GIVEN proveedores whose `proveedor`, `rucpro`, or code contains "ACME"
- WHEN catalogo mode is requested with `q=ACME`
- THEN only matching proveedores are returned, paginated, honoring the active sort

#### Scenario: Picker mode is unchanged

- GIVEN a request with the mode parameter absent and `q=AC`
- WHEN the endpoint is called
- THEN behavior and payload match BACKLOG #18: `P00000` excluded, `q` shorter than
  2 yields empty `resultados` with no scan, response shape is `{ resultados, hayMas }`
  (unchanged — the `PaginaBandeja<T>` envelope applies to catalogo mode only)

#### Scenario: Unknown mode rejected

- GIVEN an authenticated request whose mode parameter is neither the picker nor the catalogo value
- WHEN the endpoint is called
- THEN the response is `400`

#### Scenario: Unauthenticated

- GIVEN no valid session cookie
- WHEN the endpoint is called
- THEN the response is `401` and no query runs

### Requirement: Catalogo mode returns the PaginaBandeja envelope

In catalogo mode the response SHALL use the project's existing `PaginaBandeja<T>`
envelope already consumed by `InboxService`: `{ items: [{ codigo, nombre, ruc }],
pagina, tamanioPagina, totalRegistros, totalPaginas }` (camelCase). `pagina` is
the 1-based page number served, `tamanioPagina` is the page size, `totalRegistros`
is the count of the full filtered set (matching the active `q`), and
`totalPaginas` is derived from `totalRegistros` and `tamanioPagina`.
`totalRegistros` SHALL be computed with `COUNT(*) OVER()` inside the same paged
query — no separate count round-trip or second scan. A page request beyond the
last page SHALL return `200` with an empty `items` and the correct
`totalRegistros` / `totalPaginas`. No name index is added to `dbo.Proveedor`
(ADR 0003).

#### Scenario: Pagination envelope is accurate

- GIVEN catalogo mode with a filter matching 45 proveedores and page size 20
- WHEN `pagina=2` is requested
- THEN `items` holds rows 21-40, `pagina` is 2, `tamanioPagina` is 20,
  `totalRegistros` is 45, and `totalPaginas` is 3

#### Scenario: Page past the end

- GIVEN catalogo mode with a filter matching 10 proveedores
- WHEN a page beyond the available rows is requested
- THEN the response is `200`, `items` is empty, `totalRegistros` is 10

### Requirement: Catalogo mode supports server-side sort

Catalogo mode SHALL accept a sort parameter selecting one of `proveedor`, `ruc`,
or `codigo`, plus a direction (ascending or descending); default is `proveedor`
ascending. The sort SHALL be applied server-side across the full filtered set
before pagination. Picker mode SHALL ignore any sort parameter and keep its fixed
`proveedor` order.

#### Scenario: Sort by RUC descending

- GIVEN catalogo mode with sort `ruc` and descending direction
- WHEN the first page is requested
- THEN rows are ordered by `ruc` descending across the whole filtered set, then paginated

#### Scenario: Invalid sort field rejected

- GIVEN catalogo mode with a sort field outside {`proveedor`, `ruc`, `codigo`}
- WHEN the endpoint is called
- THEN the response is `400`

#### Scenario: Picker mode ignores sort

- GIVEN a picker-mode request that also carries a sort parameter
- WHEN the endpoint is called
- THEN results are still ordered by `proveedor` and the #18 contract is unaffected

### Requirement: Plan contable endpoint returns the full chart in one response

The system SHALL expose a new route `GET /api/catalogos/plan-contable` requiring
the standard `/api/*` authentication. It SHALL return the complete plan in a
single response with NO pagination, as `{ cuenta, descripcion, nivel,
esHojaImputable }` rows (camelCase; `nivel` nullable; `esHojaImputable` boolean,
`true` when `nivel` is null), ordered by `cuenta` ascending. Filtering and column
sorting for this catalog are performed client-side. An unauthenticated request
SHALL return `401`.

#### Scenario: Full plan returned

- GIVEN an authenticated request
- WHEN `GET /api/catalogos/plan-contable` is called
- THEN the response is `200` with every plan account, ordered by `cuenta`, each
  carrying `cuenta`, `descripcion`, `nivel`, `esHojaImputable`, and no paging fields

#### Scenario: Leaf accounts flagged

- GIVEN an account whose `nivel` is null
- WHEN the plan is returned
- THEN that row has `esHojaImputable = true`; non-null `nivel` rows have `false`

#### Scenario: Unauthenticated

- GIVEN no valid session cookie
- WHEN the endpoint is called
- THEN the response is `401`

### Requirement: Tipo de cambio history endpoint with mandatory bounded range

The system SHALL expose a new route `GET /api/tipos-cambio?desde={date}&hasta={date}`
in `TipoCambioEndpoints`, beside the existing `POST`, requiring the standard
`/api/*` authentication. Both `desde` and `hasta` SHALL be REQUIRED. The endpoint
SHALL return `400` when either parameter is missing, unparseable as a date, or
when `hasta` is earlier than `desde`. The inclusive span SHALL be bounded to a
maximum of 366 days; a wider span SHALL return `400`. On success it SHALL return
`200` with rows `{ fecha, origen, compra, venta }` (camelCase; `origen` is
`"SBS"` or `"MANUAL"`), including BOTH origins for a date when both exist, ordered
by `fecha` then `origen`. No origin filter is offered; column sorting is
client-side. The read SHALL be read-only and clock-pure (ADR 0019) and SHALL NOT
touch the #8 Venta-freeze path. An unauthenticated request SHALL return `401`.

#### Scenario: Valid range returns both origins per date

- GIVEN `fact.TipoCambio` has `SBS` and `MANUAL` rows for 2026-08-15
- WHEN `GET /api/tipos-cambio?desde=2026-08-01&hasta=2026-08-31` is sent authenticated
- THEN the response is `200` and includes both the `SBS` and `MANUAL` rows for
  2026-08-15, each carrying its own `origen`, `compra`, `venta`, ordered by `fecha` then `origen`

#### Scenario: Missing parameter rejected

- GIVEN a request with only `desde`
- WHEN the endpoint is called
- THEN the response is `400` and no query runs

#### Scenario: Unparseable or inverted range rejected

- GIVEN `desde=notadate` or `hasta` earlier than `desde`
- WHEN the endpoint is called
- THEN the response is `400`

#### Scenario: Span exceeds the maximum

- GIVEN `desde` and `hasta` more than 366 days apart
- WHEN the endpoint is called
- THEN the response is `400` and no unbounded scan is issued

#### Scenario: Unauthenticated

- GIVEN no valid session cookie
- WHEN the endpoint is called
- THEN the response is `401`

### Requirement: Excel export endpoint per catalog

The system SHALL expose a server-side Excel export for each of the three catalogs
(as three routes or one parameterized route — a design decision), requiring the
standard `/api/*` authentication. Each export SHALL return a real `.xlsx`
document of the FULL filtered result set — every row matching the caller's active
filter/search and range, NOT just the currently visible page — with the
`Content-Type` set to the Excel spreadsheet media type and a `Content-Disposition`
header carrying an attachment filename. Export requests SHALL honor the same
filter, range, and sort parameters as the corresponding JSON endpoint. The `.xlsx`
binary is the sole non-camelCase response in this capability. An unauthenticated
request SHALL return `401` and produce no file. The export depends on a new
backend Excel library; `sdd-design` evaluates whether it needs a new ADR.

#### Scenario: Proveedores export reflects the active filter

- GIVEN an authenticated user browsing proveedores in catalogo mode with `q=ACME`
- WHEN the proveedores Excel export is requested with the same filter
- THEN the response is `200`, the body is a valid `.xlsx`, the `Content-Type` is
  the Excel media type, `Content-Disposition` carries an attachment filename, and
  the sheet contains every matching proveedor, not only the first page

#### Scenario: Tipo de cambio export covers the whole requested range

- GIVEN an authenticated user viewing tipo de cambio for a valid `desde`/`hasta` range
- WHEN the tipo de cambio Excel export is requested for that range
- THEN the `.xlsx` contains every row in range, both origins per date

#### Scenario: Export range validation matches the JSON endpoint

- GIVEN a tipo de cambio export request missing `hasta` or exceeding the 366-day span
- WHEN the endpoint is called
- THEN the response is `400` and no file is produced

#### Scenario: Unauthenticated export

- GIVEN no valid session cookie
- WHEN any catalog export endpoint is called
- THEN the response is `401` and no file is produced

### Requirement: Read-only, partition-respecting access

All catalog-query and export endpoints SHALL read only under the existing
`usr_api` SELECT grants (`dbo.Proveedor`, `dbo.CuentaContable`,
`fact.TipoCambio`). They SHALL NOT write any `dbo.*` object, SHALL NOT add or
require a new grant or versioned SQL script, and SHALL contain no accounting rule
— queries live in an endpoint plus a repository method, never in accounting core.
The new tipo de cambio history read method SHALL be added to
`ITipoCambioRepository` as a read-only, clock-pure port method guarded by
`PurityScanTests`.

#### Scenario: No writes, no schema drift

- GIVEN any request to any catalog-query or export endpoint
- WHEN it executes
- THEN only `SELECT` statements are issued, no `fact.*` domain table other than
  `fact.TipoCambio` is touched, and the diff contains no new SQL script or `GRANT`

### Requirement: Contract-test coverage

Automated `SmartNet.Api.Tests` cases in the `CatalogoEndpointsTests` style (real
DB, real cookie) SHALL cover, for all endpoints: the `401` unauthenticated case
and camelCase payload shape; for proveedores — catalogo-mode listing incl.
`P00000`, catalogo-mode text filter, unchanged picker mode and its `{ resultados,
hayMas }` shape, the `PaginaBandeja<T>` envelope (`items`, `pagina`,
`tamanioPagina`, `totalRegistros`, `totalPaginas`) with `totalRegistros` from
`COUNT(*) OVER()`, server-side sort by each allowed field with direction, `400` on
unknown mode, and `400` on invalid sort field; for plan contable — full unpaged list and the `esHojaImputable` flag;
for tipo de cambio — both-origins result, `400` on missing/unparseable/inverted
params, and `400` on span over the maximum; for each Excel export — `200` with
Excel `Content-Type` and attachment `Content-Disposition`, full-filtered-set
contents, and `401` unauthenticated. The `integration-spa-api` harness report
SHALL be updated manually to record the new flows.

#### Scenario: Contract suite runs

- GIVEN the catalog-queries-api contract tests
- WHEN `dotnet test` runs them
- THEN every listed case is asserted and passes
