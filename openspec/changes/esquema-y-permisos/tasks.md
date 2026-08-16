# Tasks: Esquema y Permisos (BACKLOG #1)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~1800–2400 (9 declarative SQL scripts ~700–900, runner C# ~100, test-bootstrap ~150, schema-shape + permission-matrix test code ~600–900, rollback/checksum scaffolding ~150) |
| 400-line budget risk | High |
| Chained PRs recommended | Yes |
| Suggested split | PR 1 → PR 2 → PR 3 → PR 4 → PR 5 |
| Delivery strategy | ask-on-risk |
| Chain strategy | pending — orchestrator to ask user |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

**Nuance:** almost the entire count is declarative SQL (CREATE TABLE/CHECK/GRANT/DENY) and mirrored
schema-shape assertions, not branching application logic. Per-line review cost is materially lower
than the same line count in service/business code — the reviewer verifies each line against ADR 0003's
matrix and the design's type table, not control flow. Chaining is still recommended because the file
count and cross-cutting concern (structure → permissions → data) benefit from independent, orderable
review boundaries.

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | DbUp runner + local test-bootstrap harness | PR 1 | `dotnet test SmartNet.Db.Runner.Tests` | `dotnet run --project SmartNet/db/runner` against empty `fact_test_<id>` | Delete `SmartNet/db/runner/`, `SmartNet/db/test-bootstrap/` |
| 2 | Schema structure 001–007 + schema-shape tests | PR 2 | schema-shape suite (`sys.tables`/`sys.columns`/`sys.indexes` assertions) | Runner over `fact_test_<id>`, empty DB | `DROP SCHEMA fact` bootstrap-window reset (Decision 4) |
| 3 | Permissions 008 + ADR 0019 level-2 matrix tests | PR 3 | `EXECUTE AS USER` matrix suite | Runner + `test-bootstrap` WITHOUT LOGIN users | New compensating migration only; no DROP |
| 4 | Base data 009–010 + base-data tests | PR 4 | base-data assertion suite (`EstadoIntegracion`, `Configuracion`, `MotivoAtributo` counts) | Runner over `fact_test_<id>` seeded from `dbo.Motivo` | New compensating migration only; no DROP/TRUNCATE |
| 5 | CI re-hash manifest + advisory rollback scripts | PR 5 | CI hash-check script run | N/A — static file check, no DB | Remove `checksums.txt` + CI step |

---

## Phase 1: Runner and Test Harness Infrastructure

