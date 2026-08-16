# Spec: Esquema y Permisos (BACKLOG #1)

Delta spec. Describes what MUST be true after this change is applied. No SQL, no
implementation detail — verifiable, observable database properties only.

## Non-Goals (explicit scope boundaries)

- **`dbo.*` tables are never created or written by this change.** `dbo.Proveedor`,
  `dbo.CuentaContable`, `dbo.Motivo`, `dbo.Origen` are referenced read-only. No DDL, no `INSERT`,
  `UPDATE`, or `DELETE` against `dbo` exists anywhere in the versioned SQL.
- **No row in `Usuario` and no credential of any kind appears in the versioned SQL.** The table is
  created empty. The first user is created later by the application's administration command
  (ADR 0007), never by migration.
- **The `SugerenciaCuenta` historical seed is out of scope.** It belongs to BACKLOG item #3.
- **Column types not stated in any source document are not decided here.** They are authored when
  the SQL is written (design/implementation phase), not invented at spec time.

---

## Capability: `schema-fact`

### Requirement: All four ownership classes exist as tables in schema `fact`

Every table named in ADR 0003's four ownership classes (Python-private, .NET-private,
contract/coescritas, publication) MUST exist as an object in schema `fact` after migration,
and MUST NOT exist in any other schema.

#### Scenario: Full table inventory is present in schema `fact`
- **Given** the versioned migrations have been applied to an empty target database
- **When** querying `sys.tables` joined to `sys.schemas` for `schema_name = 'fact'`
- **Then** the result set includes, at minimum, one row for each of: `Email`, `DocumentoRecibido`,
  `Procesamiento`, `DatosExtraidos`, `ProcesamientoError`, `ProcesamientoIntentos`, `Factura`,
  `AsientoContable`, `AsientoContableDetalle`, `AdjuntoManual`, `AuditoriaCorreccion`,
  `FacturaExtraccion`, `CorrelativoAsiento`, `ProveedorAtributo`, `MotivoAtributo`,
  `SugerenciaCuenta`, `Usuario`, `OutboxEvent`, `CommandQueue`, `InboxEvent`, `TipoCambio`,
  `Configuracion`, `EstadoIntegracion`
- **And** no table named `Proveedor`, `CuentaContable`, `Motivo`, or `Origen` exists in schema
  `fact` (they remain exclusively in `dbo`)

#### Scenario: No object of this project exists outside schema `fact`
- **Given** the versioned migrations have been applied
- **When** enumerating all tables created by this project's migration scripts
- **Then** every one reports `schema_name = 'fact'`

### Requirement: `IX_Factura_Identidad` is a non-unique filtered index

`IX_Factura_Identidad` on `fact.Factura (RucProveedor, TipoComprobante, Numero)` filtered to
`Estado <> 'DESCARTADA'` MUST NOT enforce uniqueness. Duplicate identity and empty/null `Numero`
values must remain insertable while the invoice is in `PENDIENTE_VALIDACION`.

#### Scenario: Two PENDIENTE_VALIDACION invoices with identical identity are both insertable
- **Given** schema `fact` with `IX_Factura_Identidad` applied
- **When** inserting two rows into `fact.Factura` with the same `(RucProveedor, TipoComprobante,
  Numero)`, both with `Estado = 'PENDIENTE_VALIDACION'`
- **Then** both inserts succeed without violation

#### Scenario: Two invoices with a null or empty Numero are both insertable
- **Given** schema `fact` with `IX_Factura_Identidad` applied
- **When** inserting two rows into `fact.Factura` with the same `RucProveedor` and
  `TipoComprobante`, and `Numero` null (or empty string, per whichever the SQL author selects) on
  both, both with `Estado = 'PENDIENTE_VALIDACION'`
- **Then** both inserts succeed without violation

#### Scenario: The index does not exist as a unique index under any name
- **Given** schema `fact` with all indexes applied
- **When** querying `sys.indexes` for `fact.Factura` where `name = 'IX_Factura_Identidad'`
- **Then** the returned `is_unique` flag is `0`

