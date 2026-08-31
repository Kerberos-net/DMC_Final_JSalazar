# registro-compra-spa Specification

## Purpose

Provide a read-only Angular consulta screen for the libro de compras (registro de
compras) by accounting period: a server-side paginated table of `VALIDADA`
invoice cabeceras with their vigente asiento, a read-only expand into the asiento
line detail, a pure presentation badge that flags a cabecera↔detalle
inconsistency, and an "Exportar a Excel" action that downloads the period file
from the API. The screen is query-only — no create/edit/anular/reactivar control
— and it never calls domain / núcleo contable code (ADR 0019). It reuses the
`catalogos/` feature structure (container-presentational, signal data-access
service, typed models).

## Requirements

### Requirement: Ruta protegida hija del ShellLayout

The screen SHALL be registered as a lazy `loadComponent` child route of the
`ShellLayout` route with `canActivate: [authGuard]`, following the same pattern
as the `catalogos/*` routes. The route addition SHALL be additive so
`app.routes.spec.ts` `arrayContaining` assertions continue to hold.

#### Scenario: Acceso autenticado

- GIVEN a viewer with a valid session
- WHEN they navigate to the registro de compra route
- THEN the screen loads inside `ShellLayout`

#### Scenario: Acceso sin sesión

- GIVEN a viewer with no valid session
- WHEN they navigate to the registro de compra route
- THEN `authGuard` redirects them away from the screen

### Requirement: Filtro por período y tabla paginada en servidor

The screen SHALL offer a single period filter (`YYYY-MM`), defaulting to the
current accounting month computed from the local date (not UTC). Changing the
period SHALL re-query the API and reset to page 1. The screen SHALL render a
server-side paginated table of cabecera rows consuming the API
`PaginaRegistroCompra<T>` envelope (`items`, `pagina`, `tamanioPagina`, `totalRegistros`,
`totalPaginas`), with a pagination footer and rows-per-page control bound to
`tamanioPagina`. Columns SHALL surface at least `numeroComprobante`,
`origenLibro`, `numeroAsiento`, proveedor (`proveedorNombre` or, when null, the
`proveedorCodigo`), `fechaContable`, `basePEN`, `igvPEN`, and `netoPEN`. A
malformed period SHALL be prevented client-side or surfaced as a non-blocking
validation message when the API returns `400`.

#### Scenario: Carga por defecto del mes contable actual

- GIVEN the viewer opens the screen with no period chosen
- WHEN the screen initializes
- THEN it requests the listado for the current accounting month (local date) and shows page 1 of the results

#### Scenario: Cambiar de período

- GIVEN the table shows results for one period
- WHEN the viewer selects a different `YYYY-MM` period
- THEN the screen re-queries the API for that period and resets to page 1

#### Scenario: Paginación en servidor

- GIVEN a period whose `totalRegistros` exceeds one page
- WHEN the viewer advances to the next page
- THEN the screen requests that page from the API and renders its `items`

### Requirement: Detalle de líneas del asiento en solo lectura

Expanding a table row SHALL show the asiento line detail (from
`GET /api/registro-compra/{asientoContableId}`) as a read-only view of each line's
cuenta (`cuentaCodigo` / `cuentaDescripcion`) and its débito (`debe`) and crédito
(`haber`) amounts. The detail view SHALL offer no editing, anulación, or
reactivación control.

#### Scenario: Expandir una fila

- GIVEN a cabecera row in the table
- WHEN the viewer expands it
- THEN the screen fetches and displays that asiento's lines read-only, showing cuenta, débito and crédito per line

#### Scenario: Asiento sin líneas

- GIVEN an expanded row whose asiento has no detail lines
- WHEN the detail loads
- THEN the view shows an empty-detail message and no error

### Requirement: Marca visual de inconsistencia cabecera↔detalle

The screen SHALL show an inconsistency badge derived purely from amounts already
returned by the API, computed with a `computed()` signal and never by calling
domain code. The badge SHALL light ONLY when at least one of these holds:

