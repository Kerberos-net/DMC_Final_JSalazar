# Spec: Autenticación y Sesión (BACKLOG #2)

New capabilities. Describes what MUST be true after this change is applied — behavioral
contract only. No DDL, no library choice, no controller/class shape: those are design-phase
decisions per the proposal.

## Non-Goals (explicit scope boundaries)

- **No invoice/accounting-entry endpoint exists.** Out of scope per item #11.
- **No role or permission tier beyond "authenticated".** ADR 0007: single user, full access,
  no `Rol`/`UsuarioRol`.
- **No self-service or email/SMS password reset.** ADR 0007 rules this out explicitly; reset is
  an administrator-run application command only.
- **No cross-origin auth (CORS, `SameSite=None`).** ADR 0012 fixes same-origin behind the
  reverse proxy as a precondition, not a preference.

---

## Capability: `sesion-store`

### Requirement: `fact.Sesion` is the sole server-side source of truth for whether a session is valid

The system MUST track, per issued cookie, a server-side record it can revoke independent of the
client possessing the cookie value. A cookie without a corresponding valid `fact.Sesion` record
MUST NOT authenticate a request.

#### Scenario: A cookie whose backing session record no longer exists does not authenticate
- **Given** a previously issued `__Host-session` cookie whose corresponding `fact.Sesion` record
  has been revoked or deleted
- **When** a request is made carrying that cookie
- **Then** the request is treated as unauthenticated

### Requirement: `usr_worker` has no access to `fact.Sesion`

Per ADR 0003's ownership partition, `fact.Sesion` belongs to .NET's private bucket, matching the
existing `fact.Usuario` DENY pattern in `008_usuarios_y_permisos.sql`.

#### Scenario: usr_worker is denied all access to fact.Sesion
- **Given** `usr_worker` connected to the database with grants applied
- **When** `usr_worker` executes `SELECT`, `INSERT`, `UPDATE`, or `DELETE` against `fact.Sesion`
- **Then** the engine denies every one of those operations with a permission error

#### Scenario: usr_api has full SELECT/INSERT/UPDATE on fact.Sesion
- **Given** `usr_api` connected to the database with grants applied
- **When** `usr_api` executes `SELECT`, `INSERT`, and `UPDATE` against `fact.Sesion`
- **Then** every operation succeeds

---

## Capability: `api-host`

### Requirement: Same-origin deployment is a precondition, not an assumption left implicit

Per ADR 0012, the API and SPA MUST be served from the same origin behind the reverse proxy for
`SameSite=Lax` to be sufficient. The host MUST NOT rely on CORS or `withCredentials` to make
cross-origin auth work.

#### Scenario: The session cookie is not sent on a cross-origin request
- **Given** the API deployed per ADR 0012 (same origin as the SPA, behind the reverse proxy)
- **When** a request to the login-protected API originates from a different origin
- **Then** the browser does not attach the `__Host-session` cookie, and the request is
  unauthenticated

### Requirement: The session cookie matches ADR 0007 exactly

The cookie MUST be named `__Host-session`, `HttpOnly=true`, `SecurePolicy=Always`,
`SameSite=Lax`, with an 8-hour sliding expiration (ADR 0007, revisión 3).

#### Scenario: The cookie set on successful login carries every mandated attribute
- **Given** a successful login
- **When** inspecting the `Set-Cookie` header of the response
- **Then** the cookie name is `__Host-session`, `HttpOnly` is present, `Secure` is present,
  `SameSite=Lax` is present

#### Scenario: An idle-but-active session extends on use
- **Given** an authenticated session younger than 8 hours since its last activity
- **When** a request is made using that session before the 8-hour idle window (ADR 0007) elapses
- **Then** the session's expiration is extended by another 8 hours from that request

---

## Capability: `autenticacion`

### Requirement: Login with valid credentials creates a session and resets the failure counter

#### Scenario: Successful login issues a cookie, creates a session record, and resets IntentosFallidos
- **Given** `fact.Usuario` has an active, non-locked account with a known correct password
- **When** login is attempted with the correct `NombreUsuario` and password
- **Then** the response sets the `__Host-session` cookie
- **And** a corresponding `fact.Sesion` row is created
- **And** `fact.Usuario.IntentosFallidos` for that account is `0`

