# Tasks: Autenticación y Sesión (BACKLOG #2)

## Review Workload Forecast

| Field | Value |
|-------|-------|
| Estimated changed lines | ~3400–3900 (`011`/`012` DDL+rollback+grants ~155; schema-shape/permission-matrix/grant-gap test extensions ~290; `SmartNet.Auth.Core` ~450 + `SmartNet.Auth.Core.Tests` ~550; `SmartNet.Auth.Infrastructure` ~450 + its adapter/permission-sufficiency tests ~350; `SmartNet.Api` ~250 + `SmartNet.Api.Tests` ~500; `SmartNet.Admin` ~200 + tests ~150; `SmartNet.sln` + CI diff + final gates ~80–100) |
| 400-line budget risk | High — every Work Unit below except WU1, WU6 and WU7 individually exceeds the 400-line review budget on its own |
| Chained PRs recommended | Yes |
| Suggested split | WU1 → WU2 → WU3 → WU4 → WU5 → WU6 → WU7 (seven PRs, strictly sequential — see dependency note below) |
| Delivery strategy | ask-on-risk — this forecast flags risk, so chained delivery is a stop-and-ask, not a silent decision |
| Chain strategy | pending — orchestrator to ask user |

Decision needed before apply: Yes
Chained PRs recommended: Yes
Chain strategy: pending
400-line budget risk: High

**Nuance, and why the split differs from item #1's shape.** Item #1 was almost entirely declarative
SQL, so its five units could be reviewed structurally against a permission matrix. This item is
different in kind: `SmartNet.Auth.Core` (Decision 8's escalation arithmetic) is branching,
stateful-transition logic with a 25-row worked sequence behind it — genuine business-rule review,
not shape-matching. `SmartNet.Api` and `SmartNet.Admin` are first-time HTTP/CLI surfaces for this
repository. None of that lowers per-line review cost the way item #1's SQL did, so the recommended
split is **strict**, not a courtesy: each Work Unit below is also a hard dependency boundary — WU3
cannot compile against ports WU2's schema hasn't shipped grants for (transitively, via WU4), WU4
needs WU3's ports, WU5 needs WU4's adapters, WU6 needs WU3+WU4, WU7 needs everything. There is no
unit here that could be reordered for reviewer convenience the way item #1's base-data unit could.

### Suggested Work Units

| Unit | Goal | Likely PR | Focused test command | Runtime harness | Rollback boundary |
|------|------|-----------|----------------------|-----------------|-------------------|
| 1 | Gates: Konscious package verification, Data Protection key-ring path decision | PR 1 | N/A — research/decision, no test target | None | Revert the decision note; blocks nothing downstream if reversed in writing |
| 2 | Schema: `011_sesion.sql`, `012_usuario_nivel_bloqueo.sql`, schema-shape + permission-matrix extensions, checksum regen | PR 2 | `dotnet test SmartNet.Db.Runner.Tests` (extended suites) | Runner over `fact_test_<id>`, `TestDatabaseFixture` | New compensating migration (`011_down.sql`, `012_down.sql`); no DROP of item #1's objects |
| 3 | `SmartNet.Auth.Core` — pure domain, ports, PHC codec, purity scan | PR 3 | `dotnet test SmartNet.Auth.Core.Tests` | None — zero DB/HTTP/clock by construction | Delete `SmartNet/auth/SmartNet.Auth.Core*` |
| 4 | `SmartNet.Auth.Infrastructure` — Argon2id adapter, SQL adapters, `ITicketStore` | PR 4 | `dotnet test SmartNet.Auth.Infrastructure.Tests` | `TestDatabaseFixture`, `ExecuteAsUserAsync("usr_api", …)` | Delete `SmartNet/auth/SmartNet.Auth.Infrastructure*` |
| 5 | `SmartNet.Api` — Minimal APIs host, cookie auth, `/api/sesion` | PR 5 | `dotnet test SmartNet.Api.Tests` | `WebApplicationFactory` + `TestDatabaseFixture` | Delete `SmartNet/api/` |
| 6 | `SmartNet.Admin` — CLI verbs | PR 6 | `dotnet test SmartNet.Admin.Tests` | `TestDatabaseFixture` | Delete `SmartNet/admin/` |
| 7 | `SmartNet.sln`, CI confirmation, full end-to-end run, final gates | PR 7 | full `dotnet test` at solution level | Full suite, fresh `fact_test_<id>` per test | Remove `SmartNet.sln`; CI diff revert |

---

## Phase 0: Gates — resolved before any code references them

These are explicitly the two design-doc open items the design called out as MUST-be-early, not
footnotes. Neither has a database or code dependency, so they run first and block nothing else from
starting review, but WU4 (Argon2 `PackageReference`) and WU5 (key-ring wiring) may not proceed past
their first commit until the corresponding gate below is closed.

- [x] 0.1 GATE — Konscious.Security.Cryptography.Argon2 package verification: **CLOSED, 2026-08-16,
      verified against nuget.org and GitHub with network access.** Current version `1.3.1` (published
      2024-06-19), license confirmed `MIT`. Maintenance status: **not archived, not disabled** (277
      stars, 20 open issues), but flagged — the `master` branch's last commit is 2024-06-18 (stale
      2+ years) and PR #66 ("Add .NET 10 target") is open/unmerged as of 2026-06-17. The package
      installs cleanly on `net10.0` via its `net8.0` dependency group; no `net10.0`-specific asset is
      needed. **Explicitly checked whether .NET 10 shipped first-party Argon2id: it did not.** .NET
      10's cryptography additions (per `dotnet/core` release notes and
      `docs/core/whats-new/dotnet-10/libraries.md`) are certificate-thumbprint lookup, PEM/PKCS#12
      changes, AES KeyWrap with padding, and post-quantum crypto (ML-DSA/CNG) — no Argon2 of any kind.
      `dotnet/runtime` issue #19933 ("Add Argon2 support to System.Security.Cryptography") remains
      **open**, still in the generic "Future" milestone. **Decision 1 is NOT reversed.** Full note
      recorded in `design.md` Decision 1 with the pinned version for task 4.1's `PackageReference`.
