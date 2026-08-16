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
  - **Settled in Phase 3:** `TestBootstrapHarnessTests.Script008_IsCreateIfAbsent_ReapplyingAgainstAlreadyMigratedDatabaseSucceeds`
    applies the full migration set once (roles, membership and every `GRANT`/`DENY` now exist),
    deletes only `008`'s row from `fact.SchemaVersions` (DbUp's own journal would otherwise skip an
    already-applied script, proving nothing about `008`'s own idempotency), and re-runs the
    migration set — asserting the second run also exits `0`.
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

- [x] 3.1 RED: ADR 0019 level-2 matrix suite — one `EXECUTE AS USER` case per spec scenario (deny `usr_api`→`Procesamiento`/`DatosExtraidos`; deny `usr_worker` writes/reads per matrix; per-table allow sets; `OutboxEvent`/`InboxEvent`/`CommandQueue` split grants; shared `TipoCambio`/`EstadoIntegracion`; `Configuracion` split; five `dbo.*` SELECT-only, no other `dbo` grant).
  - **RED/GREEN granularity actually executed (documented per the hard constraint on TDD
    compression):** all 18 `PermissionMatrixTests` were authored together against a database with
    001-007 applied but no `008`, then run once. Every "denied" assertion trivially passed (no
    `GRANT` exists without `008`, so the engine already denies everything by default); every
    "succeeds" assertion failed for the right reason — error 229, permission denied, because no
    `GRANT` existed yet (15 failed / 4 trivially-true denials, one genuine test bug unrelated to
    permissions — a missing `ProveedorCodigo` in a seed insert — fixed before re-running). That is
    RED. `008` was then authored once and the suite re-run to GREEN. Mirrors the same
    one-round-trip-per-direction compression documented for Phase 2's task 2.13.
  - **Discovery not in the design:** the throwaway `fact_test_<id>` databases have never had the
    four external `dbo.*` catalogs (`Proveedor`, `CuentaContable`, `Motivo`, `Origen`) created in
    them — `SmartNet/db/fixtures/` is a manual, by-hand step against a real environment
    (`010_dbo_catalogos_ddl.sql` + `020_dbo_catalogos_datos.sql`), never wired into
    `TestDatabaseFixture`. `008`'s `GRANT SELECT ON OBJECT::dbo.<table>` statements need the object
    to exist to succeed. Added `TestDatabaseFixture.CreateExternalDboCatalogsAsync()` — bare
    structure only (no `DocumentoIdentidad`, no data, no FK), test-only infrastructure, never
    applied to `BDSmartNet` — called before `RunMigrations()` wherever `008` will run. This also
    required updating `SchemaShapeTests.cs`'s `MigratedDatabase()` helper (Phase 2, already
    complete) to create `usr_api`/`usr_worker` (WITHOUT LOGIN) and the five `dbo.*` fixtures first,
    since `008` now THROWs 50001 without them, and relaxing
    `NoTableCreatedByThisProject_ExistsOutsideSchemaFact`'s schema filter from `NOT IN ('fact')` to
    `NOT IN ('fact', 'dbo')` — the five `dbo.*` catalogs are pre-existing, externally-owned objects
    in every real deployment, not evidence of this project writing outside `fact`. Both are test
    file changes, not schema-script changes; scripts 001-007 were not touched.
