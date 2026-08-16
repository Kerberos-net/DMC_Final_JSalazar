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

- [ ] 2.1 RED: schema-shape test — `sys.tables`/`sys.schemas` inventory expects every table in spec's full list, all in `schema_name='fact'`, none named `Proveedor`/`CuentaContable`/`Motivo`/`Origen`.
- [ ] 2.2 GREEN: `001_esquema_fact.sql` — `CREATE SCHEMA fact`.
- [ ] 2.3 GREEN: `002_seguridad.sql` — `fact.Usuario` (no `INSERT`, `ClaveHash NVARCHAR(200)`, `BloqueadoHasta DATETIME2(3)`).
- [ ] 2.4 GREEN: `003_ingesta_y_procesamiento.sql` — `Email`, `DocumentoRecibido`, `Procesamiento`, `DatosExtraidos`, `ProcesamientoError`, `ProcesamientoIntentos`.
- [ ] 2.5 GREEN: `004_satelites_datos_maestros.sql` — `ProveedorAtributo`/`SugerenciaCuenta` keyed on `ProveedorCodigo CHAR(6)`; `MotivoAtributo` keyed on `Motivo INT`, `CuentaCodigo VARCHAR(10)`.
- [ ] 2.6 GREEN: `005_negocio.sql` — `Factura`, `FacturaExtraccion`, `AsientoContable`, `AsientoContableDetalle`, `CorrelativoAsiento`, `AdjuntoManual`, `AuditoriaCorreccion`.
- [ ] 2.7 RED: index/constraint tests — `IX_Factura_Identidad` (`is_unique=0`), `UQ_Factura_Procesamiento`, `UQ_Asiento_Vigente`, `CK_Linea_Tipo` (4 accept/reject cases), `CorrelativoAsiento` PK + no `sys.identity_columns`/`sys.sequences` row.
- [ ] 2.8 GREEN: apply filtered indexes and `CK_Linea_Tipo` inside `005_negocio.sql` to satisfy 2.7.
- [ ] 2.9 GREEN: `006_contratos.sql` — `OutboxEvent`, `OutboxEventIntegracion`, `CommandQueue`, `InboxEvent`; `Secuencia BIGINT` fed by `SEQUENCE fact.SeqOutbox`.
- [ ] 2.10 GREEN: `007_publicacion.sql` — `TipoCambio`, `Configuracion`, `EstadoIntegracion`.
- [ ] 2.11 RED: money/rate type test — no `float`/`real` in schema `fact`; every named monetary column `DECIMAL(18,2)`; every rate column `DECIMAL(12,6)`.
- [ ] 2.12 RED: `rowversion` test — exactly `Factura.Version` and `AsientoContable.Version`; none on `AsientoContableDetalle`.
- [ ] 2.13 GREEN: adjust column definitions across 2.3–2.10 to satisfy 2.11–2.12.

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