### Requirement: `UQ_Factura_Procesamiento` rejects a second promotion of the same `Procesamiento`

`UQ_Factura_Procesamiento` on `fact.Factura (ProcesamientoId)` filtered to `ProcesamientoId IS NOT
NULL` MUST be enforced by the engine as unique.

#### Scenario: Second insert with the same ProcesamientoId is rejected
- **Given** a row already exists in `fact.Factura` with `ProcesamientoId = X`
- **When** inserting a second row into `fact.Factura` with `ProcesamientoId = X`
- **Then** the engine rejects the insert with a uniqueness violation

#### Scenario: Two rows with a null ProcesamientoId are both insertable
- **Given** schema `fact` with `UQ_Factura_Procesamiento` applied
- **When** inserting two rows into `fact.Factura` with `ProcesamientoId` null on both
- **Then** both inserts succeed without violation

### Requirement: `UQ_Asiento_Vigente` allows at most one non-`ANULADO` entry per invoice

`UQ_Asiento_Vigente` on `fact.AsientoContable (FacturaId)` filtered to `Estado <> 'ANULADO'` MUST
be enforced by the engine as unique.

#### Scenario: A factura may accumulate many ANULADO asientos
- **Given** a `fact.Factura` row `F`
- **When** inserting multiple rows into `fact.AsientoContable` with `FacturaId = F.Id` and
  `Estado = 'ANULADO'`
- **Then** every insert succeeds regardless of how many `ANULADO` rows already reference `F`

#### Scenario: A second non-ANULADO asiento for the same factura is rejected
- **Given** a row already exists in `fact.AsientoContable` with `FacturaId = F.Id` and
  `Estado = 'CONFIRMADO'` (or any value other than `ANULADO`)
- **When** inserting a second row into `fact.AsientoContable` with `FacturaId = F.Id` and
  `Estado` set to any value other than `ANULADO`
- **Then** the engine rejects the insert with a uniqueness violation

### Requirement: `CK_Linea_Tipo` enforces the debit/credit shape per line

The check constraint on `fact.AsientoContableDetalle` MUST require, for `Tipo = 'D'`, `Debe > 0`
and `Haber = 0`; and for `Tipo = 'H'`, `Haber > 0` and `Debe = 0`.

#### Scenario: A debit line with Debe > 0 and Haber = 0 is accepted
- **Given** schema `fact` with `CK_Linea_Tipo` applied
- **When** inserting a row into `fact.AsientoContableDetalle` with `Tipo = 'D'`, `Debe = 100.00`,
  `Haber = 0`
- **Then** the insert succeeds

#### Scenario: A credit line with Haber > 0 and Debe = 0 is accepted
- **Given** schema `fact` with `CK_Linea_Tipo` applied
- **When** inserting a row into `fact.AsientoContableDetalle` with `Tipo = 'H'`, `Haber = 100.00`,
  `Debe = 0`
- **Then** the insert succeeds

#### Scenario: A debit line carrying a Haber amount is rejected
- **Given** schema `fact` with `CK_Linea_Tipo` applied
- **When** inserting a row into `fact.AsientoContableDetalle` with `Tipo = 'D'`, `Debe = 100.00`,
  `Haber = 50.00`
- **Then** the engine rejects the insert with a check constraint violation

#### Scenario: A credit line carrying a Debe amount is rejected
- **Given** schema `fact` with `CK_Linea_Tipo` applied
- **When** inserting a row into `fact.AsientoContableDetalle` with `Tipo = 'H'`, `Haber = 100.00`,
  `Debe = 50.00`
- **Then** the engine rejects the insert with a check constraint violation

#### Scenario: A line where the amount for its own type is zero is rejected
- **Given** schema `fact` with `CK_Linea_Tipo` applied
- **When** inserting a row into `fact.AsientoContableDetalle` with `Tipo = 'D'`, `Debe = 0`,
  `Haber = 0`
- **Then** the engine rejects the insert with a check constraint violation