- [x] 3.2 GREEN: `008_usuarios_y_permisos.sql` — `THROW 50001` guard for missing `usr_api`/`usr_worker` login; `CREATE USER … FOR LOGIN` (create-if-absent); roles `fact_api`/`fact_worker`; `GRANT`/`DENY` per table; object-level `GRANT SELECT` on the five `dbo.*` tables only; `ALTER USER … DEFAULT_SCHEMA = fact`.
  - Originally: (1) the explicit-`DENY` scope covered only the 4 tables design.md's first draft
    literally named for `fact_worker`, the rest of the .NET-private bucket relying on
    absence-of-`GRANT`; (2) `FOR LOGIN` (real deploy) and `WITHOUT LOGIN` (test harness) both
    satisfy the same `IF DATABASE_PRINCIPAL_ID(...) IS NULL` guard without branching the script
    (unchanged). **Widened (coordinator-directed follow-up, item 1):** the explicit `DENY` to
    `fact_worker` now covers the full eleven-table .NET-private bucket from ADR 0003 — verified
    directly against the ADR's own table (negocio: `Factura`, `AsientoContable`,
    `AsientoContableDetalle`, `AdjuntoManual`, `AuditoriaCorreccion`, `FacturaExtraccion`,
    `CorrelativoAsiento`; satélites: `ProveedorAtributo`, `MotivoAtributo`, `SugerenciaCuenta`;
    seguridad: `Usuario`) — with `design.md`'s Decision 3 updated to match. New RED-first test
    `UsrWorker_HasExplicitDeny_OnFullDotNetPrivateBucket` asserts explicit `DENY` state in
    `sys.database_permissions` (the property that actually distinguishes this from
    absence-of-`GRANT`, which already denied behaviorally either way). **Widened (item 2):** a
    fifth external catalog, `dbo.DocumentoIdentidad` (the FK target of `dbo.Proveedor.coddocide`),
    granted `SELECT` to both roles; `spec.md` updated from four to five throughout.
- [x] 3.3 GREEN: extend 3.2 for `OutboxEventIntegracion` child-table grants (api: INSERT/SELECT; worker: SELECT/UPDATE).
- [x] 3.4 RED: reproducibility test — apply migrations to two independently created databases; compare `fn_my_permissions`/`sys.database_permissions` sets for both roles; expect identical.
  - `PermissionReproducibilityTests.cs`. `sys.database_permissions` string columns carry SQL
    Server's fixed catalog collation (`Latin1_General_CI_AS_KS_WS`), which conflicted with this
    server's database-default collation (`Modern_Spanish_CI_AS`) under string concatenation in
    `ORDER BY`; fixed with an explicit `COLLATE DATABASE_DEFAULT` cast on each column before
    concatenating. Not a finding about `BDSmartNet`'s own collation (design.md's open question)
    — SQL Server's catalog views always carry this fixed collation regardless of database collation.

### Work Unit 3 — coordinator-directed follow-up (post-independent-verification)

Four gaps found by the coordinator's independent verification of Work Unit 3, decided by the user,
implemented here. Items 1-2 are reflected in the task notes above (3.1/3.2) and in `design.md`/
`spec.md`; item 4 is noted under task 5.5. This entry covers item 3, which has no single task to
attach to.

- **Item 3 — test-database leak, root cause and fix.** The 44 leaked `fact_test_<id>` databases
  were **not** a `DisposeAsync()` bug: reproduced in isolation (a test that creates a database then
  throws) and disposal ran correctly. The real cause: `MigratedDatabase()`/
  `MigratedDatabaseWithUsers()` helper methods created the `TestDatabaseFixture` as a bare local
  (`var db = await TestDatabaseFixture.CreateAsync();`) and then ran further `await`ed setup
  (`CreateWithoutLoginUserAsync`, `RunMigrations` + `Assert.Equal(0, exitCode)`) *before* `return
  db;` — all *outside* any `try`/`finally`. Callers wrapped the helper's return value in `await
  using var db = await MigratedDatabase();`, but if the helper itself threw before reaching `return
  db;` (which is exactly what happened repeatedly while `008` and its test infrastructure were
  still being debugged — every `Assert.Equal(0, exitCode)` failure), the assignment never
  completed, the caller's `await using` disposal machinery was never registered, and the
  already-created database was orphaned with nothing left to drop it. Confirmed by reproduction: a
  clean, fully-passing run leaked 0 databases; forcing the same failure pattern that produced the
  original 44 (helper throwing between create and return) reproduced the leak; wrapping each
  helper's body in `try { ... return db; } catch { await db.DisposeAsync(); throw; }` fixed it —
  confirmed by a full `dotnet test` run (57/57 passing) leaving 0 `fact_test_%` databases behind.
  Fixed in `SchemaShapeTests.MigratedDatabase()`, `PermissionMatrixTests.MigratedDatabaseWithUsers()`,
  `PermissionReproducibilityTests.MigratedDatabaseWithUsers()`. Also added, as defense in depth
  (not a substitute for the real fix): a retry-with-backoff around `TestDatabaseFixture.DisposeAsync()`'s
  `ALTER DATABASE`/`DROP DATABASE` for transient lock contention under xUnit's cross-class
  parallelism, and `TestDatabaseFixture.SweepOrphanedTestDatabasesAsync()` — a `Lazy<Task>`-guarded,
  once-per-process sweep of any `fact_test_%` database run before the first `CreateAsync()`,
  strictly scoped by an escaped `LIKE 'fact\_test\_%' ESCAPE '\'` pattern so `master` and
  `BDSmartNet` are structurally unreachable by name. Verified directly: manually created an orphan
  database matching the pattern, ran one harness test, confirmed the orphan was swept while
  `master`/`BDSmartNet` were untouched.