### Requirement: A failed login, when not locked, increments the failure counter and reveals nothing about account existence

#### Scenario: Wrong password on an unlocked account increments the counter with a generic response
- **Given** `fact.Usuario.IntentosFallidos` is less than `4` for an active, unlocked account
- **When** login is attempted with the correct `NombreUsuario` and an incorrect password
- **Then** `fact.Usuario.IntentosFallidos` for that account increments by exactly `1`
- **And** the response is the same generic failure shape as an unknown username (ADR 0007: "no
  revela si el usuario existe")

#### Scenario: A nonexistent username produces the same response shape and timing class as a wrong password
- **Given** no `fact.Usuario` row exists for a given `NombreUsuario`
- **When** login is attempted with that `NombreUsuario` and any password
- **Then** the response body/status is indistinguishable from the wrong-password case
- **And** the response does not complete measurably faster than a wrong-password attempt against
  an existing account (no early-exit timing tell)

### Requirement: The 5th consecutive failure locks the account for 15 minutes and arms `NivelBloqueo` (ADR 0007 Revisión 4)

Per ADR 0007 Revisión 4, "Límite de intentos": "Cinco fallos consecutivos arman un bloqueo,"
with a first-offense duration of 15 minutes read from `NivelBloqueo = 0` before it advances.
The 5th consecutive failed attempt MUST set `BloqueadoHasta` to 15 minutes from that attempt,
MUST reset `fact.Usuario.IntentosFallidos` to `0` (arming, not expiry, is the reset event — ADR
0007 Revisión 4: "`IntentosFallidos` ... se resetea al armar el bloqueo, no al expirar"), and
MUST increment `fact.Usuario.NivelBloqueo` from `0` to `1`.

#### Scenario: The 5th consecutive failure sets BloqueadoHasta 15 minutes out and arms NivelBloqueo
- **Given** `fact.Usuario.IntentosFallidos` is `4` and `fact.Usuario.NivelBloqueo` is `0` for an
  active, unlocked account
- **When** login is attempted with an incorrect password
- **Then** `fact.Usuario.IntentosFallidos` becomes `0` (reset at arming, ADR 0007 Revisión 4)
- **And** `fact.Usuario.NivelBloqueo` becomes `1`
- **And** `fact.Usuario.BloqueadoHasta` is set to 15 minutes after the attempt's timestamp
  (ADR 0007 Revisión 4, worked table: "fallo 5 | 0 | 1 | 15 min")

### Requirement: A login attempt while `BloqueadoHasta` is still in the future is rejected without evaluating the password

This is distinct from an ordinary failed attempt: no credential comparison (and therefore no
Argon2id computation) occurs, and the rejection is timing-safe with respect to the account-exists
question.

#### Scenario: An attempt during an active lockout is rejected before password verification
- **Given** `fact.Usuario.BloqueadoHasta` for an account is in the future
- **When** login is attempted for that account with any password, including the correct one
- **Then** the system rejects the attempt without invoking password/hash verification
- **And** `fact.Usuario.IntentosFallidos` does not change
- **And** the response shape matches the generic failure response used elsewhere in this
  capability (does not reveal lockout state to the caller)

### Requirement: An attempt after `BloqueadoHasta` has passed is evaluated as a fresh attempt, but escalation survives in `NivelBloqueo`

Per ADR 0007 Revisión 4, lockout is not silently permanent. A successful login after expiry
resets both `IntentosFallidos` and `NivelBloqueo` to zero, same as any other success. A failure
after expiry is not treated as if no lockout had ever happened: `NivelBloqueo` "solo vuelve a
cero con un éxito o con el comando de restablecimiento" (ADR 0007 Revisión 4) — it is untouched
by expiry — so a new lockout trigger reads the account's advanced `NivelBloqueo` and produces a
longer duration than the previous lockout, per the fixed sequence 15 → 30 → 60 → 120 minutes.

#### Scenario: A successful login after BloqueadoHasta has passed resets IntentosFallidos and NivelBloqueo and clears lockout
- **Given** `fact.Usuario.BloqueadoHasta` for an account is in the past and `fact.Usuario.NivelBloqueo`
  is greater than `0` from a previous lockout
- **When** login is attempted with the correct password
- **Then** the account authenticates normally
- **And** `fact.Usuario.IntentosFallidos` is reset to `0`
- **And** `fact.Usuario.NivelBloqueo` is reset to `0` (ADR 0007 Revisión 4: "cualquier éxito | 0 | 0
  | olvidado")

#### Scenario: The escalation sequence 15 → 30 → 60 → 120 minutes, matching ADR 0007 Revisión 4's worked table exactly
- **Given** a fresh account with `NivelBloqueo = 0`, and no successful login occurs anywhere in this
  sequence
- **When** the account accumulates its 5th consecutive failure (lock A)
- **Then** `fact.Usuario.BloqueadoHasta` is set 15 minutes out and `fact.Usuario.NivelBloqueo` becomes
  `1` (ADR 0007 Revisión 4, worked table: "fallo 5 | 0 | 1 | 15 min")
- **When**, after lock A expires, one further failure is attempted (the margin) and it does not
  re-lock the account
- **Then** `fact.Usuario.IntentosFallidos` becomes `1`, `fact.Usuario.NivelBloqueo` remains `1`, and
  `fact.Usuario.BloqueadoHasta` stays in the past (ADR 0007 Revisión 4, worked table: "fallo 6 tras
  expirar (el margen) | 1 | 1 | ninguno")
- **When** failures continue to accumulate to a 10th lifetime failure (lock B)
- **Then** `fact.Usuario.BloqueadoHasta` is set 30 minutes out and `fact.Usuario.NivelBloqueo` becomes
  `2` (ADR 0007 Revisión 4, worked table: "fallo 10 | 0 | 2 | 30 min")
- **When**, after lock B expires and its margin is exhausted, failures accumulate to a 15th
  lifetime failure (lock C)
- **Then** `fact.Usuario.BloqueadoHasta` is set 60 minutes out and `fact.Usuario.NivelBloqueo` becomes
  `3` (ADR 0007 Revisión 4, worked table: "fallo 15 | 0 | 3 | 60 min")
- **When**, after lock C expires and its margin is exhausted, failures accumulate to a 20th
  lifetime failure (lock D)
- **Then** `fact.Usuario.BloqueadoHasta` is set 120 minutes out and `fact.Usuario.NivelBloqueo`
  remains `3` (ADR 0007 Revisión 4, worked table: "fallo 20 | 0 | 3 (techo) | 120 min")

#### Scenario: Lockout duration never exceeds 120 minutes once NivelBloqueo is saturated
- **Given** `fact.Usuario.NivelBloqueo` is already `3` (the saturation ceiling) for an account,
  from a previous lock arming
- **When** the account's failures accumulate to another new lockout trigger, and this repeats
  across multiple further lock-arm events
- **Then** every one of those lock-arm events sets `fact.Usuario.BloqueadoHasta` to exactly 120
  minutes out, never more
- **And** `fact.Usuario.NivelBloqueo` remains `3` after each of those events — it does not
  increase past the ceiling (ADR 0007 Revisión 4: "El techo hace la secuencia no decreciente, no
  estrictamente creciente")

#### Scenario: After a lock expires, the account gets a fresh five-failure margin, but a re-lock's duration reflects the preserved NivelBloqueo, not a reset to first-offense
- **Given** an account whose most recent lockout armed at `NivelBloqueo = 2` (a 30-minute lock,
  now expired) and whose `fact.Usuario.IntentosFallidos` was reset to `0` when that lock armed
- **When** up to four further failed login attempts are made after the lock's expiry
- **Then** none of those four attempts re-locks the account — `fact.Usuario.IntentosFallidos`
  counts `1` through `4` and `fact.Usuario.BloqueadoHasta` stays in the past throughout (ADR 0007
  Revisión 4: "tras expirar un bloqueo, la cuenta recibe el mismo margen de cinco fallos ...
  antes de volver a bloquearse")
- **When** a 5th failed attempt is then made, exhausting the margin
- **Then** the account re-locks, and the new `fact.Usuario.BloqueadoHasta` reflects a 60-minute
  duration — the duration that corresponds to the account's preserved `NivelBloqueo = 2`
  advancing to `3`, not a reset to the first-offense 15-minute duration (ADR 0007 Revisión 4:
  "si vuelve a bloquearse, la duración seguirá creciendo donde se quedó, no reinicia a 15 minutos
  como si fuera la primera vez")
- **And** `fact.Usuario.NivelBloqueo` becomes `3`

### Requirement: Logout revokes the specific `fact.Sesion` record

#### Scenario: Logout revokes the session and the old cookie no longer authenticates
- **Given** an authenticated session backed by a `fact.Sesion` row
- **When** logout is invoked for that session
- **Then** the corresponding `fact.Sesion` row is revoked (design decides delete vs. status flag,
  but it MUST stop being treated as valid)
- **And** a subsequent request presenting the same, now-stale cookie is unauthenticated

---

## Capability: `restablecimiento-clave`

### Requirement: Password reset is an application command, never a manual `UPDATE`, and applies Argon2id

Per ADR 0007: "la única funcionalidad de administración del sistema" — a written, administrator-run
procedure that invokes the application, applying the same derivation used at login.

#### Scenario: Reset applies Argon2id derivation to the new password
- **Given** an administrator invokes the reset command for an existing account with a new
  password
- **When** the command completes
- **Then** `fact.Usuario.ClaveHash` is updated to a PHC-encoded Argon2id hash of the new password
- **And** the new password authenticates on the next login attempt

#### Scenario: No versioned SQL statement updates ClaveHash outside the application command
- **Given** the versioned migration/SQL source as committed to the repository
- **When** inspecting every `UPDATE` statement targeting `fact.Usuario.ClaveHash`
- **Then** no such statement exists in versioned SQL — the only writer of that column is the
  application's reset command

#### Scenario: Reset clears lockout state on the affected account
- **Given** an account with `IntentosFallidos > 0`, and/or `BloqueadoHasta` in the future, and/or
  `NivelBloqueo > 0`
- **When** the reset command completes successfully for that account
- **Then** `IntentosFallidos` is `0`, `BloqueadoHasta` is `NULL`, and `NivelBloqueo` is `0` — all
  three lockout fields, matching a successful login's own clearing behavior (ADR 0007 Revisión 4:
  `NivelBloqueo` "solo vuelve a cero con un éxito o con el comando de restablecimiento")

> **Rationale, not a gap.** ADR 0007 does not name lockout-clearing explicitly, but it does state
> the reset command is administrator-invoked and is "the only administration functionality" —
> leaving a locked account still locked after an administrator-supervised password reset would
> contradict the reset's purpose (restoring access) and is decided here rather than left to
> design, since it is a direct, unavoidable consequence of the stated intent, not a new number.

---

## Capability: `nucleo-dominio` (ADR 0019 purity)

### Requirement: Login and lockout decision logic is a pure function, structurally verifiable

Per ADR 0019, the core MUST be expressible as a pure function over (stored credential/lockout
state, submitted credential, current time) → (outcome, new credential/lockout state), with no
direct dependency on a database driver, ASP.NET Core, or the system clock.

#### Scenario: The domain-core assembly does not reference infrastructure types directly
- **Given** the compiled assembly containing the login/lockout decision logic
- **When** scanning its referenced assemblies and type usages
- **Then** it does not reference `System.Data.SqlClient`, `Microsoft.Data.SqlClient`,
  `Microsoft.AspNetCore.*`, or call `DateTime.Now`/`DateTime.UtcNow` directly — time MUST be
  received as a parameter

#### Scenario: The decision logic is exercised by tests with zero DB/HTTP/clock dependencies
- **Given** the test suite for the login/lockout domain core
- **When** running those tests
- **Then** they execute without a database connection, without an HTTP server, and without
  reading the real system clock