- [x] 1.1 Scaffold `SmartNet/db/runner/SmartNet.Db.Runner` (.NET console, DbUp), referencing no domain project.
- [x] 1.2 RED: write a runner smoke test asserting the journal table lands in `fact.SchemaVersions`, not `dbo.SchemaVersions`.
- [x] 1.3 GREEN: configure `.JournalToSqlTable("fact","SchemaVersions")`, `.WithTransactionPerScript()`, script path `SmartNet/db/schema/`, exit non-zero on failure.
- [x] 1.4 Create `SmartNet/db/test-bootstrap/`: script that creates empty `fact_test_<id>`, runs the runner, then `CREATE USER usr_api WITHOUT LOGIN` / `usr_worker WITHOUT LOGIN` before `008` applies.
- [x] 1.5 RED: assert `008` is create-if-absent (idempotent re-apply against a DB where the two WITHOUT LOGIN users already exist).
  - **Note:** `008_usuarios_y_permisos.sql` does not exist yet (Phase 3 / Unit 3). What was actually
    verified here is that the harness's own `CREATE USER ... WITHOUT LOGIN` mechanism — the one
    `008` is designed to rely on — is itself idempotent: re-creating a WITHOUT LOGIN user that
    already exists does not fail. The literal "re-apply `008` against pre-existing users" assertion
    is deferred to Phase 3, when `008` exists to test against.
  - **Discovery, not in the design:** on a brand-new database, DbUp's journal-table creation for
    `fact.SchemaVersions` fails with SQL error 2760 if schema `fact` is created by the *same* script
    within the *same* transaction (the schema, created via dynamic SQL inside the script's
    transaction, is not visible to DbUp's own journal-creation round-trip). Fixed in the runner by
    ensuring schema `fact` exists (idempotent, `IF SCHEMA_ID(...) IS NULL`) *before* invoking
    `PerformUpgrade()` — a pure infrastructure step, parallel to what `EnsureDatabase.For.SqlDatabase`
    does for the database itself, not a migration. This means `001_esquema_fact.sql` (Phase 2) must
    itself be written as `IF SCHEMA_ID('fact') IS NULL EXEC('CREATE SCHEMA fact')`, idempotently —
    consistent with the create-if-absent convention the design already applies to `008`/`009`/`010`.

## Phase 2: Schema Structure (RED before each script)

- [x] 2.1 RED: schema-shape test — `sys.tables`/`sys.schemas` inventory expects every table in spec's full list, all in `schema_name='fact'`, none named `Proveedor`/`CuentaContable`/`Motivo`/`Origen`.
- [x] 2.2 GREEN: `001_esquema_fact.sql` — `CREATE SCHEMA fact`.
- [x] 2.3 GREEN: `002_seguridad.sql` — `fact.Usuario` (no `INSERT`, `ClaveHash NVARCHAR(200)`, `BloqueadoHasta DATETIME2(3)`).
- [x] 2.4 GREEN: `003_ingesta_y_procesamiento.sql` — `Email`, `DocumentoRecibido`, `Procesamiento`, `DatosExtraidos`, `ProcesamientoError`, `ProcesamientoIntentos`.
- [x] 2.5 GREEN: `004_satelites_datos_maestros.sql` — `ProveedorAtributo`/`SugerenciaCuenta` keyed on `ProveedorCodigo CHAR(6)`; `MotivoAtributo` keyed on `Motivo INT`, `CuentaCodigo VARCHAR(10)`.
  - **Decision made explicit (not literally closed by design.md for these three tables):** no
    `FOREIGN KEY` from `ProveedorAtributo.ProveedorCodigo`, `MotivoAtributo.Motivo` or
    `SugerenciaCuenta.(ProveedorCodigo, Motivo)` to `dbo.Proveedor`/`dbo.Motivo`. Reasoning
    extended directly from design.md's item 1 (`Factura.RucProveedor`, "Not an FK... it is a frozen
    copy") and item 6 (`CuentaCodigo`, "No FK to dbo.CuentaContable... ADR 0006 freezes these values
    so they must survive the external account being renumbered or deleted; an FK would make freezing
    meaningless and could block the accounting system's own deletes"). The satellites are not
    frozen copies in the same sense as `RucProveedor`, but the second half of that reasoning — an
    FK on `fact.*` constrains `dbo.*`'s own `DELETE`s, sitting uneasily with ADR 0003's "nadie
    escribe una tabla externa" — applies identically regardless of whether the referencing column is
    a frozen snapshot or a live lookup. It is reinforced structurally: `008` (Unit 3) grants `SELECT`
    only on the four `dbo.*` tables, never `REFERENCES`, so the deploy principal could not declare
    the FK under its own permission boundary even if the reasoning above were rejected.
- [x] 2.6 GREEN: `005_negocio.sql` — `Factura`, `FacturaExtraccion`, `AsientoContable`, `AsientoContableDetalle`, `CorrelativoAsiento`, `AdjuntoManual`, `AuditoriaCorreccion`.
- [x] 2.7 RED: index/constraint tests — `IX_Factura_Identidad` (`is_unique=0`), `UQ_Factura_Procesamiento`, `UQ_Asiento_Vigente`, `CK_Linea_Tipo` (4 accept/reject cases), `CorrelativoAsiento` PK + no `sys.identity_columns`/`sys.sequences` row.
- [x] 2.8 GREEN: apply filtered indexes and `CK_Linea_Tipo` inside `005_negocio.sql` to satisfy 2.7.
- [x] 2.9 GREEN: `006_contratos.sql` — `OutboxEvent`, `OutboxEventIntegracion`, `CommandQueue`, `InboxEvent`; `Secuencia BIGINT` fed by `SEQUENCE fact.SeqOutbox`.
- [x] 2.10 GREEN: `007_publicacion.sql` — `TipoCambio`, `Configuracion`, `EstadoIntegracion`.
- [x] 2.11 RED: money/rate type test — no `float`/`real` in schema `fact`; every named monetary column `DECIMAL(18,2)`; every rate column `DECIMAL(12,6)`.
- [x] 2.12 RED: `rowversion` test — exactly `Factura.Version` and `AsientoContable.Version`; none on `AsientoContableDetalle`.
- [x] 2.13 GREEN: adjust column definitions across 2.3–2.10 to satisfy 2.11–2.12.
  - **Note on RED/GREEN granularity actually executed:** all RED assertions for Phase 2 (2.1, the
    2.7 index/constraint suite, 2.11, 2.12 — 25 tests total in `SchemaShapeTests.cs`) were written
    and run together first, confirmed failing (20 failed / 5 trivially-true absence checks, against
    zero schema scripts), then scripts `001`–`007` were authored together and the same 25 tests
    re-run to GREEN (0 failed / 25 passed after two harness type-cast bugs in the test helpers
    themselves — `int` vs `long` identity columns, `bit` vs `int` for `is_unique` — were fixed; the
    schema itself needed no correction). This compresses the task list's one-script-at-a-time RED/
    GREEN granularity into two real estate SQL Server round trips (RED once, GREEN once) rather than
    thirteen, for cost reasons stated in the Work Unit Evidence table below. The RED-before-GREEN
    property — no production SQL was written before its assertion existed and was observed failing
    — holds at the work-unit granularity.
  - **Columns/tables authored beyond what design.md/spec.md state verbatim** (consistent with
    design.md's own global rules — VARCHAR(20) enum + named CHECK, DATETIME2(3) UTC timestamps —
    but not literally spelled out column-by-column in the source documents): `Usuario.NombreUsuario`
    login handle; `Email.Estado`, `DocumentoRecibido.Estado`, `Procesamiento.Estado` value sets;
    `ProcesamientoError`/`ProcesamientoIntentos` FK target (linked directly to `Procesamiento`);
    `FacturaExtraccion.CampoNombre` value set (8 field names derivable from `DatosExtraidos`);
    `OutboxEvent`/`OutboxEventIntegracion`/`CommandQueue`/`InboxEvent`.`Estado` value sets;
    `InboxEvent.Tipo` (single value `PROCESAMIENTO_FINALIZADO`, ADR 0004 names only one notification
    direction). None of these affect the load-bearing type decisions (money, rate, RUC, CuentaCodigo,
    ProveedorCodigo, rowversion, filtered indexes, `CK_Linea_Tipo`, `CorrelativoAsiento`) that the
    spec's requirements and scenarios test directly.

## Phase 3: Permission Matrix

- [ ] 3.1 RED: ADR 0019 level-2 matrix suite — one `EXECUTE AS USER` case per spec scenario (deny `usr_api`→`Procesamiento`/`DatosExtraidos`; deny `usr_worker` writes/reads per matrix; per-table allow sets; `OutboxEvent`/`InboxEvent`/`CommandQueue` split grants; shared `TipoCambio`/`EstadoIntegracion`; `Configuracion` split; four `dbo.*` SELECT-only, no other `dbo` grant).
- [ ] 3.2 GREEN: `008_usuarios_y_permisos.sql` — `THROW 50001` guard for missing `usr_api`/`usr_worker` login; `CREATE USER … FOR LOGIN` (create-if-absent); roles `fact_api`/`fact_worker`; `GRANT`/`DENY` per table; object-level `GRANT SELECT` on the four `dbo.*` tables only; `ALTER USER … DEFAULT_SCHEMA = fact`.
- [ ] 3.3 GREEN: extend 3.2 for `OutboxEventIntegracion` child-table grants (api: INSERT/SELECT; worker: SELECT/UPDATE).
- [ ] 3.4 RED: reproducibility test — apply migrations to two independently created databases; compare `fn_my_permissions`/`sys.database_permissions` sets for both roles; expect identical.

## Phase 4: Base Data

- [ ] 4.1 RED: `EstadoIntegracion` test — exactly the seeded name set, `WORKER.FallosSeguidos = 0`.
- [ ] 4.2 RED: `Configuracion` test — every TECH-DESIGN section returns ≥1 row; `pendiente` keys have `Valor`/`ValorPorDefecto` both `NULL`.
- [ ] 4.3 RED: `MotivoAtributo` test — exactly 23 rows for motives 5,13,16,17,18,19,20,21,30,38,40,42,46,48,49,53,56,59,60,77,81,88,90, all `OrigenLibro='02'`, `Activo=1`.
- [ ] 4.4 RED: `Usuario` empty test — `COUNT(*) = 0`; no `INSERT` targeting `fact.Usuario` anywhere in versioned SQL.
- [ ] 4.5 GREEN: `009_datos_base.sql` — `EstadoIntegracion` rows, `Configuracion` defaults, all `NOT EXISTS`-guarded.
- [ ] 4.6 GREEN: `010_motivo_atributo_demo.sql` — `INSERT … SELECT` from `dbo.Motivo` matched by motive number; `IF @@ROWCOUNT <> 23 THROW`.
- [ ] 4.7 RED: dbo-write-safety test — every `INSERT` in 4.5/4.6 targets a `fact.`-qualified table.

## Phase 5: CI Hash Manifest and Advisory Rollback

- [ ] 5.1 GREEN: generate `SmartNet/db/schema/checksums.txt` hashing every applied `*.sql`.
- [ ] 5.2 RED: CI step test — re-hashing an already-listed script that was edited fails the build.
- [ ] 5.3 GREEN: add the CI re-hash step.
- [ ] 5.4 GREEN: author `SmartNet/db/schema/rollback/NNN_down.sql` per script (advisory, never executed by the runner).
- [ ] 5.5 GREEN: add CI lint rejecting unqualified `dbo.` outside the four permitted `GRANT SELECT ON OBJECT::dbo.*` lines.

## Phase 6: Integration

- [ ] 6.1 Run the full schema-shape + permission-matrix + base-data suite against a fresh `fact_test_<id>` end to end.
- [ ] 6.2 Verify `SmartNet.Db.Runner` halts before any downstream artifact on non-zero exit (ADR 0012 order), per Decision 1.
