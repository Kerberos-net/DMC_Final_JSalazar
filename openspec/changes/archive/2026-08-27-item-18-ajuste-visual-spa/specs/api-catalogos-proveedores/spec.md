# api-catalogos-proveedores Specification

New capability pulled into BACKLOG #18 by user decision: the functional proveedor picker
needs a search endpoint. No such endpoint exists today (investigation
`item-18/proveedor-picker-investigation`).

## Purpose

Expose a read-only, authenticated HTTP search over `dbo.Proveedor` so the SPA proveedor
picker can resolve a `proveedorCodigo` by name fragment or RUC. This is pure catalog
read: no accounting logic, no writes, no domain-table access.

## Requirements

### Requirement: Authenticated proveedor search endpoint

The system MUST expose `GET /api/catalogos/proveedores?q={term}&pagina={n}` requiring the
same authentication as the other `/api/*` endpoints. It MUST return a JSON body with a
`resultados` array of objects `{ codigo, nombre, ruc }` (string `codigo` = `codpro`,
string `nombre` = `proveedor`, nullable string `ruc` = `rucpro`) plus a `hayMas` boolean
(or equivalent total/offset signal) indicating whether further pages exist. Results MUST
be ordered by `nombre` ascending. An unauthenticated request MUST get `401`.

#### Scenario: Search by name fragment

- GIVEN proveedores whose `proveedor` contains "ACME"
- WHEN `GET /api/catalogos/proveedores?q=ACME` is sent authenticated
- THEN the response is `200` with matching proveedores ordered by `nombre`, each carrying `codigo`, `nombre`, `ruc`

#### Scenario: Search by RUC

- GIVEN a proveedor with `rucpro` `20123456789`
- WHEN `GET /api/catalogos/proveedores?q=20123456789` is sent authenticated
- THEN the response includes that proveedor (match is `proveedor LIKE @q OR rucpro LIKE @q`)

#### Scenario: Unauthenticated request rejected

- GIVEN no valid credentials
- WHEN the endpoint is called
- THEN the response is `401` and no query runs

### Requirement: Read-only, partition-respecting access

The endpoint MUST read `dbo.Proveedor` under the existing `usr_api` `SELECT` grant only.
It MUST NOT write any `dbo.*` object, MUST NOT create or require a new grant or versioned
SQL script, and MUST NOT read or join any `fact.*` domain table. It MUST contain no
accounting rule; the query lives in an endpoint plus a repository method, never in
accounting core (CLAUDE.md rules 2 and 3, ADR 0003).

#### Scenario: No writes and no domain-table access

- GIVEN any request to the endpoint
- WHEN it executes
- THEN only a `SELECT` against `dbo.Proveedor` is issued and no `fact.*` table is touched

### Requirement: Pagination

The endpoint MUST page results with a fixed server-side page size. `pagina` defaults to
the first page when absent or less than the first index. Requesting a page past the end
MUST return `200` with an empty `resultados` array and `hayMas=false`.

#### Scenario: Second page

- GIVEN a `q` matching more proveedores than one page
- WHEN `pagina=2` is requested
- THEN the next slice is returned, ordered by `nombre`, with `hayMas` reflecting whether more remain

#### Scenario: Page past the end

- GIVEN a `q` with three total matches and a page size of twenty
- WHEN `pagina=5` is requested
- THEN the response is `200` with empty `resultados` and `hayMas=false`

### Requirement: Empty, short, and no-match queries

When `q` is absent, empty, or shorter than a minimum length (design decides, at least 1),
the endpoint MUST return `200` with empty `resultados` and MUST NOT run an unbounded scan.
A well-formed `q` with no matches MUST return `200` with empty `resultados`.

#### Scenario: Missing q

- GIVEN no `q` parameter
- WHEN the endpoint is called authenticated
- THEN the response is `200` with empty `resultados` and no broad table scan

#### Scenario: No matches

- GIVEN `q=ZZZNOEXISTE`
- WHEN the endpoint is called
- THEN the response is `200` with empty `resultados` and `hayMas=false`

### Requirement: `P00000` (Varios) excluded from search results

The generic fallback proveedor `P00000` ("Varios") MUST NOT appear in search results, so
a human searching cannot pick it by accident; it stays reachable only through the existing
generic-proveedor path. (OPEN QUESTION — confirm against product intent; if product wants
it shown-but-marked instead, this requirement flips to "returned with an explicit
`esGenerico` flag".)

#### Scenario: P00000 filtered out

- GIVEN `q=Varios` which would textually match `P00000`
- WHEN the endpoint is called
- THEN `P00000` is not in `resultados`

### Requirement: Contract-test coverage

Automated API contract tests MUST cover: match by name fragment, match by RUC, ordering by
`nombre`, pagination (page two and page past end), empty/short `q`, no-match, the `P00000`
exclusion rule, and the `401` unauthenticated case.

#### Scenario: Contract suite runs

- GIVEN the api-catalogos-proveedores contract test suite
- WHEN `dotnet test` runs it
- THEN every listed case is asserted and passes

## Out of Scope / Flagged Decisions

- A nonclustered index on `dbo.Proveedor(proveedor)` is an external-catalog (`dbo.*`) object per ADR 0003. It is OUT OF SCOPE and left as a flagged decision; `LIKE` over ~6600 rows is acceptable without it.
- No versioned SQL and no new grant are added by this change.