- [x] 0.2 GATE — Data Protection key-ring persistence path: **CLOSED.** Path resolved the same way as
      `SMARTNET_API_DB_CONNECTION` (Decision 6) — env var `SMARTNET_API_KEYRING_PATH`, no default, fail
      loudly if absent. Recommended concrete value for this project's single-instance Windows
      deployment (ADR 0012): `C:\ProgramData\SmartNet\dataprotection-keys` — machine-wide, outlives a
      service-account profile, never inside the Git checkout. Added to ADR 0014's backup set
      (Revisión 4) with the justification that losing it invalidates every live session cookie on
      restart, defeating the reason `fact.Sesion` was chosen over an in-memory store. Full note in
      `design.md` Decision 4; wiring itself is still task 4.10, not done here.

## Phase 1: Schema — `fact.Sesion` and `NivelBloqueo` (RED before each script)

- [x] 1.1 RED: schema-shape test for `fact.Sesion` — column list/types (`SesionId BIGINT IDENTITY`,
      `TokenHash CHAR(64)` with `Latin1_General_100_BIN2` collation, `UsuarioId BIGINT`,
      `CreadaEn`/`ExpiraEn`/`UltimaActividadEn` `DATETIME2(3)`, `RevocadaEn` nullable,
      `MotivoRevocacion VARCHAR(20)` nullable, `Ticket NVARCHAR(MAX)`), `PK_Sesion`,
      `UQ_Sesion_TokenHash`, `FK_Sesion_Usuario → fact.Usuario`, `CK_Sesion_Revocacion` (paired-nullity
      check), `CK_Sesion_MotivoRevocacion` (value list `CIERRE_SESION`/`RESTABLECIMIENTO`/`ADMIN`),
      filtered index `IX_Sesion_UsuarioId_Activa ON (UsuarioId, ExpiraEn) WHERE RevocadaEn IS NULL`.
      **RED confirmed 2026-08-16**: 3 tests added to `SchemaShapeTests.cs`
      (`FactSesion_HasExpectedColumnsTypesAndCollation`, `FactSesion_HasExpectedConstraints`,
      `CkSesionRevocacion_EnforcesPairedNullity` — a 4-case `[Theory]`). Run against the repo before
      `011` existed: 5 of 7 cases failed for the right reason (`Invalid object name 'fact.Sesion'` /
      "Expected: ... Actual: null"); the other 2 `Theory` cases (both `shouldSucceed: false`) passed
      trivially — the same documented compression `PermissionMatrixTests.cs` already uses (any
      exception satisfies "should fail", including "table does not exist").
- [x] 1.2 GREEN: `011_sesion.sql` — the DDL from design.md Decision 2, plus `fact_api` grants
      (`SELECT, INSERT, UPDATE, DELETE`) and the explicit `fact_worker` four-verb `DENY`, in the same
      file per Decision 2's "grants ship with DDL" rule; `rollback/011_down.sql` (advisory, drops
      table + grants, never applied by the runner). **GREEN confirmed 2026-08-16**: all 7 of 1.1's
      tests pass (one test-side bug found and fixed along the way — `sys.columns.scale` is
      `TINYINT`, not castable straight to `int`; fixed with an explicit `CAST(... AS INT)` in the
      query, not a schema issue).