### Requirement: `fact.CorrelativoAsiento` is a plain counter table, not a SEQUENCE or IDENTITY object

The composite primary key MUST be `(Anio, Mes, Origen)`, and `Ultimo` MUST be a mutable counter
column, not a database `SEQUENCE` or `IDENTITY` property.

#### Scenario: The primary key rejects a duplicate (Anio, Mes, Origen)
- **Given** a row already exists in `fact.CorrelativoAsiento` for `(Anio=2026, Mes=8, Origen='02')`
- **When** inserting a second row for the same `(Anio, Mes, Origen)`
- **Then** the engine rejects the insert with a primary key violation

#### Scenario: The table is not backed by a SEQUENCE or IDENTITY column
- **Given** schema `fact` with `CorrelativoAsiento` created
- **When** querying `sys.identity_columns` for `fact.CorrelativoAsiento` and `sys.sequences` for
  any object named after this table
- **Then** neither returns a row — `Ultimo` is a plain, explicitly-updated integer column

### Requirement: `Version` (`rowversion`) exists on `Factura` and `AsientoContable` only

#### Scenario: Factura and AsientoContable carry a rowversion column
- **Given** schema `fact` fully applied
- **When** querying `sys.columns` for `fact.Factura` and `fact.AsientoContable` filtered to
  `system_type_id` for `timestamp`/`rowversion`
- **Then** each table returns exactly one such column, named `Version`

#### Scenario: AsientoContableDetalle does not carry a rowversion column
- **Given** schema `fact` fully applied
- **When** querying `sys.columns` for `fact.AsientoContableDetalle` filtered to the
  `timestamp`/`rowversion` type
- **Then** the query returns no rows

### Requirement: Money columns are `DECIMAL(18,2)`; the exchange rate is `DECIMAL(12,6)`; no monetary column is a floating-point type

Per `CONVENTIONS.md`, monetary amounts and tax bases MUST be `DECIMAL(18,2)`. Exchange-rate
columns (`TipoCambio.Compra`, `TipoCambio.Venta`, `AsientoContable`'s applied rate, and any other
column expressing a currency rate) MUST be `DECIMAL(12,6)`. No column that stores a monetary
amount or an exchange rate anywhere in schema `fact` may be `float`, `real`, or `double`.

#### Scenario: No column in schema fact uses a floating-point SQL type
- **Given** schema `fact` fully applied
- **When** querying `sys.columns` joined to `sys.types` for all columns in schema `fact`
- **Then** no row reports type `float` or `real`

#### Scenario: Every column named as a monetary amount is DECIMAL(18,2)
- **Given** schema `fact` fully applied
- **When** querying `sys.columns` for columns identified as monetary amounts (e.g. `Factura.Monto`,
  `AsientoContableDetalle.Debe`, `AsientoContableDetalle.Haber`, and equivalents)
- **Then** each reports type `decimal` with precision `18` and scale `2`

#### Scenario: Every column expressing an exchange rate is DECIMAL(12,6)
- **Given** schema `fact` fully applied
- **When** querying `sys.columns` for `TipoCambio.Compra`, `TipoCambio.Venta`, and the applied-rate
  column on `AsientoContable`
- **Then** each reports type `decimal` with precision `12` and scale `6`

---

## Capability: `db-permissions`

### Requirement: `usr_api` and `usr_worker` exist as two distinct database identities with per-table grants matching ADR 0003's matrix

The permission matrix MUST be enforced by the engine, not by application-level convention. Every
assertion below MUST be checkable by attempting the operation as the named user and observing
either success or a permission-denied error — never by inspecting a document.

#### Scenario: usr_api is denied SELECT on fact.Procesamiento
- **Given** `usr_api` connected to the database with grants applied
- **When** `usr_api` executes `SELECT` against `fact.Procesamiento`
- **Then** the engine denies the operation with a permission error

