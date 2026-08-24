# Spec: bandeja (BACKLOG #13)

New capability spec — `GET /api/bandeja` had no dedicated spec file before this item (it existed
only as the #7-shaped partial endpoint, referenced in passing by `api-facturas`). This is a full
spec, not a delta.

## Capability: `bandeja`

### Requirement: `GET /api/bandeja` filters by estado, date range, and proveedor

The endpoint MUST accept optional query parameters `estado`, `desde`, `hasta`, `proveedor`,
`pagina`, and `orden`. Parameters MAY be combined; each supplied filter narrows the result set
independently (AND semantics).

#### Scenario: Combined filters narrow the result
- GIVEN rows exist across multiple estados, dates, and proveedores
- WHEN `GET /api/bandeja?estado=PENDIENTE&desde=2026-01-01&hasta=2026-01-31&proveedor=P001` is called
- THEN only rows matching `estado=PENDIENTE`, within the date range, and belonging to `proveedor=P001` are returned

#### Scenario: All filter parameters omitted
- GIVEN no query parameters are supplied
- WHEN `GET /api/bandeja` is called
- THEN the endpoint applies the default-view rule (see "Default view shows only non-terminal items")

#### Scenario: `proveedor` matches no rows
- GIVEN a `proveedor` value with no matching rows
- WHEN `GET /api/bandeja?proveedor={value}` is called
- THEN the response is `200 OK` with an empty `items` array and `totalRegistros: 0`, not an error

### Requirement: Results are paginated, 20 items per page

The endpoint MUST paginate with a fixed page size of 20 and return the envelope
`{ items, pagina, tamanioPagina, totalRegistros, totalPaginas }`.

#### Scenario: First page returned by default
- GIVEN more than 20 matching rows exist
- WHEN `GET /api/bandeja` is called without `pagina`
- THEN the response returns `pagina: 1`, `tamanioPagina: 20`, up to 20 `items`, and correct `totalRegistros`/`totalPaginas`

#### Scenario: Requested page exceeds available pages
- GIVEN `totalPaginas` is N
- WHEN `GET /api/bandeja?pagina={N+1}` is called
- THEN the response is `200 OK` with an empty `items` array and the correct `totalRegistros`/`totalPaginas`, not an error

### Requirement: Every row declares its origen; combination happens server-side

Per ADR 0008/0003, each row MUST carry an explicit `origen` field (`FACTURA` or `INCIDENCIA`).
`SqlBandejaRepository` MUST perform the combination of `fact.Factura`-derived rows and
`fact.ProcesamientoError`-derived rows entirely in .NET; the Angular client MUST NOT issue
separate calls and merge them client-side.

#### Scenario: Response contains both origins in one call
- GIVEN pending processing errors and validated facturas both exist
- WHEN `GET /api/bandeja` is called
- THEN the single response contains rows with `origen: "FACTURA"` and rows with `origen: "INCIDENCIA"`, each correctly tagged

### Requirement: Panel de errores projects ProcesamientoError history for both origins

The endpoint MUST expose `fact.ProcesamientoError` history (`Mensaje`, `Clasificacion`,
`OcurridoEn`) for `INCIDENCIA` rows and for `FACTURA` rows that were promoted but later failed
reprocessing. `.NET` MUST only read `fact.ProcesamientoError`, never write it (ADR 0003).

#### Scenario: INCIDENCIA row includes its full error history
- GIVEN a `ProcesamientoId` with three `fact.ProcesamientoError` entries
- WHEN that row is returned as `origen: "INCIDENCIA"`
- THEN the response includes all three entries ordered by `OcurridoEn`, each with `Mensaje` and `Clasificacion`

#### Scenario: FACTURA row with no error history omits an empty panel
- GIVEN a promoted `FACTURA` row with zero `fact.ProcesamientoError` entries
- WHEN that row is returned
- THEN the error-history field is an empty list (or absent), and no consumer is required to render a broken/empty panel

#### Scenario: FACTURA row promoted then failed again shows the failure
- GIVEN a promoted factura whose later reprocess attempt wrote a new `fact.ProcesamientoError` row
- WHEN that row is returned as `origen: "FACTURA"`
- THEN its error-history field includes that entry

### Requirement: Default view (no filters) shows only non-terminal items

With no filters supplied, the endpoint MUST restrict results to non-terminal items: pending
documents and open incidencias. Already-validated `FACTURA` rows with no open error MUST NOT
appear unless the caller supplies an explicit `estado` filter selecting that state.

#### Scenario: Default call excludes fully validated facturas
- GIVEN a mix of pending, open-incidencia, and fully-validated-with-no-error rows
- WHEN `GET /api/bandeja` is called with no filters
- THEN only pending and open-incidencia rows are returned

#### Scenario: Explicit estado filter includes validated facturas
- GIVEN the same mix of rows
- WHEN `GET /api/bandeja?estado=PROMOVIDO` is called
- THEN validated (promoted) rows are returned