## Phase 4: Base Data

- [x] 4.1 RED: `EstadoIntegracion` test — exactly the seeded name set, `WORKER.FallosSeguidos = 0`.
  - `BaseDataTests.cs`. The seeded set is the **five** names `spec.md` states literally
    ("GMAIL, DRIVE, SHEETS, SBS, WORKER — five rows, no more, no fewer"), not the seven design.md's
    still-open Open Question speculated (`TELEGRAM`/`CORREO` added on the theory that ADR 0003 rev 4
    should win over TECH-DESIGN). **Discovery, not invention:** `spec.md` had already settled this
    explicitly and was never inconsistent; `design.md`'s own planning paragraph and Open Question
    simply hadn't been updated to match once `spec.md` did. Found and fixed in `design.md` this
    session (both marked resolved, in the five-row direction `spec.md` already committed to) — not
    silently picked a side of a live disagreement.
- [x] 4.2 RED: `Configuracion` test — every TECH-DESIGN section returns ≥1 row; `pendiente` keys have `Valor`/`ValorPorDefecto` both `NULL`.
  - Six sections, not the five TECH-DESIGN's prose lists verbatim: `INGESTA`, `ADJUNTOS`,
    `TELEGRAM`, `NOTIFICACIONES`, `INTEGRACIONES` cover TECH-DESIGN's eight named topics; a sixth,
    `CONTABILIDAD` (key `FECHA_CORTE_CONTABLE`), was added because `REGLAS.md` §7 invariant 3 and
    TECH-DESIGN's own Flujo-3 acceptance criteria reference `Configuracion.FechaCorteContable`
    literally, and without a row there is nowhere for that invariant to read its own value. Not one
    of `REGLAS.md` §12's six "pendiente de ratificación" points (those are accounting-criterion
    disputes; this is an undecided operational value), so it is seeded `pendiente` under the same
    rule ADR 0013 already established for attachment types/size, not treated as a special case.
    Exactly two keys carry a real (non-`pendiente`) `ValorPorDefecto`, each traced to one document:
    `NOTIFICACIONES.CANAL_ALERTA_FALLBACK = 'CORREO'` (TECH-DESIGN.md + ADR 0015: "fallos por
    Telegram con respaldo por correo") and `INTEGRACIONES.INTERVALO_ESPERADO_WORKER = '30'` (ADR
    0015: "30 minutos como punto de partida"). Every other key — Gmail label, allowed extensions,
    poll frequency, start date, manual-attachment types/max size, Telegram chat id, display
    preference, the four non-`WORKER` expected intervals, and `FECHA_CORTE_CONTABLE` — is seeded
    `pendiente` (`Valor`/`ValorPorDefecto` both `NULL`) because no document states a concrete value,
    following design.md's own stated policy literally rather than inventing one to make a test pass.