#### Scenario: usr_api is denied SELECT on fact.DatosExtraidos
- **Given** `usr_api` connected to the database with grants applied
- **When** `usr_api` executes `SELECT` against `fact.DatosExtraidos`
- **Then** the engine denies the operation with a permission error

#### Scenario: usr_worker is denied INSERT and UPDATE on fact.Factura
- **Given** `usr_worker` connected to the database with grants applied
- **When** `usr_worker` executes `INSERT` and separately `UPDATE` against `fact.Factura`
- **Then** the engine denies both operations with a permission error

#### Scenario: usr_worker is denied any access to fact.AsientoContable, fact.AsientoContableDetalle, fact.AdjuntoManual, fact.Usuario
- **Given** `usr_worker` connected to the database with grants applied
- **When** `usr_worker` executes `SELECT` against each of `fact.AsientoContable`,
  `fact.AsientoContableDetalle`, `fact.AdjuntoManual`, `fact.Usuario`
- **Then** the engine denies every one of those `SELECT` operations with a permission error

#### Scenario: usr_api has full SELECT/INSERT/UPDATE on its own private tables
- **Given** `usr_api` connected to the database with grants applied
- **When** `usr_api` executes `SELECT`, `INSERT`, and `UPDATE` against `fact.Factura`,
  `fact.AsientoContable`, `fact.AsientoContableDetalle`, `fact.AdjuntoManual`,
  `fact.AuditoriaCorreccion`, `fact.FacturaExtraccion`, `fact.CorrelativoAsiento`,
  `fact.ProveedorAtributo`, `fact.MotivoAtributo`, `fact.SugerenciaCuenta`, `fact.Usuario`
- **Then** every operation succeeds (subject to any table-level constraint independent of
  permissions, e.g. `CK_Linea_Tipo`)

#### Scenario: usr_worker has full SELECT/INSERT/UPDATE on its own private tables
- **Given** `usr_worker` connected to the database with grants applied
- **When** `usr_worker` executes `SELECT`, `INSERT`, and `UPDATE` against `fact.Email`,
  `fact.DocumentoRecibido`, `fact.Procesamiento`, `fact.DatosExtraidos`,
  `fact.ProcesamientoError`, `fact.ProcesamientoIntentos`
- **Then** every operation succeeds

#### Scenario: usr_api can INSERT and SELECT fact.OutboxEvent but not UPDATE it
- **Given** `usr_api` connected to the database with grants applied
- **When** `usr_api` executes `INSERT` and `SELECT` against `fact.OutboxEvent`
- **Then** both operations succeed
- **When** `usr_api` executes `UPDATE` against `fact.OutboxEvent`
- **Then** the engine denies the operation with a permission error

#### Scenario: usr_worker can SELECT and UPDATE fact.OutboxEvent but not INSERT into it
- **Given** `usr_worker` connected to the database with grants applied
- **When** `usr_worker` executes `SELECT` and `UPDATE` against `fact.OutboxEvent`
- **Then** both operations succeed
- **When** `usr_worker` executes `INSERT` against `fact.OutboxEvent`
- **Then** the engine denies the operation with a permission error

#### Scenario: usr_worker can INSERT and SELECT fact.InboxEvent but not UPDATE it
- **Given** `usr_worker` connected to the database with grants applied
- **When** `usr_worker` executes `INSERT` and `SELECT` against `fact.InboxEvent`
- **Then** both operations succeed
- **When** `usr_worker` executes `UPDATE` against `fact.InboxEvent`
- **Then** the engine denies the operation with a permission error

#### Scenario: usr_api can SELECT and UPDATE fact.InboxEvent but not INSERT into it
- **Given** `usr_api` connected to the database with grants applied
- **When** `usr_api` executes `SELECT` and `UPDATE` against `fact.InboxEvent`
- **Then** both operations succeed
- **When** `usr_api` executes `INSERT` against `fact.InboxEvent`
- **Then** the engine denies the operation with a permission error

