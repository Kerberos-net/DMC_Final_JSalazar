# registro-compra-api Specification

## Purpose

Expose read-only, authenticated endpoints so the SPA can browse the libro de
compras (registro de compras) for an accounting period: a paginated list of
`VALIDADA` invoice cabeceras with their vigente asiento, the read-only asiento
line detail for one row, and an API-generated Excel export of the period. All
access is a reporting projection: no accounting rule at the endpoint (ADR 0019),
no writes, no new versioned SQL, no new `GRANT`, no `dbo.*` write, no Python
access (ADR 0003). The `GET /api/asientos/{id}` contract is NOT modified
(proposal Decision 3).

## Requirements

### Requirement: Listado paginado del registro de compras por período

The system SHALL expose `GET /api/registro-compra?periodo={YYYY-MM}&pagina={n}&tamanioPagina={n}`
requiring the standard `/api/*` authentication. `periodo` SHALL be REQUIRED and
SHALL match the `YYYY-MM` format; it filters rows whose
`fact.AsientoContable.FechaContable` falls within that calendar month. A missing
or malformed `periodo` SHALL return `400` as an RFC 9457 problem-details
response. An unauthenticated request SHALL return `401` before any query runs.
The response body SHALL use the project `PaginaRegistroCompra<T>` envelope
`{ items, pagina, tamanioPagina, totalRegistros, totalPaginas }` (camelCase),
with `totalRegistros` computed via `COUNT(*) OVER()` in the same paged query.
The `PaginaRegistroCompra<T>` record carries five wire fields identical to the
generic pagination envelope, but avoids the mandatory `ResumenBandeja` (inbox
five-bucket summary) and the inbox dependency; see BACKLOG #22 precedent
`PaginaProveedores`.

A row SHALL be included only when `fact.Factura.Estado = 'VALIDADA'` AND the
factura's vigente `fact.AsientoContable` is NOT `ANULADO`. Each row (a
`RegistroCompraCabecera`) SHALL carry: `numeroComprobante`, `origenLibro` (the
verbatim `fact.AsientoContable.OrigenLibro` column value, never a hard-coded
`'02'`), `numeroAsiento`, `proveedorCodigo`, `proveedorNombre` (via
`LEFT JOIN dbo.Proveedor` on `proveedorCodigo`; `null` when no matching row so
only the code is shown), `glosa`, `fechaContable`, `tipoCambioVenta`, `basePEN`,
`igvPEN`, `netoPEN`, and `asientoContableId`.

#### Scenario: Período con facturas validadas

- GIVEN `fact` holds validated invoices with a vigente non-`ANULADO` asiento whose `FechaContable` is in 2026-08
- WHEN `GET /api/registro-compra?periodo=2026-08&pagina=1&tamanioPagina=20` is sent authenticated
- THEN the response is `200`, `items` holds the first 20 cabecera rows ordered deterministically, and `totalRegistros` / `totalPaginas` reflect the full filtered set

#### Scenario: Excluye no validadas y asientos anulados

- GIVEN an invoice that is not `VALIDADA`, and another whose vigente asiento is `ANULADO`, both with `FechaContable` in the period
- WHEN the listado is requested for that period
- THEN neither invoice appears in `items` and neither is counted in `totalRegistros`

#### Scenario: Período sin filas

- GIVEN no qualifying rows have `FechaContable` in 2026-01
- WHEN `GET /api/registro-compra?periodo=2026-01` is sent authenticated
- THEN the response is `200` with `items: []` and `totalRegistros: 0` (NOT `404`)

#### Scenario: Período malformado

- GIVEN a request with `periodo=2026-8`, `periodo=agosto`, or no `periodo`
- WHEN the endpoint is called
- THEN the response is `400` with an RFC 9457 problem-details body and no query runs

#### Scenario: No autenticado

- GIVEN no valid session cookie
- WHEN the endpoint is called
- THEN the response is `401` and no query runs

#### Scenario: Proveedor sin fila en dbo.Proveedor

- GIVEN a row whose `proveedorCodigo` has no matching `dbo.Proveedor` record
- WHEN the listado is returned
- THEN that row carries its `proveedorCodigo` and `proveedorNombre: null`

### Requirement: Detalle de líneas de un asiento del registro

The system SHALL expose `GET /api/registro-compra/{asientoContableId}` requiring the
standard `/api/*` authentication. It SHALL return the same cabecera fields as a
listado row plus `lineas[]`, each line carrying `orden`, `bloque`, `tipo`,
`debe`, `haber`, `cuentaCodigo`, and `cuentaDescripcion` (camelCase). The
response SHALL be read-only. The endpoint SHALL return `404` when the asiento
does not exist OR is not part of the libro de compras (its factura is not
`VALIDADA`, or the asiento is `ANULADO`). An asiento that qualifies but has no
persisted detail lines SHALL return `200` with `lineas: []`.