- [x] 4.3 RED: `MotivoAtributo` test — exactly 23 rows for motives 5,13,16,17,18,19,20,21,30,38,40,42,46,48,49,53,56,59,60,77,81,88,90, all `OrigenLibro='02'`, `Activo=1`.
  - **Recounted independently against `MOTIVOS-CLASIFICACION.md` itself** (not trusted from this
    file, per the coordinator's explicit instruction): `grep`-counted every row carrying `†` in the
    document's "Tabla completa" — **23**, exactly the list already written above, and exactly
    matching `spec.md`. Cross-checked: 27 plain `02` rows + 23 `†` rows = 50, matching the "Reparto
    final" table's stated `50`. No discrepancy found; nothing needed to stop for. Also asserts the
    negative: five motives from the `dbo.Motivo` test fixture that are **not** `†`-marked (11, 12,
    22 — plain `02`; 1, 28 — `BAJA`) do not appear in `fact.MotivoAtributo` at all.
- [x] 4.4 RED: `Usuario` empty test — `COUNT(*) = 0`; no `INSERT` targeting `fact.Usuario` anywhere in versioned SQL.
- [x] 4.5 GREEN: `009_datos_base.sql` — `EstadoIntegracion` rows, `Configuracion` defaults, all `NOT EXISTS`-guarded.
- [x] 4.6 GREEN: `010_motivo_atributo_demo.sql` — `INSERT … SELECT` from `dbo.Motivo` matched by motive number; `IF @@ROWCOUNT <> 23 THROW`.
  - **Dependency surfaced and closed:** `010` needs `dbo.Motivo` rows to select from, but
    `CreateExternalDboCatalogsAsync()` (Work Unit 3) creates the external catalogs empty. Added
    `TestDatabaseFixture.SeedDboMotivoFixtureRowsAsync()` — 28 rows (the 23 reclassified + 5 more
    for the negative check above), copied verbatim from `MOTIVOS-CLASIFICACION.md` for
    traceability, explicitly documented as a TEST FIXTURE this project does not own, never applied
    outside `TestDatabaseFixture`. Every test helper that runs the full migration set now calls it
    (`SchemaShapeTests`, `TestBootstrapHarnessTests`, `PermissionMatrixTests`,
    `PermissionReproducibilityTests`, `BaseDataTests`) — 010 THROWs 50002 without it, which is how
    the gap was found (a real RED, not a hypothetical one: the first full-suite run after 010 was
    added failed exactly this way).
  - **Bug found and fixed in the Phase 3 lint, before it could ship broken:** `DboWriteLintTests`'s
    first draft flagged *any* mention of `dbo` outside an allowed `GRANT` line — which would have
    wrongly rejected `010`'s own `INSERT INTO fact.MotivoAtributo ... SELECT ... FROM dbo.Motivo`,
    a read, the moment this script was written. Verified before fixing (a throwaway reflection probe
    against the old implementation confirmed it really did flag that exact statement), then rewrote
    the lint to check each SQL statement for a forbidden verb whose own target is `dbo.*`, never a
    bare mention of the word — reads were always meant to be allowed (ADR 0003: "nadie escribe una
    tabla externa", not "nadie la lee"). A new regression test
    (`Lint_AllowsSelectReadsFromDbo_IncludingAsAnInsertSelectSource`) proves the fixed lint accepts
    exactly this shape; the original violation-detection tests were re-run to confirm they still
    catch real violations after the rewrite.
- [x] 4.7 RED: dbo-write-safety test — every `INSERT` in 4.5/4.6 targets a `fact.`-qualified table.
  - **Not duplicated.** The (corrected, see 4.6's note) Phase 3 lint,
    `DboWriteLintTests.RealSchemaScripts_HaveNoDisallowedDboMentions`, already re-scans every script
    under `SmartNet/db/schema/` on every test run — 009 and 010 included automatically, no wiring
    needed — for exactly this property: no `INSERT`/`UPDATE`/`DELETE`/`CREATE`/`ALTER`/`DROP`/
    `TRUNCATE`/`MERGE` whose own target is `dbo.*`, and no `REFERENCES dbo.*`. A second test
    asserting the identical fact from the identical files would be redundant coverage, not defense
    in depth, so none was written; `BaseDataTests.cs` says so explicitly in its own class comment.

## Phase 5: CI Hash Manifest and Advisory Rollback

- [x] 5.1 GREEN: generate `SmartNet/db/schema/checksums.txt` hashing every applied `*.sql`.
  - Generated by `generate-checksums.ps1` (run by hand, deliberately). Scope is the top level of
    `schema/` only: `rollback/*.sql` is never applied, so "edited after apply" is not a notion that
    applies to it.
- [x] 5.2 RED: CI step test — re-hashing an already-listed script that was edited fails the build.
  - `ChecksumManifestTests` reimplements the hashing in C# rather than shelling out to the
    PowerShell generator, so the two must agree in their own code instead of sharing one.
    Edited-after-hash and a manifest entry whose file vanished are **errors**; a script present but
    unlisted is a **warning**. Probe-verified by the coordinator: appending one comment line to
    `007_publicacion.sql` turns `RealManifest_MatchesTheRealScripts_Exactly` red. That is precisely
    the drift DbUp ignores.
- [ ] 5.3 GREEN: add the CI re-hash step.
  - Deliberately not done in this unit: the repository had **no CI pipeline at all** — no
    `.github/`, no workflow. Creating one publishes automation to a public GitHub repository, which
    is the user's call, not the agent's. The user has since chosen a **two-job workflow** (a
    database-free job for build + `dbo` lint + checksum verification, and a job running the full
    suite against SQL Server as a service container). Every check here is already a single command,
    so the remaining work is wiring only.
- [x] 5.4 GREEN: author `SmartNet/db/schema/rollback/NNN_down.sql` per script (advisory, never executed by the runner).
  - Ten down scripts, one per forward script. Compensating migrations scoped to `fact`, never a
    snapshot restore: the database is shared with the company's accounting system, so restoring a
    snapshot would revert that system too (finding C7). `RollbackAdvisoryTests` asserts the runner
    never picks them up, that every forward script has a companion, and that they tear `fact` down
    cleanly in descending order.
- [x] 5.5 GREEN: add CI lint rejecting unqualified `dbo.` outside the five permitted `GRANT SELECT ON OBJECT::dbo.*` lines.
  - Detection logic done and hardened (Phases 3–4). Extended by the coordinator in this unit to
    scan `schema/` **recursively**, so `rollback/` is covered: the down scripts are advisory, but a
    human may run one by hand against a real database, which makes them exactly where an unnoticed
    `dbo` write would do its damage. CI packaging rides along with 5.3.
  - **Partially satisfied by `DboWriteLintTests.cs`** (Work Unit 3, coordinator-directed follow-up
    item 4): the detection logic itself exists and runs today — a static text scan of
    `SmartNet/db/schema/*.sql` that rejects any `CREATE`/`ALTER`/`DROP`/`INSERT`/`UPDATE`/`DELETE`/
    `TRUNCATE`/`MERGE`/`REFERENCES` targeting `dbo`, permitting only
    `GRANT SELECT ON OBJECT::dbo.<table> TO fact_api|fact_worker;` lines and SQL comments. RED-first
    against two synthetic violating scripts (a `dbo` write, a `dbo` `REFERENCES`) before the lint
    logic existed, then GREEN; the real guardian test (`RealSchemaScripts_HaveNoDisallowedDboMentions`)
    runs the same lint against the actual 001-008 scripts and passes today. **What remains open for
    this task:** wiring that test into an actual CI *step* (a pipeline job, not just `dotnet test`
    membership) is not part of this SDD change's own CI configuration and is left for whoever adds
    CI to the repository.

## Phase 6: Integration

- [ ] 6.1 Run the full schema-shape + permission-matrix + base-data suite against a fresh `fact_test_<id>` end to end.
- [ ] 6.2 Verify `SmartNet.Db.Runner` halts before any downstream artifact on non-zero exit (ADR 0012 order), per Decision 1.
