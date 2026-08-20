# Spec: Catálogos y Satélites (BACKLOG #3)

New capabilities: pure application layer over already-built DDL (item #1). No new tables, no
GRANT changes, no cascade/ranking logic. See `proposal.md` Out of Scope.

## Non-Goals (explicit scope boundaries)

- **No DDL or GRANT changes.** The 3 satellites and 5 `dbo.*` grants already exist (item #1).
- **No `SugerenciaCuenta` ranking/cascade.** Storage access only; ranking is item #9.
- **No `ctarefleja`/`ctapuente` freezing.** Reading these columns is in scope; freezing them into
  asiento lines at confirmation is item #8's job.
- **No SQL-side prefix matching (view/function with `LIKE`).** ADR 0019 + TECH-DESIGN.md
  (líneas 463-464) rule this out: "`MotivoCuenta` no es una tabla: es la interpretación que la
  aplicación hace del campo de prefijos."

---

## Capability: `catalogos-externos`

### Requirement: Each of the 5 external catalogs has a dedicated read-only repository interface

Per ADR 0003 Revisión 5, the external catalogs are `Proveedor`, `CuentaContable`, `Motivo`,
`Origen`, `DocumentoIdentidad`. REGLAS.md §2 (líneas 68-74) lists only 4; `DocumentoIdentidad` is
absent there because it supports `Proveedor.coddocide`, not a "business catalog" in REGLAS.md's
sense — documentation drift, not an accounting conflict. ADR 0003 Revisión 5 is the count
authority. Each interface MUST expose only read operations — no `Insert`/`Update`/`Delete`.

#### Scenario: CuentaContable repository can be queried by exact code
- **Given** `dbo.CuentaContable` has a row with `cuenta = '631111'`
- **When** the repository looks up `'631111'`
- **Then** it returns `cuenta`, `descripcion`, `nivel`, `ctarefleja`, `ctapuente`

#### Scenario: A lookup by code that does not exist returns no result, not an exception
- **Given** no row has `cuenta = '999999'`
- **When** the repository looks it up
- **Then** it returns an empty/absent result, no exception

#### Scenario: An empty external catalog returns an empty collection
- **Given** any of the 5 catalogs has zero rows
- **When** the repository lists all rows
- **Then** it returns an empty collection, not `null`

#### Scenario: No repository over the 5 external catalogs exposes a write method
- **Given** the 5 repository interfaces
- **When** inspecting their members
- **Then** none declares `Insert`, `Update`, or `Delete`

### Requirement: The `CuentaContable` repository can list the 907 imputable leaf accounts

REGLAS.md §2: "Solo las de 6 dígitos son imputables (907)." The catalog distinguishes leaf from
hierarchy node via `nivel` (empty on leaves) — no code-length computation needed.

#### Scenario: Listing leaf accounts excludes hierarchy nodes
- **Given** the catalog has both leaf rows (`nivel` empty) and hierarchy rows (`nivel` populated)
- **When** the repository lists imputable leaf accounts
- **Then** every returned row has `nivel` empty, no hierarchy-node row included

---

## Capability: `satelites-propios`

### Requirement: `ProveedorAtributo` and `MotivoAtributo` repositories support full CRUD

These satellites hold attributes the external catalogs do not provide (REGLAS.md §2:
"relacionada", "activo, origen"). Item #1 already grants `fact_api` `SELECT/INSERT/UPDATE` on
them. The repository interfaces MUST expose create, read, and update operations.

#### Scenario: Writing a new ProveedorAtributo row for a provider code not yet present
- **Given** no `fact.ProveedorAtributo` row exists for `ProveedorCodigo = 'P01234'`
- **When** the repository saves a new `EsRelacionada` value for that code
- **Then** a row for `'P01234'` exists afterward with that value

#### Scenario: Reading a MotivoAtributo for a Motivo not seeded returns no result, not an exception
- **Given** no `fact.MotivoAtributo` row exists for a given `Motivo` code
- **When** the repository looks up that `Motivo`
- **Then** it returns an empty/absent result, no exception

### Requirement: `SugerenciaCuenta` repository exposes storage access only — no ranking

Per the proposal's scope boundary, this item provides read/write/find over the composite key
(`ProveedorCodigo`, `Motivo`, `CuentaCodigo`) plus `Veces`/`UltimoUso`. It MUST NOT rank, sort by
frequency, or select a single "suggested" account — that is item #9.

#### Scenario: The repository can increment usage for an existing combination
- **Given** a row exists for `(ProveedorCodigo, Motivo, CuentaCodigo)`
- **When** the repository records a new use of that combination
- **Then** `Veces` increases and `UltimoUso` updates, no other row is affected

#### Scenario: The repository interface has no method that returns a single "best" suggestion
- **Given** `ISugerenciaCuentaRepository`
- **When** inspecting its members
- **Then** no method ranks/sorts/selects one preferred candidate — reads return raw rows only

### Requirement: Satellite writes validate existence against `dbo.*` at runtime, not via foreign key

Per ADR 0003, satellites have no FK to the external catalogs — the deployment user lacks
`REFERENCES` on `dbo.*`. Referential correctness on write is an application-level concern.

#### Scenario: The satellite repository layer does not rely on a database foreign key to reject an orphan write
- **Given** `dbo.Proveedor` has no row for a given `ProveedorCodigo`
- **When** a caller writes a `ProveedorAtributo` row for that code
- **Then** the outcome does not depend on a FK firing (there is none) — any rejection is an
  explicit application-level check

---

## Capability: `resolucion-de-prefijos`

### Requirement: `ResolverCandidatas` returns every 6-digit leaf account matching any of a motivo's comma-separated prefixes

Per REGLAS.md §3: "El motivo declara prefijos, no cuentas. Las candidatas son todas las hojas de 6
dígitos cuyo código empieza por alguno de esos prefijos." Given the raw `Motivo.cuenta` value
(comma-separated prefixes, 2–6 digits) and the `CuentaContable` catalog, the function MUST return
the leaf accounts (`nivel` empty) whose `cuenta` starts with any prefix — `LIKE prefijo + '%'`
semantics.

#### Scenario: A single specific prefix matches exactly one leaf account (REGLAS.md §3, motivo 22)
- **Given** `Motivo.cuenta = '631111'` and the catalog has leaf `631111`
- **When** `ResolverCandidatas` is invoked
- **Then** it returns exactly one candidate: `631111`

#### Scenario: Multiple comma-separated prefixes match the union of leaves, without duplicates (REGLAS.md §3, motivo 8)
- **Given** `Motivo.cuenta = '4011,4017,4018,403,417'`, whose prefixes' leaves total 22 distinct
  accounts per REGLAS.md §3's worked example
- **When** `ResolverCandidatas` is invoked
- **Then** it returns exactly those 22 candidates, each appearing at most once even if it matches
  more than one declared prefix

#### Scenario: A prefix with no matching leaf account contributes nothing, not an error
- **Given** a prefix matching no leaf account's `cuenta`
- **When** `ResolverCandidatas` is invoked with only that prefix
- **Then** the result is an empty collection, not an exception

#### Scenario: A hierarchy-level account is never returned as a candidate, even if its code matches a prefix
- **Given** a hierarchy-node row (`nivel` populated, e.g. 3-digit `403`) whose code satisfies a
  declared prefix
- **When** `ResolverCandidatas` is invoked with that prefix
- **Then** the hierarchy-node row is excluded — only `nivel`-empty 6-digit leaves may appear

#### Scenario: A newly created leaf account under an already-declared prefix appears without changing the motivo
- **Given** a new leaf account is added under an already-declared prefix (REGLAS.md §3: "Una
  cuenta nueva creada bajo un prefijo ya declarado aparece sola, sin tocar nada")
- **When** `ResolverCandidatas` is invoked for that motivo afterward
- **Then** the new account is included, with no change to `Motivo.cuenta`

### Requirement: `ResolverCandidatas` is a pure function with no DB, HTTP, or clock dependency (ADR 0019)

The function MUST take the prefix string and an in-memory catalog as parameters and return a
result deterministically — no DB, no HTTP, no system clock. Verified via the same PurityScanTests
pattern used for `SmartNet.Auth.Core` (item #2).

#### Scenario: SmartNet.Catalogos.Core does not reference infrastructure types
- **Given** the compiled `SmartNet.Catalogos.Core` assembly
- **When** scanning referenced assemblies/type usages
- **Then** it does not reference `Microsoft.Data.SqlClient`, `Microsoft.AspNetCore.*`, or
  `System.Net.Http`, and does not call `DateTime.Now`/`DateTime.UtcNow`

#### Scenario: ResolverCandidatas is exercised by tests with zero DB dependency
- **Given** tests seeded with an in-memory catalog reproducing REGLAS.md §3's worked examples
  (motivo 22, 48, 6, 70, 8)
- **When** running those tests
- **Then** they execute without a database connection

### Requirement: `CuentaContable.cuenta` MUST remain a variable-length type end to end

Non-negotiable: `CHAR` padding on `cuenta` would break `LIKE prefijo + '%'`/`StartsWith`
matching, since trailing spaces would make short-prefix rows never match.

#### Scenario: A short account code is not padded before prefix matching
- **Given** a `cuenta` value read from `dbo.CuentaContable` (`VARCHAR`, not `CHAR`)
- **When** it flows through the repository into `ResolverCandidatas`
- **Then** it carries no trailing padding — comparison behaves like SQL `LIKE prefijo + '%'`
  against the unpadded column

---

## Capability: `nucleo-dominio-catalogos` (ADR 0019 purity, infrastructure conventions)

### Requirement: Infrastructure repositories follow the `SmartNet.Auth.Infrastructure` pattern

`SmartNet.Catalogos.Infrastructure` MUST replicate `SmartNet.Auth.Infrastructure` (item #2):
`Sql*Repository` adapters using `Microsoft.Data.SqlClient` implementing `Core`'s interfaces.
`SmartNet.Catalogos.Core` MUST have zero `PackageReference`/DB/HTTP/clock dependency, matching the
purity scan already applied to `SmartNet.Auth.Core`.

#### Scenario: No SQL adapter writes to a dbo.* table (ADR 0003)
- **Given** every `Sql*Repository` in `SmartNet.Catalogos.Infrastructure`
- **When** inspecting the SQL statements each executes
- **Then** none of the 5 external-catalog adapters issues `INSERT`/`UPDATE`/`DELETE` against any
  `dbo.*` table