#### Scenario: Detalle de un asiento del período

- GIVEN `asientoContableId` belongs to a `VALIDADA` invoice with a vigente non-`ANULADO` asiento
- WHEN `GET /api/registro-compra/{asientoContableId}` is sent authenticated
- THEN the response is `200` with the cabecera fields and `lineas[]` in `orden` sequence, each carrying `bloque`, `tipo`, `debe`, `haber`, `cuentaCodigo`, `cuentaDescripcion`

#### Scenario: Asiento fuera del libro

- GIVEN `asientoContableId` exists but its factura is not `VALIDADA`, or the asiento is `ANULADO`
- WHEN the endpoint is called
- THEN the response is `404`

#### Scenario: Asiento inexistente

- GIVEN `asientoContableId` matches no asiento
- WHEN the endpoint is called
- THEN the response is `404`

#### Scenario: Asiento sin líneas persistidas

- GIVEN a qualifying asiento with zero detail lines
- WHEN the endpoint is called
- THEN the response is `200` with `lineas: []`

#### Scenario: No autenticado

- GIVEN no valid session cookie
- WHEN the endpoint is called
- THEN the response is `401`

### Requirement: Exportación del período a Excel generada en la API

The system SHALL expose `GET /api/registro-compra/export?periodo={YYYY-MM}`
requiring the standard `/api/*` authentication. It SHALL return a real `.xlsx`
document generated server-side (ADR 0021, precedent BACKLOG #22), with the
`Content-Type` set to the Excel spreadsheet media type and a `Content-Disposition`
header carrying an attachment filename. The sheet SHALL contain exactly the same
rows as the listado for that `periodo` (same row predicate and same cabecera
fields), not only a single page. `periodo` validation SHALL match the listado
endpoint (`400` RFC 9457 on missing/malformed). An unauthenticated request SHALL
return `401` and produce no file.

#### Scenario: Export del período

- GIVEN an authenticated user viewing the registro de compras for 2026-08
- WHEN `GET /api/registro-compra/export?periodo=2026-08` is requested
- THEN the response is `200`, the body is a valid `.xlsx`, `Content-Type` is the Excel media type, `Content-Disposition` carries an attachment filename, and the sheet holds every listado row for that period

#### Scenario: Export con período malformado

- GIVEN a request with a missing or malformed `periodo`
- WHEN the export endpoint is called
- THEN the response is `400` with an RFC 9457 problem-details body and no file is produced

#### Scenario: Export no autenticado

- GIVEN no valid session cookie
- WHEN the export endpoint is called
- THEN the response is `401` and no file is produced

### Requirement: Acceso de solo lectura que respeta la partición

All registro-compra endpoints SHALL read through a dedicated read-only Core port
`IRegistroCompraRepository` (mirroring the inbox `SqlBandejaRepository` pattern),
with a SQL adapter joining `fact.AsientoContable` + `fact.Factura` +
`LEFT JOIN dbo.Proveedor` (+ `fact.AsientoContableDetalle` for the detail read).
The port SHALL contain no accounting rule and SHALL be guarded by
`PurityScanTests`. The endpoints SHALL issue only `SELECT` statements, SHALL NOT
write any `dbo.*` object, and SHALL NOT add or require a new `GRANT` or versioned
SQL script (the existing `fact_api` grants from `008` already cover these
SELECTs). The `GET /api/asientos/{id}` contract SHALL remain unchanged.

#### Scenario: Sin escrituras ni deriva de esquema

- GIVEN any request to any registro-compra endpoint
- WHEN it executes
- THEN only `SELECT` statements are issued and the diff contains no new SQL script, no new `GRANT`, and no change to `api-asientos` / `GET /api/asientos/{id}`

### Requirement: Cobertura de tests de contrato

Automated `SmartNet.Api.Tests` cases in the `CatalogoEndpointsTests` style (real
DB, real cookie) SHALL cover: `401` unauthenticated and camelCase payload shape
for every endpoint; listado — period filter over `FechaContable`, the row
predicate (only `VALIDADA` + vigente non-`ANULADO`), the `PaginaRegistroCompra<T>`
envelope with `totalRegistros` from `COUNT(*) OVER()`, empty-period `200` with
`totalRegistros: 0`, and `400` on malformed `periodo`; detalle — `200` with
`lineas[]`, `404` for an asiento outside the libro, and `200` with `lineas: []`
for an asiento with no lines; export — `200` with Excel `Content-Type` and
attachment `Content-Disposition` and the same rows as the listado, plus `400` on
malformed `periodo`. The `integration-spa-api` harness report SHALL be updated
manually to record the new flow.

#### Scenario: Suite de contrato corre

- GIVEN the registro-compra-api contract tests
- WHEN `dotnet test` runs them
- THEN every listed case is asserted and passes
