# Proposal: Autenticación y Sesión (BACKLOG #2)

## Intent

Nothing beyond schema (#1) can run over HTTP without an authenticated caller. This change stands
up the minimal ASP.NET Core host and the full login/logout/lockout/reset vertical slice, so that
later items (starting with #11) have an API to attach to and a session model to trust. It delivers
authentication only — no invoice or accounting surface.

## Scope

### In Scope
- New `fact.Sesion` table (server-side session/ticket store) — schema surface only; exact DDL is a
  design-phase decision. GRANT/DENY entries for it, matching the existing `fact.Usuario` pattern in
  `008_usuarios_y_permisos.sql` or a new numbered script (design-phase call).
- Minimal ASP.NET Core host under `SmartNet/` (exact folder name is design-phase), cookie
  authentication per ADR 0007 (`__Host-session`, `HttpOnly`, `SecurePolicy=Always`, `SameSite=Lax`,
  8h sliding expiration), wired to the new session store so logout is a real server-side
  revocation, not just a cookie clear.
- Login endpoint: verifies credentials against `fact.Usuario.ClaveHash` (PHC-encoded, Argon2id),
  enforces lockout (`IntentosFallidos`/`BloqueadoHasta`), resets the counter on success, and never
  reveals whether a username exists.
- Logout endpoint: revokes the server-side session record.
- Lockout enforcement: 5 consecutive failures → 15 min, growing on repeats, enforced in the
  application per ADR 0007.
- Reset-as-command: the concrete shape (CLI vs admin-only endpoint) is a design-phase decision;
  what's fixed here is that it is an application command applying the same Argon2id derivation,
  never a manual `UPDATE` — "the only administration functionality of the system" (ADR 0007).
- Pure, infrastructure-free login/lockout domain core (ADR 0019): testable without DB, HTTP, or
  clock.

### Out of Scope
- Any invoice or accounting-entry endpoint (item #11 and later).
- Any UI/frontend — not part of this backlog item.
- Multi-role authorization — ADR 0007 already rules this out (single user, full access).
- Password reset via email/SMS/self-service — ADR 0007 already rules this out; reset is an
  administrator-run command, not a user-facing flow.
- General-purpose auth framework: no OAuth/OIDC, no MFA.

## Capabilities

### New Capabilities
- `sesion-store`: `fact.Sesion` schema and its `usr_api`/`usr_worker` grants.
- `api-host`: minimal ASP.NET Core host, cookie-auth middleware, DI/config scaffolding.
- `autenticacion`: login, logout, lockout enforcement, credential verification.
- `restablecimiento-clave`: the reset-as-command procedure.

### Modified Capabilities
None — `fact.Usuario` schema itself is unchanged; this change only adds consumers and a new table.

## Approach

Cookie-based session auth per ADR 0007, backed by a new SQL-Server session store so revocation is
durable across app restarts (single-instance deployment per ADR 0012 makes in-memory tempting, but
"logout really revokes" must survive a restart — this is why a table, not `IDistributedCache`, was
chosen). Password verification and lockout arithmetic live in a pure domain core per ADR 0019;
infrastructure (EF/ADO, HTTP, clock) is injected at the edges only. Argon2id is fixed as the KDF;
the specific .NET library is a design-phase choice. `fact.Sesion` DDL, its exact grants file, the
API host's folder name/target framework, and the reset command's concrete surface are all
design-phase decisions — this proposal fixes only that they exist and their behavioral contract.

## Affected Areas

| Area | Impact | Description |
|------|--------|--------------|
| `SmartNet/db/schema/` | New | `fact.Sesion` table + grants (new script or extends `008`) |
| `SmartNet/api/` (name TBD) | New | ASP.NET Core host: `Program.cs`, cookie-auth, login/logout endpoints |
| Domain core (new project) | New | Pure login/lockout/reset logic, ADR 0019-compliant |

## Risks

| Risk | Likelihood | Mitigation |
|------|------------|------------|
| Argon2id library choice adds an unvetted third-party dependency | Medium | Design phase must justify the pick (maintenance, licensing, .NET 10 compat) |
| Session-store design (table shape, cleanup of expired rows) done carelessly could leak or bloat | Medium | Design phase must define expiry/cleanup, not just insert/lookup |
| No API host exists yet — first-time scaffolding decisions (target framework, project layout) could drift from Runner's `net10.0` convention | Low | Design phase should confirm alignment explicitly, not invent silently |
| Lockout/reset logic implemented inside a controller instead of the pure core would violate ADR 0019 and be hard to test | Medium | Spec/design must require the core project boundary before tasks are cut |

## Rollback Plan

`fact.Sesion` and its grants are additive schema in `fact` only — a compensating migration drops
the table and grants without touching `fact.Usuario` or any `dbo.*` object. The API host is a new,
isolated project; removing it (or reverting its commit) does not affect the schema runner or
existing tests. No data migration exists at this stage, so rollback carries no data-loss risk.

## Dependencies

- Item #1 (schema, complete — commit `cf60715`).
- Design-phase decision on the Argon2id .NET library before implementation starts.

## Success Criteria

- [ ] Login with valid credentials sets the `__Host-session` cookie and creates a `fact.Sesion` row.
- [ ] Logout revokes the `fact.Sesion` row; the cookie no longer authenticates.
- [ ] 5 consecutive failed logins lock the account for 15 min; a 6th within that window is rejected
      without querying the password.
- [ ] Invalid username and invalid password return indistinguishable responses.
- [ ] Reset command applies Argon2id derivation and is never a manual `UPDATE`.
- [ ] Login/lockout domain core has tests with zero DB/HTTP/clock dependencies (ADR 0019).
- [ ] `usr_worker` is denied on `fact.Sesion` exactly as it is on `fact.Usuario`.

## Proposal question round

The three decisions below were already made by the user before this proposal was written and are
**not** reopened here:

1. Password hashing algorithm: **Argon2id** (library choice deferred to design).
2. Server-side session store: **new `fact.Sesion` table**, versioned SQL, own grants (exact DDL
   deferred to design).
3. Scope includes standing up the minimal API host (`SmartNet/api/` or equivalent) as part of this
   item — item #11 remains invoice/accounting endpoints only.

One item remains open for confirmation, carried over from exploration and not resolved by the
three decisions above:

- **API project layout/target framework.** Exploration found no existing decision beyond "likely
  `net10.0` to match `SmartNet.Db.Runner`, likely under `SmartNet/api/`" — nothing committed
  anywhere. Assumption used in this proposal: the API host targets `net10.0` and lives under
  `SmartNet/`, exact subfolder name to be fixed at design time. Flag if this assumption is wrong;
  otherwise design phase proceeds on it.
