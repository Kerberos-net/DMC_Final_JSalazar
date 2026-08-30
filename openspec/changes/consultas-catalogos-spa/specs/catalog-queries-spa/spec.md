# catalog-queries-spa Specification

## Purpose

Give the SPA three query-only catalog screens under `catalogos/` — proveedores,
plan contable, and tipo de cambio — reachable as lazy `ShellLayout` child routes
behind `authGuard`. The screens follow the `inbox/` container/presentational +
signals pattern (data-access signal service per screen, presentational `ui/`
table components, `models/` per contract). They are strictly read-only: no
create, edit, or delete affordance anywhere.

Owner scope expansion after the first design pass: every screen keeps the
canvas-faithful full pagination footer and gains sortable column headers and an
"Exportar a Excel" action (owner decisions 6-8).

## Requirements

### Requirement: Three guarded lazy catalog routes

The SPA SHALL register three routes as lazy `loadComponent` children of the
`ShellLayout` route, each with `canActivate: [authGuard]`: `catalogos/proveedores`,
`catalogos/plan-contable`, and `catalogos/tipo-cambio`. Routes SHALL be added
additively so existing `app.routes.spec.ts` `arrayContaining` assertions still
hold, and every non-empty child route SHALL carry the auth guard.

#### Scenario: Routes resolve to screens

- GIVEN an authenticated session
- WHEN the user navigates to `/catalogos/proveedores`, `/catalogos/plan-contable`,
  or `/catalogos/tipo-cambio`
- THEN the corresponding screen loads inside `ShellLayout`

#### Scenario: Unauthenticated visitor is blocked

- GIVEN no valid session
- WHEN the user navigates to any `catalogos/*` route
- THEN `authGuard` redirects to `/login` and the screen does not load

#### Scenario: Routes are additive

- GIVEN `app.routes.spec.ts`
- WHEN it runs
- THEN its `arrayContaining` route assertions and per-child auth-guard assertions still pass

### Requirement: Proveedores screen — paginated browse-all table with search, sort, export

The proveedores screen SHALL show a table with columns código, razón social, and
RUC, plus a search box. It SHALL load data from `GET /api/catalogos/proveedores`
in catalogo (browse-all) mode via a data-access signal service, showing all
proveedores (including `P00000`). The service consumes the `PaginaBandeja<T>`
envelope (`items`, `pagina`, `tamanioPagina`, `totalRegistros`, `totalPaginas`).
The screen SHALL render a full pagination footer with `Anterior` / `Siguiente`
controls, a `Página X de Y` indicator (`pagina` of `totalPaginas`), and a
rows-per-page selector bound to `tamanioPagina`.
Column headers for código, razón social/`proveedor`, and RUC SHALL be sortable;
activating a header SHALL re-query the server with the corresponding sort field
and direction and reset to page 1. Typing in the search box SHALL re-query the
server with the `q` filter and reset to page 1. An "Exportar a Excel" action
SHALL download the proveedores Excel export for the current search and sort,
covering the full filtered set, not just the visible page.

#### Scenario: Initial paginated list

- GIVEN an authenticated user opens the proveedores screen
- WHEN the screen loads
- THEN the first page of proveedores appears with código, razón social, and RUC,
  and the footer shows `Página 1 de {totalPaginas}` with `Anterior` disabled

#### Scenario: Pagination navigates server pages

- GIVEN the first page is shown and `totalPaginas` is greater than 1
- WHEN the user activates `Siguiente` or changes rows-per-page
- THEN the screen re-queries with the new `pagina`/`tamanioPagina` and the footer updates

#### Scenario: Sortable header re-queries server-side

- GIVEN the list is shown ordered by `proveedor`
- WHEN the user activates the RUC column header
- THEN the screen re-queries with sort `ruc` and its direction, resets to page 1,
  and repeated activation toggles ascending/descending

#### Scenario: Search filters server-side

- GIVEN the list is shown
- WHEN the user types a term in the search box
- THEN the screen re-queries with `q`, resets to page 1, and keeps the active sort

#### Scenario: Export downloads the full filtered set

- GIVEN the user has an active search term and sort
- WHEN the user activates "Exportar a Excel"
- THEN an `.xlsx` file downloads containing every matching proveedor, not only the current page

### Requirement: Plan contable screen — full list with client-side filter and sort

The plan contable screen SHALL fetch the complete plan once from `GET
/api/catalogos/plan-contable` and render a table with columns código and
denominación. A filter box SHALL narrow the visible rows client-side over the
already-loaded full list (matching código or denominación) with no new request.
Column headers SHALL be sortable client-side over the loaded list. An "Exportar a
Excel" action SHALL download the plan contable Excel export of the full filtered
set.

#### Scenario: Full plan renders

- GIVEN an authenticated user opens the plan contable screen
- WHEN the screen loads
- THEN every plan account is shown with código and denominación, no server pagination control

#### Scenario: Client-side filter and sort

- GIVEN the full plan is displayed
- WHEN the user types in the filter box or activates a column header
- THEN rows are narrowed or reordered client-side with no new HTTP request

#### Scenario: Export downloads the plan

- GIVEN the plan contable screen is open
- WHEN the user activates "Exportar a Excel"
- THEN an `.xlsx` file of the plan downloads

### Requirement: Tipo de cambio screen — date-range filter with month-to-date defaults

The tipo de cambio screen SHALL provide a fecha inicial and a fecha final filter.
On first load the defaults SHALL be the first day of the current month (fecha
inicial) and today (fecha final), computed from the local date, not UTC. It SHALL
call `GET /api/tipos-cambio` with `desde` and `hasta` from the filter and render
a table with columns fecha, origen, compra, and venta, showing both `SBS` and
`MANUAL` rows for a date when both exist. There SHALL be no origin selector.
Column headers SHALL be sortable client-side over the loaded range. An "Exportar
a Excel" action SHALL download the tipo de cambio Excel export for the current
range. When the API returns `400` the screen SHALL surface a non-blocking
validation message and SHALL NOT show stale rows as current.

#### Scenario: Default month-to-date view

- GIVEN an authenticated user opens the tipo de cambio screen on 2026-08-30
- WHEN the screen loads
- THEN `desde` is 2026-08-01, `hasta` is 2026-08-30, and the table lists fecha,
  origen, compra, venta including both origins per date

#### Scenario: User changes the range

- GIVEN the screen is showing the default range
- WHEN the user sets a new fecha inicial and fecha final and applies
- THEN the screen re-queries with the new `desde`/`hasta` and refreshes the table

#### Scenario: Client-side sort

- GIVEN the range results are shown
- WHEN the user activates a column header
- THEN rows reorder client-side with no new request

#### Scenario: Invalid range handled

- GIVEN the user picks a fecha final earlier than fecha inicial, or a span over the API maximum
- WHEN the query runs and the API responds `400`
- THEN the screen shows a validation message and does not present stale data as current

### Requirement: Screens are query-only and follow the inbox pattern

Each screen SHALL use one data-access signal service (reusing the existing
`ProveedorService` only where it does not clobber the picker's state — a new
service is expected for the browse screen), a container that owns
filter/paging/sort signals and drives fetches, presentational `ui/` table
components, and `models/` typed to the endpoint contract. No screen SHALL render
a create, edit, delete, or save control, and no screen SHALL issue a non-`GET`
request (Excel export downloads are `GET`).

#### Scenario: No mutation affordance

- GIVEN any of the three catalog screens
- WHEN its UI and network calls are inspected
- THEN only `GET` requests are made and no create/edit/delete/save control exists