- [x] 1.3 RED: permission-matrix test — `usr_api` SELECT/INSERT/UPDATE/DELETE on `fact.Sesion` all
      succeed; `usr_worker` is denied all four (`EXECUTE AS USER` pattern, mirroring
      `PermissionMatrixTests`). **Compression, acknowledged explicitly (same class already documented
      in `PermissionMatrixTests.cs`'s own header):** written and run AFTER 1.2's `011_sesion.sql`
      already existed (grants ship in the same file as the DDL, so there is no meaningful
      table-exists-but-ungranted intermediate state to RED against without hand-editing `011` to
      strip its own grants first, which nobody would ship). The two new tests
      (`UsrApi_HasFullAccess_OnFactSesion_IncludingDelete`, `UsrWorker_IsDenied_AllFourVerbs_OnFactSesion`)
      were GREEN on first run.
- [x] 1.4 Confirm 1.3 is GREEN against 1.2 without further changes (Decision 2 puts grants in the same
      file as the DDL, so this should already hold). If it does not, that is a genuine finding — record
      what was missing rather than silently patching `011` without a note. **Confirmed GREEN, no
      changes needed** — Decision 2's claim holds: `011`'s grants, shipped in the same file as the
      DDL, are sufficient with zero edits.
- [x] 1.5 RED: schema-shape test — `fact.Usuario.NivelBloqueo` is `INT NOT NULL DEFAULT (0)` with
      `CK_Usuario_NivelBloqueo CHECK (NivelBloqueo >= 0)`. **RED confirmed 2026-08-16** as part of the
      same batch as 1.1 (`FactUsuarioNivelBloqueo_IsIntNotNullDefaultZeroWithCheck`) — failed with
      `Invalid object name` / migration exit code before `012` existed.
- [x] 1.6 GREEN: `012_usuario_nivel_bloqueo.sql` — `ALTER TABLE fact.Usuario ADD NivelBloqueo …` +
      the `CHECK`, as its own script, never touching `002_seguridad.sql` (Decision 8);
      `rollback/012_down.sql` (advisory, drops constraint + column). **Genuine finding during GREEN,
      not pre-decided in design.md:** the first draft of `012` put both `ALTER TABLE` statements (ADD
      column, ADD CHECK) in one batch and failed against the real engine with error 207 ("Invalid
      column name 'NivelBloqueo'") — SQL Server compiles a batch before executing any statement in
      it, so the second statement cannot see the column the first one has not yet committed. Fixed
      with `GO` between them (design.md's own Decision 8 code block already showed this `GO`; the
      first draft dropped it by oversight, corrected before shipping). `011`/`012` never use `GO`
      elsewhere in the repo — this is the first script that needs it, and it is needed for a real,
      verified reason, not copied reflexively.
- [x] 1.7 RED: grant-inheritance test — this is the task that actually tests design.md's claim rather
      than trusting it. After `012` applies, assert directly against `sys.database_permissions` /
      `EXECUTE AS USER` behavior that (a) `usr_api`'s existing object-level `GRANT SELECT, INSERT,
      UPDATE ON OBJECT::fact.Usuario` already covers `SELECT`/`UPDATE` of the new `NivelBloqueo`
      column with no new grant statement, and (b) `usr_worker`'s existing object-level `DENY` on
      `fact.Usuario` already denies all access to the new column too. Do not assume object-level
      grants auto-cover new columns just because the design doc asserts it — prove it against the real
      engine. **VERIFIED, design.md's claim holds — no gap found.**
      `NivelBloqueo_InheritsObjectLevelGrant_FromExistingUsuarioPermissions` in
      `PermissionMatrixTests.cs` checks three things against the real engine: (a) zero rows in
      `sys.database_permissions` at column-scope (`class = 1` joined to `sys.columns` by
      `major_id`/`minor_id`) for `NivelBloqueo` — no column-level grant exists anywhere that could
      override the object-level one; (b) `usr_api` can `SELECT`/`UPDATE` the column with no code
      change; (c) `usr_worker` is denied both, exactly like the rest of the table. Same compression
      note as 1.3: written and run against `012` already applied, since `012` ships no grant
      statement to strip for a meaningful RED state.
- [x] 1.8 Confirm 1.7 is GREEN with no grant changes to `008_usuarios_y_permisos.sql`. If a gap is
      found (e.g., a column-level grant somewhere overrides the object-level one), that is a blocking
      finding for this task, not something to route around. **Confirmed GREEN, `008` untouched — no
      blocking finding.** This is the one task the coordinator's apply-scope explicitly called out as
      "actually proves the design doc's claim against the real engine, not a restatement of it," and
      it does: the test queries `sys.database_permissions` directly rather than asserting the absence
      of a diff.
- [x] 1.9 Run `DboWriteLintTests` against the schema tree that now includes `011`, `012`,
      `rollback/011_down.sql`, `rollback/012_down.sql` — confirm it still passes. Do not assume it
      "just works" because the scan is recursive; actually run it and record the result. **Actually
      run, 2026-08-16: 10/10 tests pass**, including
      `RealSchemaScripts_HaveNoDisallowedDboMentions`, which recursively scans `SmartNet/db/schema/`
      (`rollback/` included) and found zero disallowed `dbo` mentions in `011`, `012`, or either
      rollback script — none of the four touches `dbo` in any form, forward or reverse.
- [x] 1.10 Regenerate `SmartNet/db/schema/checksums.txt` via `generate-checksums.ps1` for `011` and
      `012` — the drift-detection mechanism item #1 built exists specifically for this moment.
      **Run 2026-08-16** — 12 entries written (10 existing + `011_sesion.sql` +
      `012_usuario_nivel_bloqueo.sql`), via `pwsh -File .\generate-checksums.ps1`.
- [x] 1.11 RED-then-confirm: `ChecksumManifestTests` — run before 1.10 to observe the expected
      transient warning (unlisted-but-present scripts), then re-run after 1.10 to confirm
      `RealManifest_MatchesTheRealScripts_Exactly` passes. **Both halves actually run, 2026-08-16**:
      before regen, the test failed with exactly the two expected warnings ("011_sesion.sql: exists
      on disk but is not listed…", "012_usuario_nivel_bloqueo.sql: exists on disk but is not
      listed…"), zero errors — the expected transient state. After regen, the same test passes with
      zero errors and zero warnings.
- [x] 1.12 Confirm `RollbackAdvisoryTests` picks up `011_down.sql`/`012_down.sql`, that the runner
      never applies them, and that each has a forward-script companion and tears `fact.Sesion` /
      `fact.Usuario.NivelBloqueo` down cleanly when run by hand. **Genuine finding during this task,
      not pre-decided:** the first draft of `rollback/012_down.sql` dropped the CHECK constraint then
      the column, assuming (per its own comment, since corrected) that `DROP COLUMN` auto-drops the
      column's named `DEFAULT` constraint the way it auto-drops an *unnamed* one. Verified false
      against the real engine — error 5074 ("object 'DF_Usuario_NivelBloqueo' is dependent on column
      'NivelBloqueo'") — because `DF_Usuario_NivelBloqueo` is a named constraint, not implicit. Fixed
      by explicitly dropping `DF_Usuario_NivelBloqueo` between the `CHECK` drop and the column drop.
      All 3 `RollbackAdvisoryTests` (the runner-never-executes behavioral proof, the
      every-forward-script-has-a-companion static check, and the real descending-order full-teardown
      run against a throwaway database) pass after the fix — `011_down.sql`/`012_down.sql` both
      execute successfully and `fact.Sesion`/`fact.Usuario.NivelBloqueo` are fully torn down.

## Phase 2: `SmartNet.Auth.Core` — pure domain (ADR 0019 level 1)

- [x] 2.1 Scaffold `SmartNet/auth/SmartNet.Auth.Core` (classlib, `net10.0`, zero infrastructure
      `PackageReference`s) and `SmartNet/auth/SmartNet.Auth.Core.Tests` (xUnit +
      `Microsoft.Extensions.TimeProvider.Testing`). **Done 2026-08-16.** Core project has zero
      `PackageReference`s (verified against nuget.org-resolved package list). Test project pulled
      `Microsoft.Extensions.TimeProvider.Testing` 10.9.0, `NetArchTest.Rules` 1.3.2, `Mono.Cecil`
      0.11.6 (for task 2.2), plus the same xUnit/coverlet/Test.Sdk versions
      `SmartNet.Db.Runner.Tests` already pins. `dotnet build` green on the empty scaffold.
- [x] 2.2 RED: purity/architecture-scan test — scans the compiled `SmartNet.Auth.Core` assembly's
      referenced assemblies and type usages, failing if it references `System.Data.SqlClient`,
      `Microsoft.Data.SqlClient`, or any `Microsoft.AspNetCore.*` type, and failing if IL/decompiled
      source calls `DateTime.Now`/`DateTime.UtcNow` directly. **Mechanism is an implementation
      decision** (e.g. `NetArchTest.Rules` for assembly-reference checks plus a targeted
      `Mono.Cecil`/reflection scan for the `DateTime.*` call sites, or an equivalent automated
      approach) — it must be a test that runs in CI, not a code-review promise, per spec's scenario
      "The domain-core assembly does not reference infrastructure types directly." **Written
      2026-08-16, `PurityScanTests.cs`, 5 tests**: `NetArchTest.Rules` for the three
      not-have-dependency-on assertions (`System.Data.SqlClient`, `Microsoft.Data.SqlClient`,
      `Microsoft.AspNetCore`) plus a redundant direct `Mono.Cecil` `AssemblyReferences` scan as a
      belt-and-braces check against the same three prefixes (NetArchTest already wraps Cecil
      internally, so this second test proves the mechanism doesn't depend solely on NetArchTest's
      own correctness); a fifth test walks every method body's IL instructions
      (`OpCodes.Call`/`Callvirt`) looking for a `MethodReference` whose declaring type is
      `System.DateTime` and whose name is `get_Now`/`get_UtcNow` — a real compiled-bytes scan, not a
      source-text grep (which a comment or string literal would falsely trip and an aliased call
      would falsely miss).
- [x] 2.3 Confirm 2.2 passes trivially against the still-empty project (nothing to violate yet) — this
      is the RED-before-anything-exists baseline the same way item #1's 2.1 established an empty-schema
      baseline. **Confirmed 2026-08-16**: 5/5 pass against the empty core (nothing to violate yet).
- [x] 2.4 RED: `LockoutPolicy.Adr0007` test — `UmbralFallos=5`, `DuracionBase=15min`, `Factor=2`,
      `NivelMaximo=3`. **RED confirmed 2026-08-16**: `LockoutPolicyTests.cs` — CS0234, `LockoutPolicy`
      does not exist.
- [x] 2.5 GREEN: `LockoutPolicy` record. **GREEN confirmed 2026-08-16**, 1/1 pass.
- [x] 2.6 RED: `UsuarioCredentialState` shape test — `UsuarioId`, `NombreUsuario`, `ClaveHash`,
      `IntentosFallidos`, `NivelBloqueo`, `BloqueadoHasta`, `Activo`; a construction/equality test is
      sufficient since this is a data record, not logic. **RED confirmed 2026-08-16**:
      `UsuarioCredentialStateTests.cs` — CS0234, type does not exist.
- [x] 2.7 GREEN: `UsuarioCredentialState` record. **GREEN confirmed 2026-08-16**, 2/2 pass.
- [x] 2.8 RED: `AccessPolicy.Evaluate` tests — `BloqueadoHasta` in the future ⇒ `Locked`;
      `BloqueadoHasta` `null` or in the past ⇒ not locked; exercised with `FakeTimeProvider`-supplied
      `ahora`. **RED confirmed 2026-08-16**: `AccessPolicyEvaluateTests.cs` (4 cases incl. the
      exactly-equal-to-`ahora` boundary) — CS0234, `AccessPolicy`/`AccessDecision` do not exist.
- [x] 2.9 GREEN: `AccessPolicy.Evaluate`. **GREEN confirmed 2026-08-16**, 4/4 pass.
- [x] 2.10 RED: `AccessPolicy.ApplyFailure` — the full worked sequence from design.md Decision 8,
      table-driven: failures 1–4 (no arm), failure 5 (arm at 15 min, `NivelBloqueo→1`,
      `IntentosFallidos→0`), the post-expiry margin (failure 6 does not re-lock, `NivelBloqueo`
      unchanged), failure 10 (arm at 30 min, `NivelBloqueo→2`), failure 15 (60 min, `→3`), failure 20
      (120 min, `NivelBloqueo` stays `3`, saturated), failure 25 (still 120 min, cap holds). Assert the
      duration formula reads `NivelBloqueo` **before** the saturating increment (`min(NivelBloqueo+1,
      NivelMaximo)`). **RED confirmed 2026-08-16**: `AccessPolicyApplyFailureTests.cs`, 5 tests
      written (CS0117, `ApplyFailure` does not exist) — one continuous stateful 1–25 walk
      (`FullLifetimeSequence_1Through25_MatchesAdr0007Revision4WorkedTableExactly`) plus four isolated
      tests: failures-1-4-no-arm, failure-5-arms, the read-before-increment boundary (hand-set
      `NivelBloqueo=2` must yield a 60-min lock, not 120), and cap-holds-under-repeated-pressure.
      **Formula correction found against design.md while writing this test, not pre-decided:**
      design.md Decision 8's own worked-table section states the duration formula as
      `DuracionBase × Factor^min(NivelBloqueo, NivelMaximo)` (not `NivelMaximo-1`) — verified by
      hand-tracing every row of both design.md's and ADR 0007 Revisión 4's worked tables (15→30→60→
      120→120…) against that exact formula; `min(NivelBloqueo, NivelMaximo-1)` does not reproduce the
      documented sequence (it would put the 120-min ceiling one failure early). Implemented and tested
      against the formula that actually matches the two normative tables.
- [x] 2.11 GREEN: `AccessPolicy.ApplyFailure(estado, politica, ahora)`. **GREEN confirmed
      2026-08-16**, 5/5 pass, including the full 25-failure sequence and the off-by-one guard.
- [x] 2.12 RED: `AccessPolicy.ApplySuccess` — clears all three fields: `IntentosFallidos=0`,
      `BloqueadoHasta=null`, `NivelBloqueo=0`. **RED confirmed 2026-08-16**:
      `AccessPolicyApplySuccessTests.cs` — CS0117, `ApplySuccess` does not exist.
- [x] 2.13 GREEN: `AccessPolicy.ApplySuccess`. **GREEN confirmed 2026-08-16**, 3/3 pass (clears all
      three fields; preserves identity fields; idempotent on an already-clean account).
- [x] 2.14 RED: PHC codec round-trip tests — encode/parse `$argon2id$v=19$m=19456,t=2,p=1$<salt>$<hash>`;
      a malformed PHC string; an unknown-algorithm PHC string (e.g. a legacy/foreign format) — both
      must be handled as a typed failure, never an unhandled exception, since this runs on every login
      attempt including against attacker-uncontrolled but potentially corrupted rows. **RED confirmed
      2026-08-16**: `PhcCodecTests.cs`, 13 cases — CS0246/CS0103, `PhcHash`/`PhcCodec`/`PhcParseError`
      do not exist.
- [x] 2.15 GREEN: PHC codec (pure, in `SmartNet.Auth.Core` — the "missing PHC codec is a benefit"
      framing from design.md Decision 1). **GREEN confirmed 2026-08-16**, 13/13 pass.
      `PhcParseResult` (typed success/`PhcHash` or failure/`PhcParseError`, never an exception) —
      malformed structure (wrong `$`-segment count, non-numeric `v=`/`m=,t=,p=`, unparsable base64)
      returns `PhcParseError.Malformed`; a well-formed-but-different algorithm segment (`bcrypt`,
      `pbkdf2-sha256`, even the legacy sibling `argon2i`) returns `PhcParseError.UnknownAlgorithm`.
      Neither case throws.
- [x] 2.16 GREEN: define port interfaces exactly per design.md Decision 5 —
      `IUsuarioRepository`, `ISesionRepository`, `IPasswordHasher`, `ISessionTokenFactory`. These are
      compile-time contracts with no logic of their own; there is no meaningful RED for an interface
      declaration, so this task is recorded as a direct GREEN rather than manufacturing a hollow RED —
      noted explicitly as a compression, per the hard constraint that compression must be
      acknowledged, not silently taken. **Done 2026-08-16**, compression taken as pre-authorized.
      Also added the small supporting types the interfaces reference:
      `MotivoRevocacion`/`SesionActiva` (ports over `fact.Sesion`) and `PasswordVerification`
      (`IPasswordHasher.Verify`'s typed result — `Correct`/`Incorrect`/`StoredHashUnreadable`, so a
      corrupted stored hash is distinguishable from a genuine wrong password without throwing, same
      typed-failure discipline as the PHC codec). `dotnet build` green.
- [x] 2.17 Re-run 2.2's purity scan against the complete `SmartNet.Auth.Core` (all of 2.4–2.16) —
      confirm still GREEN before Phase 3 starts building against these ports. **Re-run 2026-08-16**:
      5/5 purity-scan tests pass against the complete core (16 source files: `LockoutPolicy`,
      `UsuarioCredentialState`, `AccessDecision`, `AccessPolicy`, `PhcHash`/`PhcParseError`/
      `PhcParseResult`/`PhcCodec`, `MotivoRevocacion`, `SesionActiva`, `PasswordVerification`, and the
      four port interfaces). Full suite: `dotnet test` → 33/33 pass, 0 failures, 0 skipped.

## Phase 3: `SmartNet.Auth.Infrastructure` — adapters

- [ ] 3.1 GATE CHECK: confirm task 0.1 is closed (Konscious verified, or Decision 1 reversed in
      writing) before adding any `PackageReference` in this phase.
- [ ] 3.2 Scaffold `SmartNet/auth/SmartNet.Auth.Infrastructure` (classlib), referencing
      `SmartNet.Auth.Core` + `Microsoft.Data.SqlClient` + the verified Argon2 package; and its test
      project against `TestDatabaseFixture`.
- [ ] 3.3 RED: `IPasswordHasher` adapter tests — `Hash()` produces a PHC string parseable by Core's
      codec with `m=19456,t=2,p=1`; `Verify()` accepts the correct password and rejects an incorrect
      one; a decoy-hash generation test (random-byte PHC created once at process start, same
      parameters as real hashes) for the username-enumeration timing defense from design.md's Login
      sequence step 1.
- [ ] 3.4 GREEN: `Argon2idPasswordHasher` — wraps the raw Konscious transform; encoding/decoding
      delegates to Core's PHC codec, never reimplemented here.
- [ ] 3.5 RED: `ISessionTokenFactory` adapter tests — `Create()` returns a 256-bit CSPRNG token
      (Base64Url, 43 chars) and its lowercase-hex SHA-256; `HashOf()` is deterministic and matches
      `Create()`'s hash for the same token.
- [ ] 3.6 GREEN: `CsprngSessionTokenFactory`.
- [ ] 3.7 RED: `IUsuarioRepository` SQL adapter tests (against `TestDatabaseFixture`) —
      `FindByNameAsync` maps every column including `NivelBloqueo`; `SaveCredentialStateAsync`
      round-trips all three lockout fields in one `UPDATE` — explicitly the "a state field the UPDATE
      forgets to write" bug class design.md's Testing Strategy calls out; `UpdateClaveHashAsync`
      updates only `ClaveHash` and leaves lockout fields untouched.
- [ ] 3.8 GREEN: `SqlUsuarioRepository`.
- [ ] 3.9 RED: `ISesionRepository` SQL adapter tests — `CreateAsync` inserts a row; `FindActiveAsync`
      only returns rows where `RevocadaEn IS NULL AND ExpiraEn > @ahora`; `RenewAsync` updates
      `ExpiraEn`/`UltimaActividadEn`; `RevokeAsync` sets `RevocadaEn` + `MotivoRevocacion`;
      `RevokeAllForUsuarioAsync` revokes every live session for a user in one call.
- [ ] 3.10 GREEN: `SqlSesionRepository`.
- [ ] 3.11 RED: `ITicketStore` adapter test (`SqlSesionTicketStore`) — `StoreAsync`/`RenewAsync`/
      `RetrieveAsync`/`RemoveAsync` map onto `ISesionRepository` correctly; the persisted ticket
      payload is the 256-bit token, never the deserialized claims (design.md Decision 4).
- [ ] 3.12 GREEN: `SqlSesionTicketStore`.
- [ ] 3.13 RED: permission-sufficiency test — replay the exact SQL statements every adapter above
      issues through `ExecuteAsUserAsync("usr_api", …)` against the real grants shipped in `011`/`012`,
      confirming each succeeds under `usr_api`'s actual permission set (not an elevated test
      connection). This is the check the design's Testing Strategy names explicitly: "a missing GRANT
      would ship green" without it.
- [ ] 3.14 Confirm 3.13 is GREEN with no changes to `011`/`012`. If a statement fails under real
      grants, record the gap and fix at the schema layer (Phase 1), not by relaxing the test.

## Phase 4: `SmartNet.Api` — Minimal APIs host

- [ ] 4.1 Scaffold `SmartNet/api/SmartNet.Api` (`Microsoft.NET.Sdk.Web`, `net10.0`) and
      `SmartNet/api/SmartNet.Api.Tests` (`WebApplicationFactory` + `ProjectReference` to
      `SmartNet.Db.TestBootstrap`).
- [ ] 4.2 RED: structural test — `SmartNet.Api` has no project or package reference to
      `SmartNet.Db.Runner`, direct or transitive (design.md Decision 6: the API host must never be
      able to alter schema at boot).
- [ ] 4.3 Confirm 4.2 GREEN by construction; re-run after every later task in this phase as a
      regression guard.
- [ ] 4.4 RED: connection-string resolution test — `SMARTNET_API_DB_CONNECTION` parsed the same way
      as `RunnerOptions` (env var or explicit flag, no default, no committed fallback); absent ⇒
      startup failure with a usage message; explicitly assert the variable name is **not**
      `SMARTNET_DB_CONNECTION` (no accidental reuse of the runner's deploy-principal variable).
- [ ] 4.5 GREEN: connection-string resolution + DI wiring.
- [ ] 4.6 RED: cookie-authentication configuration test — cookie name `__Host-session`, `HttpOnly`,
      `SecurePolicy=Always`, `SameSite=Lax`, `ExpireTimeSpan=8h`, `SlidingExpiration=true` — matching
      ADR 0007 Revisión 4 and spec's cookie-attribute scenario exactly.
- [ ] 4.7 GREEN: cookie authentication middleware configuration.
- [ ] 4.8 GATE CHECK: confirm task 0.2 is closed (key-ring path decided and added to ADR 0014's
      backup set) before this task proceeds.
- [ ] 4.9 RED: Data Protection key-ring persistence test — a login issued against one
      `WebApplicationFactory` instance still authenticates against a **second, freshly constructed**
      instance pointed at the same key-ring path (simulated host restart) — this is the exact failure
      mode design.md flagged as silently defeating the reason `fact.Sesion` was chosen over
      in-memory.
- [ ] 4.10 GREEN: `PersistKeysToFileSystem(<path from 0.2>)` wiring.
- [ ] 4.11 RED: `POST /api/sesion` success — sets `__Host-session` with every mandated attribute,
      creates a `fact.Sesion` row, resets `IntentosFallidos` to `0`.
- [ ] 4.12 GREEN: `POST /api/sesion` login endpoint (Evaluate → decoy-or-real Verify → ApplyFailure/
      ApplySuccess → repositories, exactly the sequence in design.md's Data Flow).
- [ ] 4.13 RED: wrong password on an unlocked account — `IntentosFallidos` increments by exactly `1`;
      generic failure response.
- [ ] 4.14 RED: nonexistent username — response body/status indistinguishable from wrong-password.
      **Open question, not silently decided here:** the spec requires the response "does not complete
      measurably faster," which is inherently timing-sensitive and prone to flaking under a naive
      wall-clock assertion. This task should assert the *mechanism* (the decoy Argon2id verification
      path is invoked exactly once, with the real parameters, for the unknown-username case) rather
      than a raw timing comparison — flagged here as an implementation-time call, not pre-decided.
- [ ] 4.15 RED: 5th consecutive failure — `BloqueadoHasta` 15 min out, `NivelBloqueo→1`,
      `IntentosFallidos→0`.
- [ ] 4.16 RED: attempt during active lockout — rejected, and `IPasswordHasher.Verify`/the decoy path
      is never invoked (assert via a test double call-count, not just the response shape);
      `IntentosFallidos` unchanged; response shape matches the generic failure.
- [ ] 4.17 RED: escalation end-to-end — at minimum lock A (15 min) → margin (no re-lock) → lock B
      (30 min) → cap holding at a later lock, driven through the real API + DB, confirming the three
      lockout columns round-trip through the real `SaveCredentialStateAsync` call path (not just the
      Core unit test's in-memory state).
- [ ] 4.18 RED: successful login after `BloqueadoHasta` has passed — authenticates, resets
      `IntentosFallidos` and `NivelBloqueo` to `0`.
- [ ] 4.19 GREEN for 4.13–4.18: implement/adjust the endpoint until all pass. **Compression note, to
      be filled in honestly during execution:** if a single correct implementation of 4.12 already
      satisfies 4.13–4.18 without further code changes, record that here as the executed compression
      rather than manufacturing intermediate no-op commits — do not pre-authorize it before the
      round-trip is actually run.
- [ ] 4.20 RED: `DELETE /api/sesion` — revokes the `fact.Sesion` row with
      `MotivoRevocacion='CIERRE_SESION'`; a subsequent request with the same, now-stale cookie is
      unauthenticated.
- [ ] 4.21 GREEN: `DELETE /api/sesion` logout endpoint.
- [ ] 4.22 RED: `GET /api/sesion` — `200 { nombreUsuario }` when authenticated, `401` otherwise.
- [ ] 4.23 GREEN: `GET /api/sesion` probe endpoint.
- [ ] 4.24 RED: same-origin test — no CORS middleware registered, no `Access-Control-*` response
      headers ever emitted.
- [ ] 4.25 Confirm 4.24 GREEN by omission (nothing to build); record the confirmation.
- [ ] 4.26 RED: `application/problem+json` shape test — unknown user, wrong password, and locked
      account all produce byte-for-byte identical `401` problem documents.
- [ ] 4.27 GREEN/confirm 4.26 against the implementation from 4.12–4.19.

## Phase 5: `SmartNet.Admin` — reset/create/purge CLI

- [ ] 5.1 Scaffold `SmartNet/admin/SmartNet.Admin` (console, `net10.0`, `OutputType=Exe`), referencing
      `SmartNet.Auth.Core` + `SmartNet.Auth.Infrastructure`; connects via
      `SMARTNET_API_DB_CONNECTION` (runs with `usr_api`'s grants, per design.md Decision 7 — "un
      comando de la propia aplicación").
- [ ] 5.2 RED: no-echo password prompt test — the argument parser accepts no password-bearing
      argv token for any verb; the password is read only from an interactive, no-echo stdin prompt
      (e.g. `Console.ReadKey(intercept: true)` or equivalent) — argv/shell-history/process-audit
      exposure is exactly what this guards against.
- [ ] 5.3 GREEN: no-echo prompt implementation, shared across verbs.
- [ ] 5.4 RED: `usuario crear --nombre <u>` — creates a `fact.Usuario` row with an Argon2id-derived,
      PHC-encoded `ClaveHash` from the prompted password.
- [ ] 5.5 GREEN: `usuario crear` verb.
- [ ] 5.6 RED: `usuario restablecer-clave --nombre <u>` — updates `ClaveHash` via the same Argon2id
      derivation; clears all three lockout fields (`IntentosFallidos=0`, `BloqueadoHasta=NULL`,
      `NivelBloqueo=0`); calls `RevokeAllForUsuarioAsync(…, RESTABLECIMIENTO)` so every existing
      session for that user stops authenticating.
- [ ] 5.7 GREEN: `usuario restablecer-clave` verb.
- [ ] 5.8 RED: `sesion purgar --retencion-dias <n>` — deletes `fact.Sesion` rows older than the
      retention window and leaves rows within the window untouched. **Open question, carried forward
      rather than decided here:** design.md flags the default retention value itself (suggested 90
      days, as a `fact.Configuracion` key) as an operational decision, not a design one — this task
      implements the verb's mechanics against a caller-supplied `--retencion-dias`, and does not fix a
      default; if a default is required for the CLI to run with no argument, that default is a
      task-level open question to resolve at implementation time, not pre-decided here.
- [ ] 5.9 GREEN: `sesion purgar` verb (the sole `DELETE` caller in the whole permission matrix, per
      design.md Decision 3).
- [ ] 5.10 RED (spec scenario, regression guard): static scan of every `SmartNet/db/schema/*.sql`
      script for an `UPDATE` statement targeting `ClaveHash` — none exists in versioned SQL.
- [ ] 5.11 Confirm 5.10 GREEN against `011`/`012` (neither touches `ClaveHash`) — this test's job is
      to catch future drift, not to change anything now; record that explicitly, mirroring item #1's
      4.7 "not duplicated, it's a regression guard" note.

## Phase 6: Integration and CI

- [ ] 6.1 Create `SmartNet.sln` referencing all projects: `SmartNet.Db.Runner`,
      `SmartNet.Db.Runner.Tests`, `SmartNet.Db.TestBootstrap`, `SmartNet.Auth.Core`,
      `SmartNet.Auth.Core.Tests`, `SmartNet.Auth.Infrastructure`, `SmartNet.Auth.Infrastructure.Tests`,
      `SmartNet.Api`, `SmartNet.Api.Tests`, `SmartNet.Admin`, `SmartNet.Admin.Tests` — the repo has no
      solution file today.
- [ ] 6.2 Confirm `.github/workflows/ci.yml`'s existing two-job split
      (`verificaciones-estaticas` / `pruebas-de-base-de-datos`) picks up every new test project. Do
      not assume `dotnet test` at solution/directory level enumerates them automatically — check
      whether the workflow references projects by explicit path; if it does, add the new test projects
      explicitly rather than trusting silent inclusion.
- [ ] 6.3 Run the full suite (`SmartNet.Auth.Core.Tests` + `SmartNet.Auth.Infrastructure.Tests` +
      `SmartNet.Api.Tests` + `SmartNet.Admin.Tests` + the extended `SmartNet.Db.Runner.Tests`) end to
      end against fresh `fact_test_<guid>` databases. Verify afterward: 0 orphaned test databases,
      `BDSmartNet`/`master` unchanged, no `fact.Sesion`/`fact.Usuario.NivelBloqueo` artifacts left
      outside throwaway databases.
- [ ] 6.4 Verify no credential, secret, or password hash is committed anywhere in the new projects,
      fixtures, or test data — grep for literal `ClaveHash`/PHC-string values, embedded connection
      strings with passwords, and committed key-ring files.
- [ ] 6.5 Final gate: re-run `DboWriteLintTests` and `ChecksumManifestTests` against the complete,
      final `SmartNet/db/schema/` tree (`011`, `012`, both rollbacks) as the closing check before this
      change is considered done — the same two tests item #1 shipped specifically so this step is
      mechanical, not a review promise.

---

## Open questions surfaced during task decomposition (not resolved here)

- **5.8** — the default `--retencion-dias` value for `sesion purgar` (design.md suggests 90 days as an
  operational `fact.Configuracion` key but does not fix it).
- **4.14** — the exact mechanism for asserting "does not complete measurably faster" (spec's timing
  requirement); this task list requires an automatable, non-flaky proxy (decoy-path invocation count)
  rather than a raw wall-clock comparison, but the precise assertion is left to implementation.
