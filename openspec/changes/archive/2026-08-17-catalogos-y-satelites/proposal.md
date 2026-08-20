# Proposal: Catálogos y Satélites (BACKLOG #3)

## Intent

Item #1 already shipped the DDL and grants for the 3 own satellite tables (`fact.ProveedorAtributo`,
`fact.MotivoAtributo`, `fact.SugerenciaCuenta`) and the read grants on the 5 external `dbo.*`
catalogs (ADR 0003 Rev.5). No C# application code reads any of them yet, and the prefix-resolution
rule in `REGLAS.md` §3 is not implemented anywhere. Item #8 (accounting core) cannot build against a
real chart of accounts without this layer, and item #7 depends on it too. This change is exclusively
the application layer over an already-built schema — not another round of DDL.

## Scope

### In Scope
- Read-only repositories over the 5 external catalogs: `Proveedor`, `CuentaContable`, `Motivo`,
  `Origen`, `DocumentoIdentidad` (ADR 0003 Rev.5).
- Read/write repositories over the 3 existing satellites: `ProveedorAtributo`, `MotivoAtributo`,
  `SugerenciaCuenta`. For `SugerenciaCuenta`, this item exposes storage access only (no ranking or
  cascade logic — see Out of Scope).
- Pure prefix-resolution function (REGLAS.md §3): given a `Motivo.cuenta` value (comma-separated
  2–6 digit prefixes), returns the matching 6-digit leaf accounts (`nivel` empty, 907 total) whose
  `cuenta` starts with any prefix (`LIKE prefijo + '%'`).
- `SmartNet.Catalogos.Core` (pure domain, no DB/HTTP/clock — ADR 0019) and
  `SmartNet.Catalogos.Infrastructure` (SQL adapters), replicating the `SmartNet.Auth.*` pattern from
  item #2, each with its own `.Tests` project.

### Out of Scope
- The frequency cascade / ranking logic that turns `SugerenciaCuenta` rows into a suggested account
  and its initial seeding from history — that is item #9, which depends on item #8. This item only
  gives item #9 a repository to read/write against.
- Any DDL/GRANT changes — the 3 satellites and the 5 `dbo.*` grants already exist (item #1).
- `ctarefleja`/`ctapuente` freezing into asiento lines — reading these columns is in scope here;
  freezing them at confirmation time is item #8's job.
- Reconciling `REGLAS.md` §2's 4-catalog table with ADR 0003 Rev.5's 5 catalogs — flagged as a risk
  below, not resolved by this change.

## Capabilities

### New Capabilities
- `catalogos-externos`: read-only repositories over the 5 `dbo.*` catalogs.
- `satelites-propios`: repositories over the 3 `fact.*` satellites (read/write, no FK to `dbo.*` per
  ADR 0003 — runtime validation against `dbo.*`, not referential integrity).
- `resolucion-de-prefijos`: pure function resolving `Motivo.cuenta` prefixes against the 907 leaf
  accounts.

### Modified Capabilities
None.

## Approach

Hexagonal, same pattern as item #2's `SmartNet.Auth.*`: `SmartNet.Catalogos.Core` holds repository
interfaces (`ICuentaContableRepository`, `IMotivoRepository`, etc.) and `ResolverCandidatas` as a
pure function, testable without SQL Server. `SmartNet.Catalogos.Infrastructure` implements
`Sql*Repository` adapters — read-only for the 5 external catalogs, read/write for the 3 satellites.
Domain names in Spanish (`CuentaContable`, `ResolverCandidatas`, `CtaReflejaCodigo`), technical
scaffolding in English (`ICuentaContableRepository`) per CONVENTIONS.md.

## Affected Areas

| Area | Impact | Description |
|------|--------|--------------|
| `SmartNet/catalogos/SmartNet.Catalogos.Core/` | New | Repository interfaces, `ResolverCandidatas`, domain types |
| `SmartNet/catalogos/SmartNet.Catalogos.Infrastructure/` | New | `Sql*Repository` adapters over `dbo.*` and `fact.*` |
| `SmartNet/db/schema/` | None | No change — DDL/GRANT already complete from item #1 |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| `REGLAS.md` §2 lists only 4 catalogs, ADR 0003 Rev.5 lists 5 (`DocumentoIdentidad` missing) | Low | Spec must cite ADR 0003 as the authority for "5 catalogs"; not a real accounting contradiction, just doc drift — not fixed here |
| No FK between satellites and external catalogs (ADR 0003) — prefix resolution and satellite writes must validate against `dbo.*` at runtime | Medium | Design phase must specify runtime validation (e.g. existence checks before write), not rely on the engine |
| `Cuentas.xlsx` binary not read directly; structure inferred via fixtures/DDL | Low | If concrete test data is needed, open the file before writing spec examples |
| `CuentaContable.cuenta` must stay `VARCHAR`, never `CHAR` — fixed-length padding breaks `LIKE prefijo + '%'` | Low | Non-negotiable per exploration; call out explicitly in spec/design |

## Rollback Plan

Pure additive application code — two new projects with no schema changes. Reverting the commit(s)
removes `SmartNet.Catalogos.Core`/`.Infrastructure` entirely with no data or grant impact; no
compensating migration needed.

## Dependencies

- Item #1 (schema and grants, complete).
- `Cuentas.xlsx` (real chart of accounts) as mandatory context per BACKLOG.md — already used in
  exploration for structure; open directly if spec needs concrete row-level examples.

## Success Criteria

- [ ] All 5 external catalog repositories are read-only (no `INSERT`/`UPDATE`/`DELETE` methods).
- [ ] `ResolverCandidatas` is unit-tested with zero DB dependency, matching REGLAS.md §3's motivo 22
      (1 candidate) and motivo 8 (22 candidates, 5 prefixes) examples.
- [ ] Satellite repositories exercise the existing `fact_api` grants (no schema change required to
      pass).
- [ ] `SmartNet.Catalogos.Core` has zero `PackageReference`/DB/HTTP/clock dependency (ADR 0019,
      same purity scan pattern as `SmartNet.Auth.Core`).
- [ ] `SugerenciaCuenta` repository is CRUD-only — no ranking/cascade logic present in this item.

## Proposal question round

Decisions already fixed by prior items/ADRs and **not** reopened here:

1. The 5 external catalogs and their read-only access: **ADR 0003 Rev.5**.
2. The 3 satellite tables' schema and grants: **already built in item #1**, unchanged here.
3. Prefix resolution is application logic, not SQL (`LIKE` in a view/function): **ADR 0019 +
   TECH-DESIGN.md line 463-464** rule this out explicitly.
4. `SugerenciaCuenta`'s frequency-cascade ranking is **item #9's job, not this item's** — confirmed
   against BACKLOG.md's dependency chain (`#9` depends on `#8`, which depends on `#3`).

One item flagged for confirmation, not resolved by the above:

- **REGLAS.md §2 vs ADR 0003 Rev.5 (4 vs 5 catalogs).** Assumption used in this proposal: this is
  documentation drift, not a live accounting rule conflict — `DocumentoIdentidad` supports
  `Proveedor.coddocide`, it is not a "business catalog" in REGLAS.md's sense. The spec phase should
  state this explicitly rather than silently pick 5. Flag if this reading is wrong.