#### Scenario: usr_api can INSERT and SELECT fact.CommandQueue but not UPDATE it
- **Given** `usr_api` connected to the database with grants applied
- **When** `usr_api` executes `INSERT` and `SELECT` against `fact.CommandQueue`
- **Then** both operations succeed
- **When** `usr_api` executes `UPDATE` against `fact.CommandQueue`
- **Then** the engine denies the operation with a permission error

#### Scenario: usr_worker can SELECT and UPDATE fact.CommandQueue but not INSERT into it
- **Given** `usr_worker` connected to the database with grants applied
- **When** `usr_worker` executes `SELECT` and `UPDATE` against `fact.CommandQueue`
- **Then** both operations succeed
- **When** `usr_worker` executes `INSERT` against `fact.CommandQueue`
- **Then** the engine denies the operation with a permission error

#### Scenario: Both users can INSERT, UPDATE, and SELECT fact.TipoCambio
- **Given** either `usr_api` or `usr_worker` connected to the database with grants applied
- **When** that user executes `INSERT`, `UPDATE`, and `SELECT` against `fact.TipoCambio`
- **Then** every operation succeeds for both users

#### Scenario: Both users can SELECT fact.Configuracion; only usr_api can INSERT/UPDATE it
- **Given** `usr_api` and `usr_worker` connected to the database with grants applied
- **When** `usr_worker` executes `SELECT` against `fact.Configuracion`
- **Then** the operation succeeds
- **When** `usr_worker` executes `INSERT` or `UPDATE` against `fact.Configuracion`
- **Then** the engine denies both operations with a permission error
- **When** `usr_api` executes `SELECT`, `INSERT`, and `UPDATE` against `fact.Configuracion`
- **Then** every operation succeeds

#### Scenario: Both users can INSERT, UPDATE, and SELECT fact.EstadoIntegracion
- **Given** either `usr_api` or `usr_worker` connected to the database with grants applied
- **When** that user executes `INSERT`, `UPDATE`, and `SELECT` against `fact.EstadoIntegracion`
- **Then** every operation succeeds for both users

#### Scenario: Both users can SELECT the four external dbo tables and neither can write them
- **Given** `usr_api` and `usr_worker` connected to the database with grants applied
- **When** each user executes `SELECT` against `dbo.Proveedor`, `dbo.CuentaContable`, `dbo.Motivo`,
  and `dbo.Origen`
- **Then** every `SELECT` succeeds for both users
- **When** each user executes `INSERT`, `UPDATE`, or `DELETE` against any of those four tables
- **Then** the engine denies every one of those operations, for both users, with a permission error

#### Scenario: Neither user has any grant on any dbo table other than the four named
- **Given** `usr_api` and `usr_worker` connected to the database with grants applied
- **When** enumerating effective permissions for both users via the engine's permission-metadata
  views (e.g. `sys.database_permissions` / `fn_my_permissions`), scoped to schema `dbo`
- **Then** the only objects listed are `dbo.Proveedor`, `dbo.CuentaContable`, `dbo.Motivo`,
  `dbo.Origen`, all with `SELECT` only

### Requirement: The permission matrix is expressed exclusively as versioned SQL

No permission may exist that was granted outside the versioned migration scripts. Reapplying the
migrations to a fresh database MUST reproduce an identical matrix.

#### Scenario: Effective permissions are reproducible from the versioned scripts alone
- **Given** two databases created independently, both by applying only the versioned migration
  scripts
- **When** enumerating effective permissions for `usr_api` and `usr_worker` on both databases
- **Then** the two permission sets are identical

---

## Capability: `schema-base-data`

### Requirement: One `EstadoIntegracion` row exists per integration named in TECH-DESIGN, including `WORKER`

#### Scenario: EstadoIntegracion is seeded with exactly the five known integration names
- **Given** the versioned migrations have been applied
- **When** querying `SELECT Nombre FROM fact.EstadoIntegracion`
- **Then** the result set contains exactly one row for each of `GMAIL`, `DRIVE`, `SHEETS`, `SBS`,
  `WORKER` — five rows, no more, no fewer

