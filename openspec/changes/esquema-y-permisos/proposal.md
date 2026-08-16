# Proposal: Esquema y Permisos (BACKLOG #1)

## Intent

Every other backlog item depends on a schema that exists. This change delivers the versioned SQL that creates schema `fact` (tables, indexes, constraints), grants the two DB users their permissions, and seeds the minimal rows the system needs to start — nothing more. It is a foundation item: its value is enabling everything downstream, not completeness of business logic.

## Scope

### In Scope
- Versioned SQL scripts creating schema `fact`: all tables from the four private/contract/publication ownership classes in ADR 0003 (Python-private, .NET-private, contract tables, publication tables).
- Indexes/constraints already confirmed by exploration (`IX_Factura_Identidad`, `UQ_Factura_Procesamiento`, `UQ_Asiento_Vigente`, `CK_Linea_Tipo`, `CorrelativoAsiento` PK, rowversion columns).
- `GRANT` statements for `usr_api` and `usr_worker`, travelling in versioned SQL, including read-only `SELECT` on `dbo.Proveedor`, `dbo.CuentaContable`, `dbo.Motivo`, `dbo.Origen`.
- Base data: `EstadoIntegracion` rows (one per integration, including `WORKER`), default `Configuracion` rows, and the `MotivoAtributo` rows for the 23 motives reclassified to `02 COMPRAS` (marked `†` in `MOTIVOS-CLASIFICACION.md`).

### Out of Scope
- Creating `dbo.*` external tables — read-only references only, never DDL against `dbo`.
- **The initial `Usuario` row.** The versioned SQL creates the `Usuario` table and inserts nothing into it. The first user is created by the application's own administration command — the same one ADR 0007 already decides to build for password reset, applying the same salted key derivation. **No password hash may ever appear in versioned SQL**, because that SQL is committed to git and pushed to a remote.
- `SugerenciaCuenta` historical seed (process, not a row; depends on prefix resolution — belongs to backlog item #3 per ADR 0011).
- External accounting-system integration, data migration.
- Column-type decisions for the ~15 fields no document states a type for (see Approach).

## Capabilities

### New Capabilities
- `schema-fact`: DDL for all `fact.*` tables, indexes, constraints per ADR 0003 ownership classes.
- `db-permissions`: `usr_api`/`usr_worker` GRANTs, including the `dbo.*` read-only boundary.
- `schema-base-data`: `EstadoIntegracion`, default `Configuracion`, and the 23 reclassified `MotivoAtributo` rows.

### Modified Capabilities
None — greenfield.

## Approach

Plain versioned `.sql` files applied by a runtime-neutral tool (ADR 0016) — not EF Core Migrations, not Alembic. The **tool choice (DbUp vs Flyway vs equivalent) is an open design decision**, not resolved here, because it affects migration naming, GRANT tracking, and deploy-pipeline ordering across two runtimes (.NET + Python). The **~15 NOT STATED column types are decided when the SQL is written**; the SQL becomes the source of truth (consistent with ADR 0016 — schema as contract) rather than backfilling `TECH-DESIGN.md`. Money stays `DECIMAL(18,2)`, rate `DECIMAL(12,6)`, never float/real/double.

## Affected Areas

| Area | Impact | Description |
|------|--------|--------------|
| `SmartNet/db/schema/` (new) | New | Versioned SQL migrations for `fact` schema, grants, base data |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| `CREATE LOGIN` is instance-level, may exceed project DB rights | Medium | Design must handle both: contained DB users vs login+user; not decided here |
| Inventing a NOT STATED type here becomes a defect in 16 downstream items | Low (mitigated) | Types deferred to design/SQL authoring, made visible and reviewable, not silently filled |
| Migration tool choice affects deploy order across two runtimes | Medium | Deferred explicitly to design phase |

## Rollback Plan

Migrations are additive and versioned. A failed migration is reverted by a **down-script or a
compensating migration** — never by restoring the database.

> **Corrected during orchestrator review.** The first draft of this proposal offered "restoring the
> pre-migration schema snapshot" as a fallback. **That option does not exist here.** The database is
> **shared** with the company's accounting system (ADR 0014), so restoring it to a point in time
> reverts that system's data as well. This is finding C7 of the second adversarial review
> reappearing in the first artifact written after it closed. Any rollback path that operates on the
> whole database is out of bounds for this project, and the design must not reintroduce one.

Because the objects live in schema `fact` and nothing outside it is touched, a compensating
migration can drop or alter only this project's objects. No data migration exists yet, so rollback
carries no data-loss risk at this stage.

## Dependencies

- Instance-level `CREATE LOGIN` rights (may require DBA action — open question).
- Tool choice from design phase before scripts can be authored.

## Success Criteria

- [ ] ADR 0019 level-2 boundary contract tests pass against the created schema.
- [ ] Permission matrix verified: `usr_api` cannot read `fact.Procesamiento`; `usr_worker` cannot write `fact.Factura`.
- [ ] All `dbo.*` references are `SELECT`-only for both users.

## Proposal question round

Already resolved by the orchestrator (do not re-open): SQL as source of truth for types, tool choice deferred, base-data boundary (EstadoIntegracion/Configuracion/Usuario in, SugerenciaCuenta seed out), and DB user/permission creation ambiguity flagged as an open design risk.

Both remaining items were **resolved by the user** after this proposal was first written:

1. **`MotivoAtributo` default rows are IN scope.** The 23 motives reclassified to `02 COMPRAS` for
   the demonstration —marked `†` in `MOTIVOS-CLASIFICACION.md`— load as base data here. Known cost,
   accepted: they are domain data loaded without the code that consumes them and without tests that
   validate them. Item #3 is what will read and filter them.

2. **The initial `Usuario` row is OUT of scope**, and so is any credential handling. The versioned
   SQL creates the table and inserts nothing. The first user is created by the application's
   administration command (ADR 0007), which applies the same salted key derivation. The alternatives
   —a placeholder hash rotated at first login, or a deploy-time parameter— were rejected: the first
   publishes a known credential to git, and the second couples the schema to a migration tool that
   is not yet chosen.

> **Nota de sincronización.** This file was corrected by the orchestrator after the proposal phase
> returned. The Engram copy under `sdd/esquema-y-permisos/proposal` predates these corrections;
> **this file is authoritative**.
