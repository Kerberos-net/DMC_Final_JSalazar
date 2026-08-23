# Delta for tipos-de-cambio (BACKLOG #11)

Adds the HTTP wrapper the `carga-manual` capability (item #4) explicitly deferred. No change to
`nucleo-dominio-tipos-cambio`, `lookup-tasa-vigente`, or `scraper-sbs`.

## ADDED Requirements

### Requirement: `POST /api/tipos-cambio` exposes carga-manual over HTTP with problem+json errors

The endpoint MUST call the existing `SmartNet.TiposCambio.Infrastructure` insert (item #4) for a
`MANUAL` rate and translate its outcomes to RFC 9457 `application/problem+json`, per ADR 0008. It
is the resolution path for the "factura en moneda extranjera sin tipo de cambio" `409` case
(spec `api-facturas`).

#### Scenario: Loading a MANUAL rate for an uncovered date succeeds
- **Given** `fact.TipoCambio` has no row for `Fecha = '2026-08-15'`
- **When** `POST /api/tipos-cambio` is called with `{ "fecha": "2026-08-15", "tasa": 3.85 }`
- **Then** the response is `201 Created` (or `200 OK`) and a row
  `(Fecha=2026-08-15, Origen=MANUAL, Tasa=3.85)` exists afterward

#### Scenario: Loading MANUAL for a date that already has a MANUAL row returns 409
- **Given** a `Origen=MANUAL` row already exists for `Fecha = '2026-08-15'`
- **When** `POST /api/tipos-cambio` is called for the same date
- **Then** the response is `409 Conflict`, `application/problem+json`, translating the composite
  `(Fecha, Origen)` PK violation — no silent overwrite

#### Scenario: Malformed body returns 400
- **Given** a request body missing `tasa` or with a non-positive `tasa`
- **When** `POST /api/tipos-cambio` is called
- **Then** the response is `400 Bad Request`, `application/problem+json`, and no row is inserted

#### Scenario: A SBS row already exists for the date — MANUAL load still succeeds independently
- **Given** `fact.TipoCambio` has a `Origen=SBS` row for `Fecha = '2026-08-15'`
- **When** `POST /api/tipos-cambio` loads a `MANUAL` rate for the same date
- **Then** the `MANUAL` row is inserted alongside the `SBS` row (different composite key) — lookup
  continues to prefer `SBS` per the existing `nucleo-dominio-tipos-cambio` rule