#### Scenario: The WORKER heartbeat row starts in a state that does not trigger a false alert
- **Given** the `EstadoIntegracion` row for `Nombre = 'WORKER'` as seeded by migration
- **When** inspecting its `FallosSeguidos` value
- **Then** it is `0` (or another value that does not by itself mark the integration as `Con error`
  before the worker has ever run)

### Requirement: A default `Configuracion` row (or set of rows, per the sectioned structure the SQL author chooses) exists after migration

#### Scenario: Configuracion is queryable immediately after migration without requiring any prior write
- **Given** the versioned migrations have been applied to an empty database
- **When** querying `fact.Configuracion` (or its section tables, per the structure chosen at
  authoring time) for every section named in TECH-DESIGN (monitored folder/label, allowed
  extensions, poll frequency, start date, attachment types and max size, Telegram destination,
  notification and display preferences, expected interval per integration)
- **Then** every section returns at least one row with a non-null value for its required fields —
  the application never needs to handle "no configuration row exists yet" as a startup case

### Requirement: `fact.Usuario` exists and is empty after migration

#### Scenario: The Usuario table has zero rows immediately after migration
- **Given** the versioned migrations have been applied to an empty database
- **When** querying `SELECT COUNT(*) FROM fact.Usuario`
- **Then** the count is `0`

#### Scenario: No column of fact.Usuario contains a value resembling a password hash in the migration source
- **Given** the versioned SQL migration files as committed to the repository
- **When** inspecting every `INSERT` statement targeting `fact.Usuario`
- **Then** no such `INSERT` statement exists in the versioned SQL

### Requirement: Exactly the 23 `†`-marked motives from `MOTIVOS-CLASIFICACION.md` are loaded into `MotivoAtributo` reclassified to origin `02 COMPRAS`

The reclassified set is defined by the `†` marker in `MOTIVOS-CLASIFICACION.md`'s complete table,
counted directly from that document, not assumed from prose.

> **Corrected by the orchestrator.** The first draft of this requirement said 22 and omitted motive
> **88** (`Devolución Comprobante CChica`, account `169105`). The count was verified directly against
> the document's table: **23** rows carry `†` and 27 do not, which matches the stated total of 50.
> The prose in `MOTIVOS-CLASIFICACION.md` said "28 propios + 22 reclasificados" — off by one on each
> side — and has been corrected there too, along with every other document repeating it.

#### Scenario: MotivoAtributo contains exactly 23 rows with OrigenLibro = '02' sourced from the reclassification
- **Given** the versioned migrations have been applied
- **When** querying `fact.MotivoAtributo` for the rows corresponding to motive numbers 5, 13, 16,
  17, 18, 19, 20, 21, 30, 38, 40, 42, 46, 48, 49, 53, 56, 59, 60, 77, 81, 88, 90 (the 23 rows marked
  `†` in `MOTIVOS-CLASIFICACION.md`)
- **Then** every one of those 23 rows exists with `OrigenLibro = '02'`
- **And** no other motive not marked `†` in that document is reclassified away from its documented
  origin by this migration

#### Scenario: Reclassified motives remain Activo unless independently listed among the six BAJA motives
- **Given** the 23 reclassified rows loaded into `fact.MotivoAtributo`
- **When** checking their `Activo` flag
- **Then** each is `Activo = true`, since none of the 23 `†`-marked motives (5, 13, 16, 17, 18, 19,
  20, 21, 30, 38, 40, 42, 46, 48, 49, 53, 56, 59, 60, 77, 81, 88, 90) coincides with the six motives
  listed as `BAJA` in `MOTIVOS-CLASIFICACION.md` (1, 28, 39, 44, 76, 83)

### Requirement: No base-data seeding writes to any `dbo` table

#### Scenario: All base-data INSERT statements target schema fact only
- **Given** the versioned migration files that load `EstadoIntegracion`, `Configuracion`, and
  `MotivoAtributo` base data
- **When** inspecting every `INSERT` statement in those files
- **Then** every target table is qualified with schema `fact`; none targets `dbo`
