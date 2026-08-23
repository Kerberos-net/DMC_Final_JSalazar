# Spec: Tipos de cambio (BACKLOG #4)

New capabilities: domain/infrastructure layer over the already-shipped `fact.TipoCambio` table
(item #1, `007_publicacion.sql`/`008_usuarios_y_permisos.sql`). No new DDL. See `proposal.md`
Out of Scope.

## Non-Goals (explicit scope boundaries)

- **No new DDL/GRANT changes.** Table, composite PK `(Fecha, Origen)`, CHECK, and
  `fact_api`/`fact_worker` grants already exist.
- **No freezing into the asiento or `Factura.TipoCambioAplicado`.** ADR 0018 pt. 2's
  congelamiento-al-confirmar is item #8's job. This item only exposes the vigente rate for #8 to
  read and freeze later.
- **No scheduler/polling/retry orchestration.** The Python scraper is single-run; recurring
  execution is deferred to #5 (Gmail ingestion).
- **No HTTP endpoint or `409` response.** ADR 0018 pt. 3's "no data → factura no se abre" is
  exposed here only as a typed domain/repository absence result, not as an API contract — that's
  item #11.
- **No accountant ratification of REGLAS.md §12 pts. 1 (tasa venta) and 5 (NC hereda TC).** This
  item's completion does not imply those two points are final.
- **No dedicated discrepancy audit table.** REGLAS.md §6 says a late SBS publication "no pisa la
  fila manual en silencio: registra la discrepancia"; per the proposal's approved assumption, this
  item satisfies that only by persisting both rows queryably (SBS wins on lookup) — a dedicated
  discrepancy signal/flag is not built here.

---

## Capability: `nucleo-dominio-tipos-cambio`

### Requirement: `SmartNet.TiposCambio.Core` selects the vigente rate by SBS>MANUAL priority, with zero DB/HTTP/clock dependency (ADR 0019)

Per the proposal's already-resolved decision, when rows exist for both `Origen='SBS'` and
`Origen='MANUAL'` on the same `Fecha`, the selection rule MUST return the SBS row. The rule MUST
be a pure function taking the candidate rows for a date as input — no DB, HTTP, or system clock —
verified with the same PurityScanTests pattern used for `SmartNet.Catalogos.Core`/`Auth.Core`.

#### Scenario: SBS row wins when both origins exist for the same date
- **Given** `fact.TipoCambio` has a row for `Fecha = '2026-08-14'` with `Origen = 'SBS'` and
  another with `Origen = 'MANUAL'`
- **When** the Core selection rule is applied to both candidate rows
- **Then** it returns the `SBS` row

#### Scenario: MANUAL row is used when SBS did not publish for that date
- **Given** only a `Origen = 'MANUAL'` row exists for the date
- **When** the Core selection rule is applied
- **Then** it returns the `MANUAL` row

#### Scenario: SmartNet.TiposCambio.Core does not reference infrastructure types
- **Given** the compiled `SmartNet.TiposCambio.Core` assembly
- **When** scanning referenced assemblies/type usages
- **Then** it does not reference `Microsoft.Data.SqlClient`, `Microsoft.AspNetCore.*`, or
  `System.Net.Http`, and does not call `DateTime.Now`/`DateTime.UtcNow`

---

## Capability: `carga-manual`

### Requirement: `SmartNet.TiposCambio.Infrastructure` can insert a MANUAL rate row for a date not yet covered

Per ADR 0018 pt. 3 / REGLAS.md §6, when SBS did not publish for a fecha de emisión, an operator
loads the rate manually so the factura can be worked. The repository MUST insert a row with
`Origen = 'MANUAL'` for the given `(Fecha, Tasa)`.

#### Scenario: Loading a MANUAL rate for a date with no prior row
- **Given** `fact.TipoCambio` has no row for `Fecha = '2026-08-15'`
- **When** the repository inserts a MANUAL rate of `3.85` for that date
- **Then** a row `(Fecha = '2026-08-15', Origen = 'MANUAL', Tasa = 3.85)` exists afterward

#### Scenario: Loading MANUAL for a date that already has a MANUAL row is rejected by the existing composite PK, not silently overwritten
- **Given** a `Origen = 'MANUAL'` row already exists for `Fecha = '2026-08-15'`
- **When** the repository attempts to insert another MANUAL row for the same date
- **Then** the operation fails on the existing `(Fecha, Origen)` primary key — it does not update
  the prior value in place

---

## Capability: `lookup-tasa-vigente`

### Requirement: The repository resolves the vigente rate for a fecha de emisión, exposing which origin was used

Per ADR 0018 pt. 1/pt. 2 and REGLAS.md §6, the venta rate for the fecha de emisión is what the
asiento (#8) will eventually freeze. The lookup MUST apply the Core SBS>MANUAL rule when both
exist and MUST return a typed result exposing the origin used (`SBS` or `MANUAL`), per the
proposal's approved assumption that origin is needed for future audit/#8 use.

#### Scenario: Lookup for a date with only an SBS row returns that rate with origin SBS
- **Given** `fact.TipoCambio` has one row for `Fecha = '2026-08-14'`, `Origen = 'SBS'`,
  `Tasa = 3.802`
- **When** the repository looks up the vigente rate for that date
- **Then** it returns `Tasa = 3.802` with origin `SBS`

#### Scenario: Lookup for a date with both origins returns the SBS rate and origin
- **Given** `fact.TipoCambio` has both an `SBS` and a `MANUAL` row for the same `Fecha`
- **When** the repository looks up the vigente rate for that date
- **Then** it returns the `SBS` row's `Tasa`, with origin `SBS`

### Requirement: Lookup for a date with no rate returns a typed absence result, never `null` or a generic exception

Per ADR 0018 pt. 3, "si no existe fila de tipo de cambio para la fecha de emisión, la factura no
se abre para edición." This item exposes that as a typed domain/repository result the future #11
endpoint can translate into `409` — it does not build the endpoint itself.

#### Scenario: No SBS and no MANUAL row for the fecha de emisión
- **Given** `fact.TipoCambio` has no row for `Fecha = '2026-08-16'`
- **When** the repository looks up the vigente rate for that date
- **Then** it returns a typed "sin tipo de cambio" result — not `null`, not a thrown generic
  exception

#### Scenario: The absence result is distinguishable from a valid-rate result by type, not by inspecting a null field
- **Given** the repository's lookup return type
- **When** a caller pattern-matches/branches on the result
- **Then** the "present" and "absent" cases are two distinct, exhaustively-handleable shapes

---

## Capability: `scraper-sbs`

### Requirement: The Python worker performs a single-run scrape of the SBS venta rate and writes an `Origen='SBS'` row

Per the proposal's already-resolved decision, `SmartNet/worker/` is the first Python code in the
repo: a minimal, single-execution script — no scheduler/polling/retry loop (deferred to #5). It
MUST write using `fact_worker` credentials, consistent with ADR 0003's partition (Python never
touches `dbo.*`).

#### Scenario: A successful run inserts the SBS rate for the current publication date
- **Given** the SBS site publishes a venta rate for today's date
- **When** the scraper runs
- **Then** a row `(Fecha = today, Origen = 'SBS', Tasa = <scraped value>)` is written via
  `fact_worker` credentials

#### Scenario: The scraper does not write to any dbo.* table
- **Given** a completed scraper run, successful or failed
- **When** inspecting the SQL statements it executes
- **Then** none targets a `dbo.*` table — only `fact.TipoCambio` and `fact.EstadoIntegracion`

### Requirement: Every scraper run — success or failure — is logged in `fact.EstadoIntegracion` (Nombre='SBS')

Per the proposal's success criteria, the run's outcome must be observable without a dedicated
alerting mechanism (proposal's approved assumption: `fact.EstadoIntegracion` alone is sufficient
for this item).

#### Scenario: A successful scrape logs a success attempt
- **Given** the scraper successfully inserts the SBS rate
- **When** the run finishes
- **Then** `fact.EstadoIntegracion` has an entry for `Nombre = 'SBS'` reflecting a successful
  attempt at that run time

#### Scenario: A failed scrape (site unreachable, parse error, etc.) still logs the attempt
- **Given** the SBS site is unreachable or its page format cannot be parsed
- **When** the run finishes without inserting a rate
- **Then** `fact.EstadoIntegracion` has an entry for `Nombre = 'SBS'` reflecting a failed attempt —
  no `fact.TipoCambio` row is written for that run

---

## Capability: `api-tipos-cambio` (BACKLOG #11)

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