- `round(basePEN + igvPEN, 2) != round(netoPEN, 2)` (cabecera), or
- `round(sum(debe), 2) != round(sum(haber), 2)` over the asiento `lineas[]`
  (cabecera↔detalle).

The comparison SHALL be exact to the céntimo with no epsilon tolerance, because
`REGLAS.md` §6 states there is no tolerance and §7.1 requires the asiento to
balance globally. Percepción SHALL NOT participate in the cabecera check: per
`REGLAS.md` §5 / §10.4 the percepción line (`401131`) only affects the abono to
the proveedor, so `netoPEN` is base + IGV without percepción; in the
débito/haber check percepción appears on both sides and cancels. The rule applies
unchanged to boleta / operación no gravada, where `igvPEN = 0` and
`basePEN = netoPEN` (§10.2). This is a display/presentation check only — it does
not touch `SmartNet.Contable.Core` / núcleo contable (ADR 0019).

#### Scenario: Fila consistente

- GIVEN a row where `round(basePEN + igvPEN, 2) == round(netoPEN, 2)` and, for its lines, `round(sum(debe), 2) == round(sum(haber), 2)`
- WHEN the row renders
- THEN no inconsistency badge is shown

#### Scenario: Descuadre de cabecera

- GIVEN a row where `round(basePEN + igvPEN, 2) != round(netoPEN, 2)`
- WHEN the row renders
- THEN the inconsistency badge lights

#### Scenario: Descuadre débito/crédito

- GIVEN an expanded asiento where `round(sum(debe), 2) != round(sum(haber), 2)`
- WHEN the detail renders
- THEN the inconsistency badge lights

#### Scenario: Percepción no dispara la marca de cabecera

- GIVEN a gravada factura with a percepción line whose `netoPEN` equals `basePEN + igvPEN` (percepción excluded)
- WHEN the row renders
- THEN no cabecera inconsistency badge is shown

#### Scenario: La marca no invoca código de dominio

- GIVEN the inconsistency badge computation
- WHEN the component is inspected
- THEN it is a `computed()` over returned amounts only, with no import of or call into núcleo contable / `SmartNet.Contable.Core`

### Requirement: Exportar a Excel del período

The screen SHALL provide an "Exportar a Excel" action that downloads the `.xlsx`
for the currently selected period from `GET /api/registro-compra/export?periodo=`.
The file SHALL be produced by the API (not generated client-side).

#### Scenario: Descargar el Excel del período

- GIVEN the viewer is browsing the registro de compras for a period
- WHEN they activate "Exportar a Excel"
- THEN the browser downloads the period `.xlsx` served by the API export endpoint

### Requirement: Estados de carga, error y vacío

The screen SHALL show a loading indicator while a request is in flight, a
non-blocking error message when a request fails (without showing stale data as
current), and an explicit empty state when the period has no rows
(`totalRegistros: 0`).

#### Scenario: Período sin resultados

- GIVEN the API returns `items: []` and `totalRegistros: 0` for the period
- WHEN the screen renders
- THEN it shows an empty-state message instead of an empty grid with no context

#### Scenario: Fallo de la API

- GIVEN a listado request that fails
- WHEN the screen handles the error
- THEN it shows a non-blocking error message and does not present stale results as current

### Requirement: Pantalla de solo consulta con patrón inbox

The feature SHALL use one data-access signal service (private writable signal +
`asReadonly()`, `cargando` / `error` signals), a container component owning the
period / paging / expand signals, and presentational `ui/` components typed to
the API contract. It SHALL expose no create, edit, delete, save, anular, or
reactivar control; the only requests it issues are `GET` (including the export
download).

#### Scenario: Sin controles de mutación

- GIVEN the rendered registro de compra screen and its expanded detail
- WHEN the UI is inspected
- THEN there is no control that creates, edits, deletes, anula, or reactiva an asiento or factura, and every request is a `GET`
